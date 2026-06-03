// NT8ChartBridge.cs · Phase 2 · L2 MarketDepth AddOn
//
// 设计 (5-24):
//   - 独立 HTTP port 8775
//   - MarketDepth 实时订阅 → 6 L2 特征计算 → HTTP /l2_features
//   - 单 symbol: MNQ 06-26 (agent_l2_config 表配置)
//   - 不需要 PG 连接 (纯内存计算)
//
// 部署门槛 (跟其它 AddOn 同):
//   - token 复用 C:\proj\nt8-relay\addon-token.txt
//   - netsh http add urlacl url=http://+:8775/ user=Everyone (一次性 admin)
//   - 防火墙 New-NetFirewallRule -DisplayName "NT8 ChartBridge 8775" -LocalPort 8775 -Protocol TCP -Action Allow
//   - NT8 F5 编译 + 完整 restart
//
// 健康检查:
//   curl http://192.168.10.30:8775/health
//   curl http://192.168.10.30:8775/l2_features?symbol=MNQ%2006-26

#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
    public class NT8ChartBridge : AddOnBase
    {
        private const int    PORT         = 8775;
        private const string TOKEN_FILE   = @"C:\proj\nt8-relay\addon-token.txt";
        private const string DEBUG_LOG    = @"C:\proj\nt8-relay\chartbridge-debug.log";

        // 监控的 instrument (agent_l2_config 表可用后从 PG 读 · V1 硬编码)
        private static readonly string[] SUBSCRIBE_SYMBOLS = new[] { "MNQ 06-26" };

        // L2 快照深度 (top N levels)
        private const int L2_DEPTH = 10;

        // delta 窗口 (10s 滚动)
        private static readonly TimeSpan DELTA_WINDOW = TimeSpan.FromSeconds(10);

        private NT8Common _common;

        // ───────── L2 order book per symbol ─────────

        private class L2Level
        {
            public double Price;
            public long   Volume;
            public int    Orders;
        }

        private class L2Book
        {
            public SortedDictionary<double, long> Bids = new SortedDictionary<double, long>(Comparer<double>.Create((a, b) => -a.CompareTo(b)));  // desc price
            public SortedDictionary<double, long> Asks = new SortedDictionary<double, long>();  // asc price
            public DateTime LastUpdateUtc = DateTime.MinValue;

            public double BestBid => Bids.Count > 0 ? Bids.First().Key : 0;
            public double BestAsk => Asks.Count > 0 ? Asks.First().Key : 0;
            public double Spread => (BestAsk > 0 && BestBid > 0) ? BestAsk - BestBid : 0;

            // Rollup top N levels
            public double BidVolumeTopN(int n)
            {
                double sum = 0; int i = 0;
                foreach (var kv in Bids) { if (i++ >= n) break; sum += kv.Value; }
                return sum;
            }

            public double AskVolumeTopN(int n)
            {
                double sum = 0; int i = 0;
                foreach (var kv in Asks) { if (i++ >= n) break; sum += kv.Value; }
                return sum;
            }

            public double MinLevelVolTopN(int n)
            {
                double min = double.MaxValue; int i = 0;
                foreach (var kv in Bids) { if (i++ >= n) break; if (kv.Value < min) min = kv.Value; }
                i = 0;
                foreach (var kv in Asks) { if (i++ >= n) break; if (kv.Value < min) min = kv.Value; }
                return min == double.MaxValue ? 0 : min;
            }

            public double AvgLevelVolTopN(int n)
            {
                double sum = 0; int cnt = 0; int i = 0;
                foreach (var kv in Bids) { if (i++ >= n) break; sum += kv.Value; cnt++; }
                i = 0;
                foreach (var kv in Asks) { if (i++ >= n) break; sum += kv.Value; cnt++; }
                return cnt > 0 ? sum / cnt : 0;
            }
        }

        // ───────── Delta tracking (10s rolling window) ─────────

        private class VolumeSnapshot
        {
            public DateTime Ts;
            public double   Volume;
        }

        private class L2State
        {
            public L2Book Book = new L2Book();
            public List<VolumeSnapshot> BidSnapshots = new List<VolumeSnapshot>();
            public List<VolumeSnapshot> AskSnapshots = new List<VolumeSnapshot>();
            public DateTime LastDeltaPrune = DateTime.UtcNow;

            // Computed features (cached for HTTP read)
            public double DepthImbalance;
            public double TopOfBookRatio;
            public double Spread;
            public double DeltaBid;
            public double DeltaAsk;
            public double LiquidityVacuum;
            public DateTime FeaturesComputedAt = DateTime.MinValue;
        }

        private readonly Dictionary<string, L2State> _states = new Dictionary<string, L2State>();
        private readonly Dictionary<string, EventHandler<MarketDepthEventArgs>> _handlers = new Dictionary<string, EventHandler<MarketDepthEventArgs>>();
        private readonly object _l2Lock = new object();

        // ───────── Broker connection state ─────────

        private bool   _connHooked      = false;
        private bool   _initialSubDone  = false;
        private int    _resubInProgress = 0;
        private DateTime _lastResubUtc  = DateTime.MinValue;
        private static readonly TimeSpan _resubMinInterval = TimeSpan.FromSeconds(60);
        private string _connName        = "?";
        private string _connStatus      = "Unknown";

        // ───────── Lifecycle ─────────

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NT8ChartBridge";
                Description = "MarketDepth L2 → 6 features via HTTP (Phase 2)";
            }
            else if (State == State.Configure)
            {
                _common = new NT8Common(PORT, TOKEN_FILE, DEBUG_LOG, "[NT8ChartBridge]");
                _common.LoadToken();
                _common.StartHttpServer(HandleRequest);

                // 等 broker connection 稳定再 SubscribeAll
                Task.Run(async () =>
                {
                    int waited = 0;
                    while (waited < 30)
                    {
                        try
                        {
                            bool ready = Connection.Connections != null
                                && Connection.Connections.Any(c => c.Status == ConnectionStatus.Connected
                                                                  && c.PriceStatus == ConnectionStatus.Connected);
                            if (ready)
                            {
                                HookConnectionEvents();
                                _common.Log("broker stable · subscribing to " + SUBSCRIBE_SYMBOLS.Length + " symbols");
                                await Task.Delay(2000);
                                SubscribeAll();
                                return;
                            }
                        }
                        catch { }
                        await Task.Delay(1000);
                        waited++;
                    }
                    _common.Log("WARN broker not ready after 30s · subscribe anyway");
                    HookConnectionEvents();
                    SubscribeAll();
                });

                _common.Log($"NT8ChartBridge started v1.0 · {SUBSCRIBE_SYMBOLS.Length} symbols · 8775");
            }
            else if (State == State.Terminated)
            {
                if (_common != null)
                {
                    UnhookConnectionEvents();
                    UnsubscribeAll();
                    _common.StopHttpServer();
                    _common.Log("NT8ChartBridge stopped");
                }
            }
        }

        // ───────── HTTP routes ─────────

        private async Task HandleRequest(HttpListenerContext ctx)
        {
            await Task.Yield();
            string path = ctx.Request.Url.AbsolutePath;
            var query = ctx.Request.QueryString;

            if (path == "/health")
            {
                _common.SendJson(ctx, 200, new
                {
                    status = "ok",
                    version = "NT8ChartBridge/1.0",
                    ts = DateTime.UtcNow.ToString("o"),
                    symbols = SUBSCRIBE_SYMBOLS,
                });
            }
            else if (path == "/l2_features")
            {
                string symbol = query["symbol"];
                if (string.IsNullOrEmpty(symbol)) { _common.SendJson(ctx, 400, new { error = "symbol required" }); return; }

                L2State st;
                lock (_l2Lock)
                {
                    if (!_states.TryGetValue(symbol, out st))
                    { _common.SendJson(ctx, 404, new { error = "symbol not subscribed", symbol }); return; }
                }

                double spread, depthImbalance, topOfBookRatio, deltaBid, deltaAsk, liqVac;
                DateTime computedAt;
                lock (_l2Lock)
                {
                    spread = st.Spread;
                    depthImbalance = st.DepthImbalance;
                    topOfBookRatio = st.TopOfBookRatio;
                    deltaBid = st.DeltaBid;
                    deltaAsk = st.DeltaAsk;
                    liqVac = st.LiquidityVacuum;
                    computedAt = st.FeaturesComputedAt;
                }

                _common.SendJson(ctx, 200, new
                {
                    symbol,
                    ts = computedAt == DateTime.MinValue ? null : computedAt.ToString("o"),
                    depth_imbalance = Math.Round(depthImbalance, 2),
                    top_of_book_ratio = Math.Round(topOfBookRatio, 4),
                    spread = Math.Round(spread, 2),
                    delta_bid = Math.Round(deltaBid, 2),
                    delta_ask = Math.Round(deltaAsk, 2),
                    liquidity_vacuum = Math.Round(liqVac, 4),
                });
            }
            else if (path == "/l2_snapshot")
            {
                string symbol = query["symbol"];
                if (string.IsNullOrEmpty(symbol)) { _common.SendJson(ctx, 400, new { error = "symbol required" }); return; }

                L2State st;
                lock (_l2Lock)
                {
                    if (!_states.TryGetValue(symbol, out st))
                    { _common.SendJson(ctx, 404, new { error = "symbol not subscribed", symbol }); return; }
                }

                var bids = new List<object>();
                var asks = new List<object>();
                lock (_l2Lock)
                {
                    int i = 0;
                    foreach (var kv in st.Book.Bids) { if (i++ >= L2_DEPTH) break; bids.Add(new { price = kv.Key, volume = kv.Value }); }
                    i = 0;
                    foreach (var kv in st.Book.Asks) { if (i++ >= L2_DEPTH) break; asks.Add(new { price = kv.Key, volume = kv.Value }); }
                }

                _common.SendJson(ctx, 200, new
                {
                    symbol,
                    ts = DateTime.UtcNow.ToString("o"),
                    bids,
                    asks,
                });
            }
            else if (path == "/status")
            {
                var snapshots = new List<object>();
                lock (_l2Lock)
                {
                    foreach (var kv in _states)
                    {
                        snapshots.Add(new
                        {
                            symbol = kv.Key,
                            bid_depth = kv.Value.Book.Bids.Count,
                            ask_depth = kv.Value.Book.Asks.Count,
                            spread = kv.Value.Spread,
                            last_update_utc = kv.Value.Book.LastUpdateUtc == DateTime.MinValue ? null : kv.Value.Book.LastUpdateUtc.ToString("o"),
                        });
                    }
                }
                _common.SendJson(ctx, 200, new { symbols = snapshots });
            }
            else
            {
                _common.SendJson(ctx, 404, new { error = "unknown path", path });
            }
        }

        // ───────── MarketDepth subscription ─────────

        private void SubscribeAll()
        {
            foreach (var sym in SUBSCRIBE_SYMBOLS)
                SubscribeOne(sym);
            _initialSubDone = true;
        }

        private void SubscribeOne(string symbol)
        {
            try
            {
                var instr = Instrument.GetInstrument(symbol);
                if (instr == null) { _common.Log($"subscribe FAIL: instrument '{symbol}' not found"); return; }

                lock (_l2Lock)
                {
                    if (_states.ContainsKey(symbol))
                    {
                        _common.Log($"subscribe skip: {symbol} already subscribed");
                        return;
                    }
                    _states[symbol] = new L2State();
                }

                EventHandler<MarketDepthEventArgs> handler = (s, e) => OnL2Update(symbol, e);
                instr.MarketDepth.Update += handler;

                lock (_l2Lock) { _handlers[symbol] = handler; }
                _common.Log($"subscribed to MarketDepth: {symbol}");
            }
            catch (Exception ex)
            {
                _common.Log($"subscribe EX ({symbol}): {ex.Message}");
            }
        }

        private void UnsubscribeAll()
        {
            foreach (var sym in SUBSCRIBE_SYMBOLS)
            {
                try
                {
                    var instr = Instrument.GetInstrument(sym);
                    EventHandler<MarketDepthEventArgs> handler;
                    lock (_l2Lock)
                    {
                        if (!_handlers.TryGetValue(sym, out handler)) continue;
                        _handlers.Remove(sym);
                    }
                    if (instr != null && instr.MarketDepth != null)
                        instr.MarketDepth.Update -= handler;
                }
                catch { }
            }
            lock (_l2Lock) { _states.Clear(); }
        }

        // ───────── L2 update handler ─────────

        private void OnL2Update(string symbol, MarketDepthEventArgs e)
        {
            try
            {
                L2State st;
                lock (_l2Lock)
                {
                    if (!_states.TryGetValue(symbol, out st)) return;
                }

                var book = st.Book;
                var dict = e.MarketDataType == MarketDataType.Bid ? book.Bids : book.Asks;

                lock (_l2Lock)
                {
                    // MarketDepthOperation enum not available in AddOn API — use int values
                    // 0=Insert, 1=Update, 2=Remove
                    int op = (int)e.Operation;
                    if (op == 0 || op == 1)  // Insert or Update
                    {
                        dict[e.Price] = e.Volume;
                    }
                    else if (op == 2)  // Remove
                    {
                        dict.Remove(e.Price);
                    }
                    book.LastUpdateUtc = DateTime.UtcNow;

                    // Recompute features
                    RecomputeFeatures(st);
                }
            }
            catch (Exception ex)
            {
                _common.Log($"OnL2Update EX ({symbol}): {ex.Message}");
            }
        }

        // ───────── Feature computation ─────────

        private void RecomputeFeatures(L2State st)
        {
            var book = st.Book;
            double bidVol = book.BidVolumeTopN(5);
            double askVol = book.AskVolumeTopN(5);
            double total = bidVol + askVol;

            // 1. depth_imbalance (0-100)
            st.DepthImbalance = total > 0 ? 100.0 * askVol / total : 50.0;

            // 2. top_of_book_ratio
            double bid0 = book.Bids.Count > 0 ? book.Bids.First().Value : 0;
            double ask0 = book.Asks.Count > 0 ? book.Asks.First().Value : 1;
            st.TopOfBookRatio = ask0 > 0 ? bid0 / ask0 : (bid0 > 0 ? 999 : 1);

            // 3. spread
            st.Spread = book.Spread;

            // 4/5. delta_bid / delta_ask (10s rolling window)
            double totalBid = book.BidVolumeTopN(L2_DEPTH);
            double totalAsk = book.AskVolumeTopN(L2_DEPTH);
            DateTime now = DateTime.UtcNow;

            st.BidSnapshots.Add(new VolumeSnapshot { Ts = now, Volume = totalBid });
            st.AskSnapshots.Add(new VolumeSnapshot { Ts = now, Volume = totalAsk });

            // Prune older than 10s
            DateTime cutoff = now - DELTA_WINDOW;
            st.BidSnapshots.RemoveAll(s => s.Ts < cutoff);
            st.AskSnapshots.RemoveAll(s => s.Ts < cutoff);

            if (st.BidSnapshots.Count >= 2)
                st.DeltaBid = st.BidSnapshots.Last().Volume - st.BidSnapshots.First().Volume;
            if (st.AskSnapshots.Count >= 2)
                st.DeltaAsk = st.AskSnapshots.Last().Volume - st.AskSnapshots.First().Volume;

            // 6. liquidity_vacuum
            double avg = book.AvgLevelVolTopN(L2_DEPTH);
            double min = book.MinLevelVolTopN(L2_DEPTH);
            st.LiquidityVacuum = avg > 0 ? min / avg : 1.0;

            st.FeaturesComputedAt = now;
        }

        // ───────── Connection recovery ─────────

        private void HookConnectionEvents()
        {
            try
            {
                if (_connHooked) return;
                Connection.ConnectionStatusUpdate += OnBrokerConnectionStatusUpdate;
                _connHooked = true;

                // Log initial connection state
                try
                {
                    var conn = Connection.Connections?.FirstOrDefault();
                    if (conn != null) { try { _connName = conn.Options?.Name ?? "?"; } catch { _connName = "?"; } _connStatus = conn.Status.ToString(); }
                }
                catch { }
                _common.Log($"[broker] hooked ConnectionStatusUpdate · current: {_connName} {_connStatus}");
            }
            catch (Exception ex)
            {
                _common.Log("HookConnectionEvents EX: " + ex.Message);
            }
        }

        private void UnhookConnectionEvents()
        {
            try
            {
                if (_connHooked)
                {
                    Connection.ConnectionStatusUpdate -= OnBrokerConnectionStatusUpdate;
                    _connHooked = false;
                }
            }
            catch { }
        }

        private void OnBrokerConnectionStatusUpdate(object sender, ConnectionStatusEventArgs e)
        {
            try
            {
                try { _connName = e.Connection.Options?.Name ?? "?"; } catch { }
                _connStatus = e.Status.ToString();

                // RECOVERY edge: disconnected → connected
                if (e.Status == ConnectionStatus.Connected && _initialSubDone)
                {
                    if (DateTime.UtcNow - _lastResubUtc < _resubMinInterval) return;
                    if (Interlocked.CompareExchange(ref _resubInProgress, 1, 0) != 0) return;

                    _common.Log("[broker] RECOVERY detected · resubscribing MarketDepth");
                    _lastResubUtc = DateTime.UtcNow;
                    Task.Run(async () =>
                    {
                        try
                        {
                            UnsubscribeAll();
                            await Task.Delay(3000);
                            SubscribeAll();
                            _common.Log("[broker] resubscribe DONE");
                        }
                        catch (Exception rex)
                        {
                            _common.Log("[broker] resubscribe EX: " + rex.Message);
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _resubInProgress, 0);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _common.Log("OnBrokerConnectionStatusUpdate EX: " + ex.Message);
            }
        }
    }
}
