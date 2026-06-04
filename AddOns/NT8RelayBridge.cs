// NT8RelayBridge.cs · v0.3 精简版（拆分后保留核心 endpoint）
//
// 拆分历史（2026-05-01）：
//   - push 流 / quote 订阅 / quote endpoint 搬到 NT8EventPusher.cs (8770)
//   - SMC BC oracle 搬到 NT8OracleValidator.cs (8768)
//   - PG 直写 backfill 搬到 NT8DataBackfill.cs (8767)
//   - 公共 helper 抽到 NT8Common.cs
//
// 当前职责（精简后）：
//   - HTTP 8766 监听
//   - GET /health /accounts /account /positions /orders_active /instrument_meta
//   - 纯只读 endpoint，不订阅事件，不 push 流，不依赖 Npgsql
//
// 部署：
//   1. SCP NT8Common.cs + 4 个 AddOn .cs 到 NT8 P51
//      C:\Users\<user>\Documents\NinjaTrader 8\bin\Custom\AddOns\
//      (NT8Common.cs 也放 AddOns/，NinjaScript 编译同 namespace 互访)
//   2. 写 token 到 C:\proj\nt8-relay\addon-token.txt
//   3. 一次性 url 预约（管理员 PowerShell）：
//        netsh http add urlacl url=http://+:8766/ user=Everyone
//        netsh http add urlacl url=http://+:8767/ user=Everyone   (NT8DataBackfill)
//        netsh http add urlacl url=http://+:8768/ user=Everyone   (NT8OracleValidator)
//        netsh http add urlacl url=http://+:8770/ user=Everyone   (NT8EventPusher)
//   4. 防火墙规则（4 个端口各开一条）
//   5. NT8 F5 编译（菜单 Tools → Edit NinjaScript → AddOn → 任一 AddOn → F5）
//   6. 重启 NT8 加载所有 AddOn
//
// 历史保留（v0.3 12h fix）：4-29 timestamp_ny 12h offset 修复（搬到 NT8OracleValidator）
//
// 4 个坑（保留作文档参考）：
//   ✓ 用 AddOnBase 不是 Indicator（看全部账户）
//   ✓ Position 是 net position（用 MarketPosition 标 Long/Short/Flat 区分方向）
//   ✓ Account.All 遍历多账户，不硬编码
//   ✓ PnL 用 GetUnrealizedProfitLoss(PerformanceUnit.Currency)，不自己 (close-avg)*qty

#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
    public class NT8RelayBridge : AddOnBase
    {
        private const int    PORT       = 8766;
        private const string TOKEN_FILE = @"C:\proj\nt8-relay\addon-token.txt";
        private const string DEBUG_LOG  = @"C:\proj\nt8-relay\addon-debug.log";

        private NT8Common _common;

        // ───────── Lifecycle ─────────

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "NT8RelayBridge";
                Description = "Read-only NT8 endpoint (health/accounts/position/orders) for CC integration";
            }
            else if (State == State.Configure)
            {
                _common = new NT8Common(PORT, TOKEN_FILE, DEBUG_LOG, "[NT8RelayBridge]");
                _common.LoadToken();
                _common.StartHttpServer(HandleRequest);
                _common.Log("AddOn started v0.3 (精简版 · " + Account.All.Count + " accounts visible)");
            }
            else if (State == State.Terminated)
            {
                if (_common != null)
                {
                    _common.StopHttpServer();
                    _common.Log("AddOn stopped");
                }
            }
        }

        // ───────── HTTP 路由 ─────────

        private async Task HandleRequest(HttpListenerContext ctx)
        {
            await Task.Yield();
            string path = ctx.Request.Url.AbsolutePath;
            var query = ctx.Request.QueryString;

            if (path == "/health")
            {
                _common.SendJson(ctx, 200, new
                {
                    status   = "ok",
                    version  = "AddOn/0.3-h1ctx",
                    h1_context_endpoint = true,
                    ttfm_context_endpoint = true,
                    ts       = DateTime.UtcNow.ToString("o"),
                    accounts = Account.All.Count,
                });
            }
            else if (path == "/accounts")
            {
                _common.SendJson(ctx, 200, new { accounts = Account.All.Select(a => a.Name).ToArray() });
            }
            else if (path == "/account")
            {
                var acct = NT8Common.FindAccount(query["name"]);
                if (acct == null) { _common.SendJson(ctx, 404, new { error = "account not found", name = query["name"] }); return; }
                _common.SendJson(ctx, 200, NT8Common.BuildAccountSnapshot(acct));
            }
            else if (path == "/positions")
            {
                var acct = NT8Common.FindAccount(query["account"]);
                if (acct == null) { _common.SendJson(ctx, 404, new { error = "account not found", account = query["account"] }); return; }
                _common.SendJson(ctx, 200, new { positions = acct.Positions.Select(NT8Common.BuildPositionSnapshot).ToArray() });
            }
            else if (path == "/orders_active")
            {
                var acct = NT8Common.FindAccount(query["account"]);
                if (acct == null) { _common.SendJson(ctx, 404, new { error = "account not found", account = query["account"] }); return; }
                var active = acct.Orders.Where(NT8Common.IsActiveOrder).Select(NT8Common.BuildOrderSnapshot).ToArray();
                _common.SendJson(ctx, 200, new { orders = active });
            }
            else if (path == "/instrument_meta")
            {
                var name = query["name"];
                if (string.IsNullOrEmpty(name)) { _common.SendJson(ctx, 400, new { error = "name required" }); return; }
                var instr = NinjaTrader.Cbi.Instrument.GetInstrument(name);
                if (instr == null || instr.MasterInstrument == null)
                {
                    _common.SendJson(ctx, 404, new { error = "instrument not found", name = name });
                    return;
                }
                var mi = instr.MasterInstrument;
                _common.SendJson(ctx, 200, new
                {
                    name              = name,
                    master_instrument = mi.Name,
                    full_name         = instr.FullName,
                    tick_size         = mi.TickSize,
                    point_value       = mi.PointValue,
                    tick_value        = mi.TickSize * mi.PointValue,
                    currency          = mi.Currency.ToString(),
                    instrument_type   = mi.InstrumentType.ToString(),
                    expiry            = instr.Expiry.ToString("yyyy-MM-dd"),
                });
            }
            else if (path == "/bars")
            {
                // GET /bars?symbol=MNQ 06-26&tf=5&count=500
                // tf=分钟数 (1/5/15/60/240/1440/10080/43200) · count=1..5000 · 默认 tf=5, count=500
                var symbol = query["symbol"];
                if (string.IsNullOrEmpty(symbol)) { _common.SendJson(ctx, 400, new { error = "symbol required" }); return; }

                int tf = 5, count = 500;
                int.TryParse(query["tf"], out tf);
                int.TryParse(query["count"], out count);
                if (tf <= 0) tf = 5;
                if (count <= 0 || count > 5000) count = 500;

                var instrResult = NinjaTrader.Cbi.Instrument.GetInstrument(symbol);
                if (instrResult == null) { _common.SendJson(ctx, 404, new { error = "instrument not found", symbol = symbol }); return; }

                // 使用 barsBack overload（NT8RealtimePush 验证模式）
                BarsRequest br = null;
                try
                {
                    br = new BarsRequest(instrResult, count);
                    br.BarsPeriod = TfToBarsPeriod(tf);
                    try { br.TradingHours = TradingHours.Get("Default 24 x 7"); } catch { }
                    br.MergePolicy = MergePolicy.MergeBackAdjusted;

                    var done = new ManualResetEvent(false);
                    Bars rb = null;
                    string fetchErr = null;

                    br.Request(new Action<BarsRequest, ErrorCode, string>((reqResult, errCode, errMsg) =>
                    {
                        try
                        {
                            if (errCode != ErrorCode.NoError) fetchErr = "ErrorCode=" + errCode + " msg=" + errMsg;
                            else rb = reqResult.Bars;
                        }
                        finally { done.Set(); }
                    }));

                    if (!done.WaitOne(30000))
                    {
                        _common.SendJson(ctx, 504, new { error = "BarsRequest timeout (30s)", symbol = symbol });
                        return;
                    }
                    if (fetchErr != null) { _common.SendJson(ctx, 502, new { error = fetchErr, symbol = symbol }); return; }
                    if (rb == null) { _common.SendJson(ctx, 502, new { error = "BarsRequest returned null", symbol = symbol }); return; }

                    // 组装 JSON 数组
                    var barsOut = new List<object>(rb.Count);
                    for (int i = 0; i < rb.Count; i++)
                    {
                        barsOut.Add(new
                        {
                            ts     = rb.GetTime(i).ToString("o"),
                            open   = rb.GetOpen(i),
                            high   = rb.GetHigh(i),
                            low    = rb.GetLow(i),
                            close  = rb.GetClose(i),
                            volume = rb.GetVolume(i),
                        });
                    }

                    _common.SendJson(ctx, 200, new
                    {
                        symbol      = symbol,
                        tf_minutes  = tf,
                        count       = rb.Count,
                        bars        = barsOut,
                    });
                }
                catch (Exception ex)
                {
                    _common.SendJson(ctx, 500, new { error = "BarsRequest ex: " + ex.Message, symbol = symbol });
                }
                finally
                {
                    try { br?.Dispose(); } catch { }
                }
            }
            else if (path == "/h1_context")
            {
                var key = query["key"];
                if (string.IsNullOrEmpty(key))
                    key = query["symbol"];
                if (string.IsNullOrEmpty(key))
                {
                    _common.SendJson(ctx, 400, new { error = "key or symbol required" });
                    return;
                }

                ICT_H1_Context h1;
                if (!ICT_Context_Bus.TryGetH1(key, out h1))
                {
                    if (!TryReadH1ContextSnapshot(key, out h1))
                    {
                        _common.SendJson(ctx, 404, new { error = "h1 context not found", key = key, bus_keys = ICT_Context_Bus.GetH1Keys() });
                        return;
                    }
                    ICT_Context_Bus.PublishH1(h1.Key, h1);
                }

                _common.SendJson(ctx, 200, BuildH1ContextPayload(h1));
            }
            else if (path == "/h1_context_keys")
            {
                var key = query["key"];
                if (!string.IsNullOrEmpty(key))
                {
                    ICT_H1_Context h1;
                    if (!ICT_Context_Bus.TryGetH1(key, out h1) && TryReadH1ContextSnapshot(key, out h1))
                        ICT_Context_Bus.PublishH1(h1.Key, h1);
                }

                _common.SendJson(ctx, 200, new
                {
                    keys = ICT_Context_Bus.GetH1Keys(),
                    snapshot_file = ResolveH1ContextSnapshotPath(),
                    snapshot_exists = File.Exists(ResolveH1ContextSnapshotPath())
                });
            }
            else if (path == "/ttfm_context")
            {
                var key = query["key"];
                if (string.IsNullOrEmpty(key))
                    key = query["symbol"];
                if (string.IsNullOrEmpty(key))
                {
                    _common.SendJson(ctx, 400, new { error = "key or symbol required" });
                    return;
                }

                TTFM_Context context;
                if (!TTFM_Context_Bus.TryGet(key, out context))
                {
                    if (!TryReadTTFMContextSnapshot(key, out context))
                    {
                        _common.SendJson(ctx, 404, new { error = "ttfm context not found", key = key, bus_keys = TTFM_Context_Bus.GetKeys() });
                        return;
                    }
                    TTFM_Context_Bus.Publish(context.Key, context);
                }

                _common.SendJson(ctx, 200, BuildTTFMContextPayload(context));
            }
            else if (path == "/ttfm_context_keys")
            {
                var key = query["key"];
                if (!string.IsNullOrEmpty(key))
                {
                    TTFM_Context context;
                    if (!TTFM_Context_Bus.TryGet(key, out context) && TryReadTTFMContextSnapshot(key, out context))
                        TTFM_Context_Bus.Publish(context.Key, context);
                }

                _common.SendJson(ctx, 200, new
                {
                    keys = TTFM_Context_Bus.GetKeys(),
                    snapshot_file = ResolveTTFMContextSnapshotPath(),
                    snapshot_exists = File.Exists(ResolveTTFMContextSnapshotPath())
                });
            }
            else
            {
                _common.SendJson(ctx, 404, new
                {
                    error = "unknown path",
                    path = path,
                    hint = "endpoint moved? quote/push → 8770 NT8EventPusher · backfill → 8767 NT8DataBackfill · oracle → 8768 NT8OracleValidator"
                });
            }
        }
        // ───────── 工具方法 ─────────

        private static BarsPeriod TfToBarsPeriod(int tfMinutes)
        {
            if (tfMinutes >= 43200) return new BarsPeriod { BarsPeriodType = BarsPeriodType.Month, Value = tfMinutes / 43200 };
            if (tfMinutes >= 10080) return new BarsPeriod { BarsPeriodType = BarsPeriodType.Week,  Value = tfMinutes / 10080 };
            if (tfMinutes >= 1440)  return new BarsPeriod { BarsPeriodType = BarsPeriodType.Day,   Value = tfMinutes / 1440 };
            return new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = tfMinutes };
        }

        private static object BuildH1ContextPayload(ICT_H1_Context h1)
        {
            return new
            {
                key = h1.Key,
                instrument = h1.Instrument,
                published_at = h1.PublishedAt.ToString("o"),
                bar_time = h1.BarTime.ToString("o"),
                current_price = h1.CurrentPrice,
                bias = h1.Bias,
                confidence = h1.Confidence,
                allowed_direction = h1.AllowedDirection.ToString(),
                dealing_range_high = h1.DealingRangeHigh,
                dealing_range_mid = h1.DealingRangeMid,
                dealing_range_low = h1.DealingRangeLow,
                long_ote_top = h1.LongOteTop,
                long_ote_bottom = h1.LongOteBottom,
                short_ote_top = h1.ShortOteTop,
                short_ote_bottom = h1.ShortOteBottom,
                nearest_bull_zone_top = h1.NearestBullZoneTop,
                nearest_bull_zone_bottom = h1.NearestBullZoneBottom,
                nearest_bear_zone_top = h1.NearestBearZoneTop,
                nearest_bear_zone_bottom = h1.NearestBearZoneBottom,
                nearest_key_above = h1.NearestKeyAbove,
                nearest_key_below = h1.NearestKeyBelow,
                location = h1.Location,
                narrative = h1.Narrative,
            };
        }

        private static object BuildTTFMContextPayload(TTFM_Context context)
        {
            return new
            {
                key = context.Key,
                instrument = context.Instrument,
                profile = context.Profile,
                context_tf_minutes = context.ContextTfMinutes,
                published_at = context.PublishedAt.ToString("o"),
                bar_time = context.BarTime.ToString("o"),
                current_price = context.CurrentPrice,
                bias = context.Bias,
                confidence = context.Confidence,
                allowed_direction = context.AllowedDirection.ToString(),
                setup_status = context.SetupStatus.ToString(),
                setup_phase = context.SetupPhase,
                setup_id = context.SetupId,
                setup_age_bars = context.SetupAgeBars,
                failure_reason = context.FailureReason,
                child_context_key = context.ChildContextKey,
                child_tf_minutes = context.ChildTfMinutes,
                child_bar_time = context.ChildBarTime.ToString("o"),
                child_tf_state = context.ChildTfState,
                child_parent_candle_number = context.ChildParentCandleNumber,
                child_parent_candle_start = context.ChildParentCandleStartTime.ToString("o"),
                child_parent_candle_end = context.ChildParentCandleEndTime.ToString("o"),
                child_parent_candle = new
                {
                    number = context.ChildParentCandleNumber,
                    start = context.ChildParentCandleStartTime.ToString("o"),
                    end = context.ChildParentCandleEndTime.ToString("o"),
                    open = context.ChildParentCandleOpen,
                    high = context.ChildParentCandleHigh,
                    low = context.ChildParentCandleLow,
                    close = context.ChildParentCandleClose,
                    inside = context.IsInsideParentCandleWindow
                },
                ai = new
                {
                    interface_version = context.AiInterfaceVersion,
                    setup_gate = context.AiSetupGate,
                    setup_gate_reason = context.AiSetupGateReason
                },
                sequence_direction = context.SequenceDirection,
                current_candle_number = context.CurrentCandleNumber,
                tspot_touched = context.TSpotTouched,
                tspot_violated = context.TSpotViolated,
                protected_swing_broken = context.ProtectedSwingBroken,
                cisd_enabled = context.CisdEnabled,
                cisd_confirmed = context.CisdConfirmed,
                cisd_minutes = context.CisdMinutes,
                cisd_direction = context.CisdDirection,
                cisd_level = context.CisdLevel,
                cisd_time = context.CisdTime.ToString("o"),
                cisd_age_bars = context.CisdAgeBars,
                distance_to_cisd_ticks = context.DistanceToCisdTicks,
                distance_to_tspot_ticks = context.DistanceToTSpotTicks,
                distance_to_protected_ticks = context.DistanceToProtectedTicks,
                location = context.Location,
                c1 = new
                {
                    time = context.C1Time.ToString("o"),
                    open = context.C1Open,
                    high = context.C1High,
                    low = context.C1Low,
                    close = context.C1Close
                },
                c2 = new
                {
                    time = context.C2Time.ToString("o"),
                    open = context.C2Open,
                    high = context.C2High,
                    low = context.C2Low,
                    close = context.C2Close
                },
                c3 = new
                {
                    time = context.C3Time.ToString("o"),
                    open = context.C3Open,
                    high = context.C3High,
                    low = context.C3Low,
                    close = context.C3Close,
                    eq = context.C3Eq
                },
                c4 = new
                {
                    time = context.C4Time.ToString("o"),
                    open = context.C4Open,
                    high = context.C4High,
                    low = context.C4Low,
                    close = context.C4Close
                },
                c5 = new
                {
                    time = context.C5Time.ToString("o"),
                    open = context.C5Open,
                    high = context.C5High,
                    low = context.C5Low,
                    close = context.C5Close
                },
                c6 = new
                {
                    time = context.C6Time.ToString("o"),
                    open = context.C6Open,
                    high = context.C6High,
                    low = context.C6Low,
                    close = context.C6Close
                },
                t_spot_upper = context.TSpotUpper,
                t_spot_lower = context.TSpotLower,
                protected_swing = context.ProtectedSwing,
                invalidation_price = context.InvalidationPrice,
                roi_source = context.RoiSource,
                roi_htf_minutes = context.RoiHtfMinutes,
                roi_active_bias = context.RoiActiveBias,
                nearest_bull_zone_top = context.NearestBullZoneTop,
                nearest_bull_zone_bottom = context.NearestBullZoneBottom,
                nearest_bull_zone_ce = context.NearestBullZoneCE,
                nearest_bull_zone_start = context.NearestBullZoneStartTime.ToString("o"),
                bull_roi = new
                {
                    top = context.NearestBullZoneTop,
                    bottom = context.NearestBullZoneBottom,
                    ce = context.NearestBullZoneCE,
                    start = context.NearestBullZoneStartTime.ToString("o")
                },
                nearest_bear_zone_top = context.NearestBearZoneTop,
                nearest_bear_zone_bottom = context.NearestBearZoneBottom,
                nearest_bear_zone_ce = context.NearestBearZoneCE,
                nearest_bear_zone_start = context.NearestBearZoneStartTime.ToString("o"),
                bear_roi = new
                {
                    top = context.NearestBearZoneTop,
                    bottom = context.NearestBearZoneBottom,
                    ce = context.NearestBearZoneCE,
                    start = context.NearestBearZoneStartTime.ToString("o")
                },
                narrative = context.Narrative,
            };
        }

        private static bool TryReadH1ContextSnapshot(string key, out ICT_H1_Context context)
        {
            context = null;
            string path = ResolveH1ContextSnapshotPath();
            if (!File.Exists(path))
                return false;

            Dictionary<string, string> data = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(path))
            {
                int idx = line.IndexOf('=');
                if (idx <= 0)
                    continue;
                data[line.Substring(0, idx)] = line.Substring(idx + 1);
            }

            string snapshotKey;
            if (!data.TryGetValue("key", out snapshotKey) || snapshotKey != key)
                return false;

            context = new ICT_H1_Context
            {
                Key = snapshotKey,
                Instrument = GetString(data, "instrument"),
                PublishedAt = ParseDate(GetString(data, "published_at")),
                BarTime = ParseDate(GetString(data, "bar_time")),
                CurrentPrice = ParseDouble(GetString(data, "price")),
                Bias = ParseInt(GetString(data, "bias")),
                Confidence = ParseInt(GetString(data, "confidence")),
                AllowedDirection = ParseDirection(GetString(data, "allowed_direction")),
                Location = GetString(data, "location"),
                DealingRangeHigh = ParseDouble(GetString(data, "dr_high")),
                DealingRangeMid = ParseDouble(GetString(data, "dr_mid")),
                DealingRangeLow = ParseDouble(GetString(data, "dr_low")),
                LongOteTop = ParseDouble(GetString(data, "long_ote_top")),
                LongOteBottom = ParseDouble(GetString(data, "long_ote_bottom")),
                ShortOteTop = ParseDouble(GetString(data, "short_ote_top")),
                ShortOteBottom = ParseDouble(GetString(data, "short_ote_bottom")),
                NearestBullZoneTop = ParseDouble(GetString(data, "nearest_bull_zone_top")),
                NearestBullZoneBottom = ParseDouble(GetString(data, "nearest_bull_zone_bottom")),
                NearestBearZoneTop = ParseDouble(GetString(data, "nearest_bear_zone_top")),
                NearestBearZoneBottom = ParseDouble(GetString(data, "nearest_bear_zone_bottom")),
                NearestKeyAbove = ParseDouble(GetString(data, "nearest_key_above")),
                NearestKeyBelow = ParseDouble(GetString(data, "nearest_key_below")),
                Narrative = GetString(data, "narrative")
            };
            return true;
        }

        private static bool TryReadTTFMContextSnapshot(string key, out TTFM_Context context)
        {
            context = null;
            string path = ResolveTTFMContextSnapshotPath();
            if (!File.Exists(path))
                return false;

            Dictionary<string, string> data = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(path))
            {
                int idx = line.IndexOf('=');
                if (idx <= 0)
                    continue;
                data[line.Substring(0, idx)] = line.Substring(idx + 1);
            }

            string snapshotKey;
            if (!data.TryGetValue("key", out snapshotKey) || snapshotKey != key)
                return false;

            context = new TTFM_Context
            {
                Key = snapshotKey,
                Instrument = GetString(data, "instrument"),
                Profile = GetString(data, "profile"),
                ContextTfMinutes = ParseInt(GetString(data, "context_tf_minutes")),
                PublishedAt = ParseDate(GetString(data, "published_at")),
                BarTime = ParseDate(GetString(data, "bar_time")),
                CurrentPrice = ParseDouble(GetString(data, "price")),
                Bias = ParseInt(GetString(data, "bias")),
                Confidence = ParseInt(GetString(data, "confidence")),
                AllowedDirection = ParseDirection(GetString(data, "allowed_direction")),
                SetupStatus = ParseTTFMStatus(GetString(data, "setup_status")),
                SetupPhase = GetString(data, "setup_phase"),
                SetupId = ParseInt(GetString(data, "setup_id")),
                SetupAgeBars = ParseInt(GetString(data, "setup_age_bars")),
                FailureReason = GetString(data, "failure_reason"),
                ChildContextKey = GetString(data, "child_context_key"),
                ChildTfMinutes = ParseInt(GetString(data, "child_tf_minutes")),
                ChildBarTime = ParseDate(GetString(data, "child_bar_time")),
                ChildParentCandleNumber = ParseInt(GetString(data, "child_parent_candle_number")),
                ChildParentCandleStartTime = ParseDate(GetString(data, "child_parent_candle_start")),
                ChildParentCandleEndTime = ParseDate(GetString(data, "child_parent_candle_end")),
                ChildParentCandleHigh = ParseDouble(GetString(data, "child_parent_candle_high")),
                ChildParentCandleLow = ParseDouble(GetString(data, "child_parent_candle_low")),
                ChildParentCandleOpen = ParseDouble(GetString(data, "child_parent_candle_open")),
                ChildParentCandleClose = ParseDouble(GetString(data, "child_parent_candle_close")),
                IsInsideParentCandleWindow = ParseBool(GetString(data, "is_inside_parent_candle_window")),
                AiInterfaceVersion = GetString(data, "ai_interface_version"),
                AiSetupGate = ParseBool(GetString(data, "ai_setup_gate")),
                AiSetupGateReason = GetString(data, "ai_setup_gate_reason"),
                SequenceDirection = ParseInt(GetString(data, "sequence_direction")),
                CurrentCandleNumber = ParseInt(GetString(data, "current_candle_number")),
                TSpotTouched = ParseBool(GetString(data, "tspot_touched")),
                TSpotViolated = ParseBool(GetString(data, "tspot_violated")),
                ProtectedSwingBroken = ParseBool(GetString(data, "protected_swing_broken")),
                CisdEnabled = ParseBool(GetString(data, "cisd_enabled")),
                CisdConfirmed = ParseBool(GetString(data, "cisd_confirmed")),
                CisdMinutes = ParseInt(GetString(data, "cisd_minutes")),
                CisdDirection = ParseInt(GetString(data, "cisd_direction")),
                CisdLevel = ParseDouble(GetString(data, "cisd_level")),
                CisdTime = ParseDate(GetString(data, "cisd_time")),
                CisdAgeBars = ParseInt(GetString(data, "cisd_age_bars")),
                DistanceToCisdTicks = ParseDouble(GetString(data, "distance_to_cisd_ticks")),
                DistanceToTSpotTicks = ParseDouble(GetString(data, "distance_to_tspot_ticks")),
                DistanceToProtectedTicks = ParseDouble(GetString(data, "distance_to_protected_ticks")),
                Location = GetString(data, "location"),
                C1High = ParseDouble(GetString(data, "c1_high")),
                C1Low = ParseDouble(GetString(data, "c1_low")),
                C1Open = ParseDouble(GetString(data, "c1_open")),
                C1Close = ParseDouble(GetString(data, "c1_close")),
                C1Time = ParseDate(GetString(data, "c1_time")),
                C2High = ParseDouble(GetString(data, "c2_high")),
                C2Low = ParseDouble(GetString(data, "c2_low")),
                C2Open = ParseDouble(GetString(data, "c2_open")),
                C2Close = ParseDouble(GetString(data, "c2_close")),
                C2Time = ParseDate(GetString(data, "c2_time")),
                C3High = ParseDouble(GetString(data, "c3_high")),
                C3Low = ParseDouble(GetString(data, "c3_low")),
                C3Open = ParseDouble(GetString(data, "c3_open")),
                C3Close = ParseDouble(GetString(data, "c3_close")),
                C3Eq = ParseDouble(GetString(data, "c3_eq")),
                C3Time = ParseDate(GetString(data, "c3_time")),
                C4High = ParseDouble(GetString(data, "c4_high")),
                C4Low = ParseDouble(GetString(data, "c4_low")),
                C4Open = ParseDouble(GetString(data, "c4_open")),
                C4Close = ParseDouble(GetString(data, "c4_close")),
                C4Time = ParseDate(GetString(data, "c4_time")),
                C5High = ParseDouble(GetString(data, "c5_high")),
                C5Low = ParseDouble(GetString(data, "c5_low")),
                C5Open = ParseDouble(GetString(data, "c5_open")),
                C5Close = ParseDouble(GetString(data, "c5_close")),
                C5Time = ParseDate(GetString(data, "c5_time")),
                C6High = ParseDouble(GetString(data, "c6_high")),
                C6Low = ParseDouble(GetString(data, "c6_low")),
                C6Open = ParseDouble(GetString(data, "c6_open")),
                C6Close = ParseDouble(GetString(data, "c6_close")),
                C6Time = ParseDate(GetString(data, "c6_time")),
                TSpotUpper = ParseDouble(GetString(data, "t_spot_upper")),
                TSpotLower = ParseDouble(GetString(data, "t_spot_lower")),
                ProtectedSwing = ParseDouble(GetString(data, "protected_swing")),
                InvalidationPrice = ParseDouble(GetString(data, "invalidation_price")),
                RoiSource = GetString(data, "roi_source"),
                RoiHtfMinutes = ParseInt(GetString(data, "roi_htf_minutes")),
                RoiActiveBias = ParseInt(GetString(data, "roi_active_bias")),
                NearestBullZoneTop = ParseDouble(GetString(data, "nearest_bull_zone_top")),
                NearestBullZoneBottom = ParseDouble(GetString(data, "nearest_bull_zone_bottom")),
                NearestBullZoneCE = ParseDouble(GetString(data, "nearest_bull_zone_ce")),
                NearestBullZoneStartTime = ParseDate(GetString(data, "nearest_bull_zone_start")),
                NearestBearZoneTop = ParseDouble(GetString(data, "nearest_bear_zone_top")),
                NearestBearZoneBottom = ParseDouble(GetString(data, "nearest_bear_zone_bottom")),
                NearestBearZoneCE = ParseDouble(GetString(data, "nearest_bear_zone_ce")),
                NearestBearZoneStartTime = ParseDate(GetString(data, "nearest_bear_zone_start")),
                Narrative = GetString(data, "narrative")
            };
            return true;
        }

        private static string ResolveH1ContextSnapshotPath()
        {
            string dir = Core.Globals.UserDataDir;
            if (string.IsNullOrWhiteSpace(dir))
                dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(dir, "h1_context_snapshot.txt");
        }

        private static string ResolveTTFMContextSnapshotPath()
        {
            string dir = Core.Globals.UserDataDir;
            if (string.IsNullOrWhiteSpace(dir))
                dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(dir, "ttfm_context_snapshot.txt");
        }

        private static string GetString(Dictionary<string, string> data, string key)
        {
            string value;
            return data.TryGetValue(key, out value) ? value : string.Empty;
        }

        private static double ParseDouble(string value)
        {
            double result;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : double.NaN;
        }

        private static int ParseInt(string value)
        {
            int result;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : 0;
        }

        private static DateTime ParseDate(string value)
        {
            DateTime result;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result) ? result : Core.Globals.MinDate;
        }

        private static ICTContextDirection ParseDirection(string value)
        {
            ICTContextDirection result;
            return Enum.TryParse<ICTContextDirection>(value, out result) ? result : ICTContextDirection.None;
        }

        private static bool ParseBool(string value)
        {
            bool result;
            return bool.TryParse(value, out result) && result;
        }

        private static TTFMSetupStatus ParseTTFMStatus(string value)
        {
            TTFMSetupStatus result;
            return Enum.TryParse<TTFMSetupStatus>(value, out result) ? result : TTFMSetupStatus.Unknown;
        }
    }
}
