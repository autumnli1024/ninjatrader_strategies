#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;          // SimpleFont
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows;                  // TextAlignment
#endregion

// ============================================================================
//  TimeCycles - Liquidity Sweep Detector  (NT8 port of TV v10.3)
//  Patrick  2026-03-07
//
//  闁衡偓绾懐绠婚悷鏇氳兌閸嬶綁鏁嶉崼銏＄ゲ閻庨潧婀遍鍥ㄧ▔閳ь剟鎮ч崼顒傜:
//   1. 闁稿繈鍔戦崕鎾偨閵婏絺鍋撶仦鐐槯闂傚倸鐡ㄩ崺?+ 濞寸娀鏀遍悧鎼佸Υ瀹ュ懏缍忛柡宥呮川閺侀箖鏁?闁?濞戞挸绉堕弫?barsBack闁挎稑鐬奸崵搴㈢▔瀹ュ棛纾介柨?
//   2. 闁告绮敮鈧柟闈涱儏婵晠寮捄鍝勯殬閺夌儐鍓氬畷鏌ユ晬瀹€鈧ú鍧楀箳閵壯勬殢 NT8 闁?Time[0]闁挎稑鐗嗛崙锟犲及椤栨碍绂堥悶娑栧妽濠€浼村捶閻楀牊顦ч梻鍌濇彧缁?
//   3. CycleData 閻忓繋娴囬ˉ濠囧箥閳ь剟寮垫径瀣Ж闁告稏鍔嶅﹢锟犳偐閼哥鍋撴笟濠勭闂侇偅妲掔欢顐€掗崨顔界彴
//   4. RemoveDrawObject 闁?new 闁稿繑濞婇弫顓犫偓娑欘殕椤掓粎娑甸鈧导鍕嫊閽樺鍞ㄧ紒顐ヮ嚙閹捇宕ュ鍡樼厵闁?
// ============================================================================

namespace NinjaTrader.NinjaScript.Indicators
{
    // ========================================================================
    //  婵絽绻嬮柌婊冾啅閹绘帞鏆氶柟瀛樺姇閹冲棝寮甸悢鐑樼暠闁轰胶澧楀畵浣衡偓鍦嚀濞?
    // ========================================================================
    internal class CycleData
    {
        public double   PchValue;
        public double   PclValue;
        public DateTime PchTime;        // 闁哄牃鍋撳Δ鍌浢奸悳?K 缂佺偓瀵у鍌炴晸?
        public DateTime PclTime;        // 闁哄牃鍋撳ù锝呯凹閻?K 缂佺偓瀵у鍌炴晸?
        public DateTime CycleStart;     // 闁告稏鍔嶅﹢鈩冿純閺嶃劎澹孠缂佺偓瀵у鍌炴晸?
        public DateTime CycleEnd;       // 闁告稏鍔嶅﹢锛勭磼閹惧瓨灏嗛柡鍐ㄧ埣濡潡鏁嶉崼鐔哥厐闁告稏鍔嶅﹢鈩冿純閺嶃劎澹孠缂佺偓瀵у鍌炴⒒鏉堝墽绀?

        // -1 = 濞寸姴瀛╁﹢顓犳喆閿旂虎娼鹃柨? 0 = 閻熸瑱濡囬～顐﹀籍閸撲焦鐣?CurrentBar 缂傚倹鐗曡ぐ?
        public int  PchSweepBar = -1;
        public int  PclSweepBar = -1;
        public bool PchIsSweep;
        public bool PclIsSweep;

        // NT8 缂備焦锚濞存鈧數顢婇挅?tag闁挎稑鐗嗛悺褏绮敂鑳洬闁哥儐鍨粩鎾煥椤曞棛绀?
        public string TagPchLine;
        public string TagPclLine;
        public string TagCeLine;
        public string TagPchLabel;
        public string TagPclLabel;
        public string TagSweepPch;
        public string TagSweepPcl;
        public string TagDealingRange;
        public string TagDealingLabel;
    }

    // ========================================================================
    //  闁圭娲﹂悥锝嗙▔鐠佸磭绉?
    // ========================================================================
    public class TimeCyclesLiquiditySweep : Indicator
    {
        // 闁冲厜鍋撻柍鍏夊亾 閺夆晜鍔橀、鎴﹀籍閸撲礁笑闁?闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾
        private readonly List<CycleData> _cycles = new List<CycleData>();
        private int _seq;

        // 鐟滅増鎸告晶鐘诲嫉椤忓嫮鏆氶柟瀛樺姇閹冲棝鏁?
        private double   _curHigh, _curLow;
        private DateTime _curHighTime, _curLowTime, _curCycleStart;
        private bool     _curInit;
        private string   _tagCurH, _tagCurL;

        private int _lastPeriodIndex = int.MinValue;

        private Series<double> _lastCycleHighSeries;
        private Series<double> _lastCycleLowSeries;
        private Series<double> _currentCycleHighSeries;
        private Series<double> _currentCycleLowSeries;
        private Series<double> _pchSweepSignalSeries;
        private Series<double> _pclSweepSignalSeries;
        private Series<double> _bosSignalSeries;
        private Series<double> _dealingRangeHighSeries;
        private Series<double> _dealingRangeLowSeries;
        private Series<double> _dealingRangeMidSeries;
        private Series<double> _inPremiumSeries;
        private Series<double> _inDiscountSeries;

        // 闁冲厜鍋撻柍鍏夊亾 tag 闁汇垻鍠愰崹?闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾
        private string T(string p)
        {
            return p + "_" + (++_seq);
        }

        // 闁冲厜鍋撻柍鍏夊亾 DashStyle 闁哄嫮濮撮惃?闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾
        private static DashStyleHelper DS(string s)
        {
            switch (s)
            {
                case "Solid": return DashStyleHelper.Solid;
                case "Dash":  return DashStyleHelper.Dash;
                default:      return DashStyleHelper.Dot;
            }
        }

        // 闁冲厜鍋撻柍鍏夊亾 闁告稏鍔嶅﹢锛勬閵忕姷绌块悹渚婄磿閻ｅ鏁嶉崼銏＄函闁规亽鍎抽弫銈夊嫉椤掆偓濠€鎾籍閸洘锛熼柨娑樻湰濡倝妫侀埀顒勫籍鐠哄搫闅橀弶鐑嗗墯瀹曟煡鏁嶆径鍡樻晿闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾
        private int PeriodIndex(DateTime t)
        {
            var baseToday = new DateTime(t.Year, t.Month, t.Day,
                                         StartHour, StartMinute, 0);
            if (t < baseToday)
                baseToday = baseToday.AddDays(-1);

            double elapsedMins = (t - baseToday).TotalMinutes;
            if (elapsedMins < 0) return -1;
            return (int)Math.Floor(elapsedMins / IntervalMins);
        }

        // 闁冲厜鍋撻柍鍏夊亾 闁哄秴娲﹂弫鐐哄棘閸パ呮憻闁圭柉澹堥ˉ?闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾
        private string LblText(string tag, DateTime t)
        {
            string period = ShowPeriodLabel ? "M" + IntervalMins + " " : "";
            string tstr   = ShowTimeLabel
                            ? string.Format(" {0:00}:{1:00}", t.Hour, t.Minute)
                            : "";
            return period + tag + tstr;
        }

        // ====================================================================
        //  OnStateChange
        // ====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description        = "TimeCycles Liquidity Sweep Detector v10.3 (NT8)";
                Name               = "TimeCyclesLiquiditySweep";
                Calculate          = Calculate.OnBarClose;
                IsOverlay          = true;
                IsAutoScale        = false;
                DrawOnPricePanel   = true;
                BarsRequiredToPlot = 2;
                IsSuspendedWhileInactive = true;

                StartHour       = 18;
                StartMinute     = 0;
                IntervalMins    = 60;

                ShowDividers    = true;
                DividerColor    = Brushes.RoyalBlue;
                DividerStyle    = "Dash";
                DividerWidth    = 1;

                ShowCurrentHL   = true;
                CurrentColor    = Brushes.OrangeRed;
                PchBaseColor    = Brushes.Crimson;
                PclBaseColor    = Brushes.MediumSeaGreen;
                PchSweepColor   = Brushes.Black;
                PclSweepColor   = Brushes.Black;
                LineWidth       = 2;

                SweepChar       = "X";
                ShowBos         = true;
                PurgeBars       = 1;
                SweepTextColor  = Brushes.Crimson;
                ShowPeriodLabel = true;
                ShowTimeLabel   = true;

                ShowCE          = true;
                CeColor         = Brushes.MediumPurple;
                CeStyle         = "Dot";

                ShowDealingRange = true;
                DealingRangeColor = Brushes.SteelBlue;
                DealingRangeOpacity = 8;
                ShowDealingRangeLabel = true;

                MaxHistory      = 3;
            }
            else if (State == State.Configure)
            {
                _cycles.Clear();
                _curInit         = false;
                _seq             = 0;
                _lastPeriodIndex = int.MinValue;
            }
                    else if (State == State.DataLoaded)
            {
                _lastCycleHighSeries  = new Series<double>(this);
                _lastCycleLowSeries   = new Series<double>(this);
                _currentCycleHighSeries = new Series<double>(this);
                _currentCycleLowSeries  = new Series<double>(this);
                _pchSweepSignalSeries = new Series<double>(this);
                _pclSweepSignalSeries = new Series<double>(this);
                _bosSignalSeries      = new Series<double>(this);
                _dealingRangeHighSeries = new Series<double>(this);
                _dealingRangeLowSeries  = new Series<double>(this);
                _dealingRangeMidSeries  = new Series<double>(this);
                _inPremiumSeries        = new Series<double>(this);
                _inDiscountSeries       = new Series<double>(this);
            }
        }

        // ====================================================================
        //  OnBarUpdate
        // ====================================================================
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            ResetSignals();

            int  curIdx     = PeriodIndex(Time[0]);
            bool isNewCycle = curIdx != _lastPeriodIndex && curIdx >= 0;

            if (isNewCycle)
            {
                if (ShowDividers)
                    Draw.VerticalLine(this, T("DIV"), Time[0],
                                      DividerColor, DS(DividerStyle), DividerWidth);

                if (_curInit)
                    FinalizeCycle(Time[0]);

                StartNewCycle();
            }
            else if (!_curInit)
            {
                StartNewCycle();
            }
            else
            {
                UpdateCurrentCycle();
            }

            ProcessLatestCompletedCycle();
            PublishLastCycleLevels();

            _lastPeriodIndex = curIdx;
        }

        private void ResetSignals()
        {
            if (_lastCycleHighSeries == null) return;

            _lastCycleHighSeries[0]  = double.NaN;
            _lastCycleLowSeries[0]   = double.NaN;
            _currentCycleHighSeries[0] = double.NaN;
            _currentCycleLowSeries[0]  = double.NaN;
            _pchSweepSignalSeries[0] = 0;
            _pclSweepSignalSeries[0] = 0;
            _bosSignalSeries[0]      = 0;
            _dealingRangeHighSeries[0] = double.NaN;
            _dealingRangeLowSeries[0]  = double.NaN;
            _dealingRangeMidSeries[0]  = double.NaN;
            _inPremiumSeries[0]        = 0;
            _inDiscountSeries[0]       = 0;
        }

        private void PublishLastCycleLevels()
        {
            if (_lastCycleHighSeries == null || _cycles.Count == 0) return;

            CycleData c = _cycles[_cycles.Count - 1];
            _lastCycleHighSeries[0] = c.PchValue;
            _lastCycleLowSeries[0]  = c.PclValue;
            _dealingRangeHighSeries[0] = c.PchValue;
            _dealingRangeLowSeries[0]  = c.PclValue;
            _dealingRangeMidSeries[0]  = (c.PchValue + c.PclValue) * 0.5;
            _inPremiumSeries[0]        = Close[0] >= _dealingRangeMidSeries[0] ? 1 : 0;
            _inDiscountSeries[0]       = Close[0] <= _dealingRangeMidSeries[0] ? 1 : 0;
        }

        private void UpdateCurrentCycle()
        {
            if (High[0] > _curHigh) { _curHigh = High[0]; _curHighTime = Time[0]; }
            if (Low[0]  < _curLow)  { _curLow  = Low[0];  _curLowTime  = Time[0]; }

            if (_currentCycleHighSeries != null)
            {
                _currentCycleHighSeries[0] = _curHigh;
                _currentCycleLowSeries[0] = _curLow;
            }

            if (ShowCurrentHL)
            {
                DrawDashedLine(_tagCurH, _curCycleStart, _curHigh);
                DrawDashedLine(_tagCurL, _curCycleStart, _curLow);
            }
        }

        private void ProcessLatestCompletedCycle()
        {
            if (_cycles.Count == 0 || _pchSweepSignalSeries == null) return;

            CycleData c = _cycles[_cycles.Count - 1];

            if (c.PchSweepBar == -1 && High[0] > c.PchValue)
            {
                c.PchIsSweep  = Close[0] <= c.PchValue;
                c.PchSweepBar = CurrentBar;
                _pchSweepSignalSeries[0] = c.PchIsSweep ? 1 : 2;
                if (!c.PchIsSweep)
                    _bosSignalSeries[0] = 1;

                DrawFixedLine(c.TagPchLine, c.PchTime, c.PchValue, Time[0], c.PchValue,
                              PchSweepColor, DS("Solid"), LineWidth);

                string mark = c.PchIsSweep ? SweepChar : (ShowBos ? "BOS" : "");
                if (!string.IsNullOrEmpty(mark))
                    DrawMark(c.TagSweepPch, Time[0], c.PchValue, mark);
            }
            else if (c.PchSweepBar > 0 && PurgeBars > 0 && !c.PchIsSweep
                     && (CurrentBar - c.PchSweepBar) <= PurgeBars
                     && Close[0] <= c.PchValue)
            {
                c.PchIsSweep = true;
                _pchSweepSignalSeries[0] = 1;
                _bosSignalSeries[0] = 0;
                DrawMark(c.TagSweepPch, Time[0], c.PchValue, SweepChar);
            }

            if (c.PclSweepBar == -1 && Low[0] < c.PclValue)
            {
                c.PclIsSweep  = Close[0] >= c.PclValue;
                c.PclSweepBar = CurrentBar;
                _pclSweepSignalSeries[0] = c.PclIsSweep ? 1 : 2;
                if (!c.PclIsSweep)
                    _bosSignalSeries[0] = -1;

                DrawFixedLine(c.TagPclLine, c.PclTime, c.PclValue, Time[0], c.PclValue,
                              PclSweepColor, DS("Solid"), LineWidth);

                string mark = c.PclIsSweep ? SweepChar : (ShowBos ? "BOS" : "");
                if (!string.IsNullOrEmpty(mark))
                    DrawMark(c.TagSweepPcl, Time[0], c.PclValue, mark);
            }
            else if (c.PclSweepBar > 0 && PurgeBars > 0 && !c.PclIsSweep
                     && (CurrentBar - c.PclSweepBar) <= PurgeBars
                     && Close[0] >= c.PclValue)
            {
                c.PclIsSweep = true;
                _pclSweepSignalSeries[0] = 1;
                _bosSignalSeries[0] = 0;
                DrawMark(c.TagSweepPcl, Time[0], c.PclValue, SweepChar);
            }
        }

        // ====================================================================
        //  閻忓繋绀侀悺銊╁川閵婏附鍩傞柨娑欐皑閺?PCH / PCL / CE 缂佹儳鐏濆鐑藉冀閸ャ劍鏆?
        // ====================================================================
        private void FinalizeCycle(DateTime endTime)
        {
            var c = new CycleData
            {
                PchValue    = _curHigh,
                PclValue    = _curLow,
                PchTime     = _curHighTime,
                PclTime     = _curLowTime,
                CycleStart  = _curCycleStart,
                CycleEnd    = endTime,
                PchSweepBar = -1,
                PclSweepBar = -1,
                TagPchLine  = T("PCH_LN"),
                TagPclLine  = T("PCL_LN"),
                TagCeLine   = T("CE_LN"),
                TagPchLabel = T("PCH_LB"),
                TagPclLabel = T("PCL_LB"),
                TagSweepPch = T("SW_PCH"),
                TagSweepPcl = T("SW_PCL"),
                TagDealingRange = T("DR_BOX"),
                TagDealingLabel = T("DR_LB"),
            };

            // PCH / PCL 缂佹儳灏呯槐娆戞導妞嬪骸浠?= 闁?濞达絽瀛╂晶宥夊捶閳鐥崠锛勭缂備礁鐗忛崑?= 闁告稏鍔嶅﹢锛勭磼閹惧瓨灏嗛柨娑樿嫰閸ㄥ灚鎱ㄧ€ｅ墎绀?
            if (ShowDealingRange)
            {
                Draw.Rectangle(this, c.TagDealingRange, false,
                               c.CycleStart, c.PchValue,
                               endTime, c.PclValue,
                               DealingRangeColor, DealingRangeColor,
                               DealingRangeOpacity, true);
                if (ShowDealingRangeLabel)
                    DrawLbl(c.TagDealingLabel, endTime, (c.PchValue + c.PclValue) / 2.0,
                            LblText("DR", c.CycleStart), DealingRangeColor);
            }

            DrawFixedLine(c.TagPchLine,
                          c.PchTime, c.PchValue,
                          endTime,   c.PchValue,
                          PchBaseColor, DS("Solid"), LineWidth);

            DrawFixedLine(c.TagPclLine,
                          c.PclTime, c.PclValue,
                          endTime,   c.PclValue,
                          PclBaseColor, DS("Solid"), LineWidth);

            // CE 濞戞搩鍘鹃崵搴ㄦ晬閸綆娲柣鈺傜墬閺嗭絾绋夐鍕櫙闁哄牏鍣︾槐?
            if (ShowCE)
            {
                double ce = (c.PchValue + c.PclValue) / 2.0;
                DrawFixedLine(c.TagCeLine,
                              c.CycleStart, ce,
                              endTime,      ce,
                              CeColor, DS(CeStyle), 1);
            }

            // 闁哄秴娲﹂弫鐐哄棘閸パ呮憻闁挎稑鐗嗛崹鍨叏鐎ｎ厼鍨遍柛锔哄妼閹冲棝寮甸悢铏规尝闁哄鍠庨ˇ鈺呮晬鐏炶姤鍊电紓渚囧幗閻︼繝寮界粵鈧紒鎹愬劵缁愶繝姊捐箛鎾搭偨濞村ジ娼荤槐?
            DrawLbl(c.TagPchLabel, endTime, c.PchValue,
                    LblText("PCH", c.PchTime), PchBaseColor);
            DrawLbl(c.TagPclLabel, endTime, c.PclValue,
                    LblText("PCL", c.PclTime), PclBaseColor);

            _cycles.Add(c);

            // 閻℃帒鎳庨崵顓㈠储閸℃钑夊☉鎾筹躬濡炬椽鏁嶇仦钘夌仼闂傚嫨鍊栧〒鍫曞籍瑜嶉幊鍡涙晸?
            while (_cycles.Count > Math.Max(1, MaxHistory))
            {
                var old = _cycles[0];
                SafeRemove(old.TagPchLine);
                SafeRemove(old.TagPclLine);
                SafeRemove(old.TagCeLine);
                SafeRemove(old.TagPchLabel);
                SafeRemove(old.TagPclLabel);
                SafeRemove(old.TagSweepPch);
                SafeRemove(old.TagSweepPcl);
                SafeRemove(old.TagDealingRange);
                SafeRemove(old.TagDealingLabel);
                _cycles.RemoveAt(0);
            }
        }

        // ====================================================================
        //  鐎殿喒鍋撳┑顔碱儐閺屽﹪宕ㄩ妸锔藉焸
        // ====================================================================
        private void StartNewCycle()
        {
            _curHigh       = High[0];
            _curLow        = Low[0];
            _curHighTime   = Time[0];
            _curLowTime    = Time[0];
            _curCycleStart = Time[0];
            _curInit       = true;

            SafeRemove(_tagCurH);
            SafeRemove(_tagCurL);
            _tagCurH = T("CUR_H");
            _tagCurL = T("CUR_L");

            if (ShowCurrentHL)
            {
                DrawDashedLine(_tagCurH, _curCycleStart, _curHigh);
                DrawDashedLine(_tagCurL, _curCycleStart, _curLow);
            }
        }

        // ====================================================================
        //  缂備焦锚濞存ɑ娼忛崨顓炐?
        // ====================================================================

        // 婵ɑ娼欓柦鈺冪棯閸栵紕绀勯柣顫妽濡炲倿姊荤€涙ê鐓堥柛褎鍔栭悥锝夋晬鐏炶姤鍊?tag 閻熸洖妫涘ú濠囧础閾忣偅绾柡鍌氬簻缁?
        private void DrawFixedLine(string tag,
                                    DateTime t1, double p1,
                                    DateTime t2, double p2,
                                    Brush color, DashStyleHelper dash, int width)
        {
            Draw.Line(this, tag, false, t1, p1, t2, p2, color, dash, width);
        }

        // 鐟滅増鎸告晶鐘诲川閵婏附鍩傞柧蹇旀皑閸ゅ酣鏁嶉崼婊呯煠闁告稏鍔嶅﹢锛勬導瀹勯偊娼?闁?鐟滅増鎸告晶?K 缂佹儳灏呯槐?
        private void DrawDashedLine(string tag, DateTime startTime, double price)
        {
            Draw.Line(this, tag, false,
                      startTime, price,
                      Time[0],   price,
                      CurrentColor, DashStyleHelper.Dash, LineWidth);
        }

        // 闁哄秴娲﹂弫鐐哄棘閸パ呮憻闁挎稑鐗愰崚娑㈠捶閵娧冩疇闁告瑥纾顒佺▔婵犲啯鐓欓柨?
        private void DrawLbl(string tag, DateTime t, double price,
                              string text, Brush color)
        {
            Draw.Text(this, tag, true, text,
                      t, price, 5,
                      color,
                      new NinjaTrader.Gui.Tools.SimpleFont("Arial", 9),
                      TextAlignment.Left,
                      Brushes.Transparent, Brushes.Transparent, 0);
        }

        // Sweep / BOS 缂佹绠戣ぐ鍧楁晬閸喐鈻旂紒鈧崫鍕含閻熸瑱濡囬～顐ｆ媴瀹ュ洨鏋傞柨?
        private void DrawMark(string tag, DateTime t, double price, string mark)
        {
            Draw.Text(this, tag, true, mark,
                      t, price, 0,
                      SweepTextColor,
                      new NinjaTrader.Gui.Tools.SimpleFont("Arial", 11),
                      TextAlignment.Center,
                      Brushes.Transparent, Brushes.Transparent, 0);
        }

        // 閻庣懓顦崣蹇涘礆閻樼粯鐝熼柨娑樼墢閳?tag 闁烩晛鐡ㄧ敮瀛樻交閺傛寧绀€闁挎稑鐭傛导鈺呭礂瀹ュ懐纾介悽顖炴交缁?
        private void SafeRemove(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            base.RemoveDrawObject(tag);
        }

        // ====================================================================
        //  闁告瑥鍊归弳鐔轰沪閻戝洦瀚?
        // ====================================================================

        #region 闁?闁哄啫鐖煎Λ鎸庣▔鎼粹剝鍣柡鍫㈠枙椤旀洟鏁?

        [NinjaScriptProperty]
        [Range(0, 23)]
        public int StartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        public int StartMinute { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1440)]
        public int IntervalMins { get; set; }

        #endregion

        #region 妫ｅ啯鎯?闁告稏鍔嶅﹢锟犲礆閸℃顥忕紒鏃€鐗滈崵?

        [NinjaScriptProperty]
        public bool ShowDividers { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        public Brush DividerColor { get; set; }

        [Browsable(false)]
        public string DividerColorSerializable
        {
            get { return Serialize.BrushToString(DividerColor); }
            set { DividerColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        public string DividerStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5)]
        public int DividerWidth { get; set; }

        #endregion

        #region 妫ｅ啫绠?PCH / PCL 婵☆垼浜為崵?

        [NinjaScriptProperty]
        public bool ShowCurrentHL { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        public Brush CurrentColor { get; set; }

        [Browsable(false)]
        public string CurrentColorSerializable
        {
            get { return Serialize.BrushToString(CurrentColor); }
            set { CurrentColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        public Brush PchBaseColor { get; set; }

        [Browsable(false)]
        public string PchBaseColorSerializable
        {
            get { return Serialize.BrushToString(PchBaseColor); }
            set { PchBaseColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        public Brush PclBaseColor { get; set; }

        [Browsable(false)]
        public string PclBaseColorSerializable
        {
            get { return Serialize.BrushToString(PclBaseColor); }
            set { PclBaseColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        public Brush PchSweepColor { get; set; }

        [Browsable(false)]
        public string PchSweepColorSerializable
        {
            get { return Serialize.BrushToString(PchSweepColor); }
            set { PchSweepColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        public Brush PclSweepColor { get; set; }

        [Browsable(false)]
        public string PclSweepColorSerializable
        {
            get { return Serialize.BrushToString(PclSweepColor); }
            set { PclSweepColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 5)]
        public int LineWidth { get; set; }

        #endregion

        #region 妫ｅ啯瀵?Sweep / BOS 闁哄秴娲╅惁?

        [NinjaScriptProperty]
        public string SweepChar { get; set; }

        [NinjaScriptProperty]
        public bool ShowBos { get; set; }

        [NinjaScriptProperty]
        [Range(0, 5)]
        public int PurgeBars { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        public Brush SweepTextColor { get; set; }

        [Browsable(false)]
        public string SweepTextColorSerializable
        {
            get { return Serialize.BrushToString(SweepTextColor); }
            set { SweepTextColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        public bool ShowPeriodLabel { get; set; }

        [NinjaScriptProperty]
        public bool ShowTimeLabel { get; set; }

        #endregion

        #region 妫ｅ啫鑵?ICT CE 濞戞搩鍘鹃崵?

        [NinjaScriptProperty]
        public bool ShowCE { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        public Brush CeColor { get; set; }

        [Browsable(false)]
        public string CeColorSerializable
        {
            get { return Serialize.BrushToString(CeColor); }
            set { CeColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        public string CeStyle { get; set; }

        [NinjaScriptProperty]
        public bool ShowDealingRange { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        public Brush DealingRangeColor { get; set; }

        [Browsable(false)]
        public string DealingRangeColorSerializable
        {
            get { return Serialize.BrushToString(DealingRangeColor); }
            set { DealingRangeColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(0, 100)]
        public int DealingRangeOpacity { get; set; }

        [NinjaScriptProperty]
        public bool ShowDealingRangeLabel { get; set; }

        #endregion

        #region 闁虫寧鐟辩粭?闁诡儸鍡楀幋濞戞挸楠稿濠氭晸?

        [NinjaScriptProperty]
        [Range(3, 50)]
        public int MaxHistory { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> LastCycleHigh { get { return _lastCycleHighSeries; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> LastCycleLow { get { return _lastCycleLowSeries; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> CurrentCycleHigh { get { return _currentCycleHighSeries; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> CurrentCycleLow { get { return _currentCycleLowSeries; } }

        // 0 = no touch, 1 = sweep/revert, 2 = BOS above PCH.
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PchSweepSignal { get { return _pchSweepSignalSeries; } }

        // 0 = no touch, 1 = sweep/revert, 2 = BOS below PCL.
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PclSweepSignal { get { return _pclSweepSignalSeries; } }

        // 0 = none, 1 = bullish BOS, -1 = bearish BOS.
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BosSignal { get { return _bosSignalSeries; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> DealingRangeHigh { get { return _dealingRangeHighSeries; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> DealingRangeLow { get { return _dealingRangeLowSeries; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> DealingRangeMid { get { return _dealingRangeMidSeries; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> InPremium { get { return _inPremiumSeries; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> InDiscount { get { return _inDiscountSeries; } }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TimeCyclesLiquiditySweep[] cacheTimeCyclesLiquiditySweep;
		public TimeCyclesLiquiditySweep TimeCyclesLiquiditySweep(int startHour, int startMinute, int intervalMins, bool showDividers, Brush dividerColor, string dividerStyle, int dividerWidth, bool showCurrentHL, Brush currentColor, Brush pchBaseColor, Brush pclBaseColor, Brush pchSweepColor, Brush pclSweepColor, int lineWidth, string sweepChar, bool showBos, int purgeBars, Brush sweepTextColor, bool showPeriodLabel, bool showTimeLabel, bool showCE, Brush ceColor, string ceStyle, bool showDealingRange, Brush dealingRangeColor, int dealingRangeOpacity, bool showDealingRangeLabel, int maxHistory)
		{
			return TimeCyclesLiquiditySweep(Input, startHour, startMinute, intervalMins, showDividers, dividerColor, dividerStyle, dividerWidth, showCurrentHL, currentColor, pchBaseColor, pclBaseColor, pchSweepColor, pclSweepColor, lineWidth, sweepChar, showBos, purgeBars, sweepTextColor, showPeriodLabel, showTimeLabel, showCE, ceColor, ceStyle, showDealingRange, dealingRangeColor, dealingRangeOpacity, showDealingRangeLabel, maxHistory);
		}

		public TimeCyclesLiquiditySweep TimeCyclesLiquiditySweep(ISeries<double> input, int startHour, int startMinute, int intervalMins, bool showDividers, Brush dividerColor, string dividerStyle, int dividerWidth, bool showCurrentHL, Brush currentColor, Brush pchBaseColor, Brush pclBaseColor, Brush pchSweepColor, Brush pclSweepColor, int lineWidth, string sweepChar, bool showBos, int purgeBars, Brush sweepTextColor, bool showPeriodLabel, bool showTimeLabel, bool showCE, Brush ceColor, string ceStyle, bool showDealingRange, Brush dealingRangeColor, int dealingRangeOpacity, bool showDealingRangeLabel, int maxHistory)
		{
			if (cacheTimeCyclesLiquiditySweep != null)
				for (int idx = 0; idx < cacheTimeCyclesLiquiditySweep.Length; idx++)
					if (cacheTimeCyclesLiquiditySweep[idx] != null && cacheTimeCyclesLiquiditySweep[idx].StartHour == startHour && cacheTimeCyclesLiquiditySweep[idx].StartMinute == startMinute && cacheTimeCyclesLiquiditySweep[idx].IntervalMins == intervalMins && cacheTimeCyclesLiquiditySweep[idx].ShowDividers == showDividers && cacheTimeCyclesLiquiditySweep[idx].DividerColor == dividerColor && cacheTimeCyclesLiquiditySweep[idx].DividerStyle == dividerStyle && cacheTimeCyclesLiquiditySweep[idx].DividerWidth == dividerWidth && cacheTimeCyclesLiquiditySweep[idx].ShowCurrentHL == showCurrentHL && cacheTimeCyclesLiquiditySweep[idx].CurrentColor == currentColor && cacheTimeCyclesLiquiditySweep[idx].PchBaseColor == pchBaseColor && cacheTimeCyclesLiquiditySweep[idx].PclBaseColor == pclBaseColor && cacheTimeCyclesLiquiditySweep[idx].PchSweepColor == pchSweepColor && cacheTimeCyclesLiquiditySweep[idx].PclSweepColor == pclSweepColor && cacheTimeCyclesLiquiditySweep[idx].LineWidth == lineWidth && cacheTimeCyclesLiquiditySweep[idx].SweepChar == sweepChar && cacheTimeCyclesLiquiditySweep[idx].ShowBos == showBos && cacheTimeCyclesLiquiditySweep[idx].PurgeBars == purgeBars && cacheTimeCyclesLiquiditySweep[idx].SweepTextColor == sweepTextColor && cacheTimeCyclesLiquiditySweep[idx].ShowPeriodLabel == showPeriodLabel && cacheTimeCyclesLiquiditySweep[idx].ShowTimeLabel == showTimeLabel && cacheTimeCyclesLiquiditySweep[idx].ShowCE == showCE && cacheTimeCyclesLiquiditySweep[idx].CeColor == ceColor && cacheTimeCyclesLiquiditySweep[idx].CeStyle == ceStyle && cacheTimeCyclesLiquiditySweep[idx].ShowDealingRange == showDealingRange && cacheTimeCyclesLiquiditySweep[idx].DealingRangeColor == dealingRangeColor && cacheTimeCyclesLiquiditySweep[idx].DealingRangeOpacity == dealingRangeOpacity && cacheTimeCyclesLiquiditySweep[idx].ShowDealingRangeLabel == showDealingRangeLabel && cacheTimeCyclesLiquiditySweep[idx].MaxHistory == maxHistory && cacheTimeCyclesLiquiditySweep[idx].EqualsInput(input))
						return cacheTimeCyclesLiquiditySweep[idx];
			return CacheIndicator<TimeCyclesLiquiditySweep>(new TimeCyclesLiquiditySweep(){ StartHour = startHour, StartMinute = startMinute, IntervalMins = intervalMins, ShowDividers = showDividers, DividerColor = dividerColor, DividerStyle = dividerStyle, DividerWidth = dividerWidth, ShowCurrentHL = showCurrentHL, CurrentColor = currentColor, PchBaseColor = pchBaseColor, PclBaseColor = pclBaseColor, PchSweepColor = pchSweepColor, PclSweepColor = pclSweepColor, LineWidth = lineWidth, SweepChar = sweepChar, ShowBos = showBos, PurgeBars = purgeBars, SweepTextColor = sweepTextColor, ShowPeriodLabel = showPeriodLabel, ShowTimeLabel = showTimeLabel, ShowCE = showCE, CeColor = ceColor, CeStyle = ceStyle, ShowDealingRange = showDealingRange, DealingRangeColor = dealingRangeColor, DealingRangeOpacity = dealingRangeOpacity, ShowDealingRangeLabel = showDealingRangeLabel, MaxHistory = maxHistory }, input, ref cacheTimeCyclesLiquiditySweep);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TimeCyclesLiquiditySweep TimeCyclesLiquiditySweep(int startHour, int startMinute, int intervalMins, bool showDividers, Brush dividerColor, string dividerStyle, int dividerWidth, bool showCurrentHL, Brush currentColor, Brush pchBaseColor, Brush pclBaseColor, Brush pchSweepColor, Brush pclSweepColor, int lineWidth, string sweepChar, bool showBos, int purgeBars, Brush sweepTextColor, bool showPeriodLabel, bool showTimeLabel, bool showCE, Brush ceColor, string ceStyle, bool showDealingRange, Brush dealingRangeColor, int dealingRangeOpacity, bool showDealingRangeLabel, int maxHistory)
		{
			return indicator.TimeCyclesLiquiditySweep(Input, startHour, startMinute, intervalMins, showDividers, dividerColor, dividerStyle, dividerWidth, showCurrentHL, currentColor, pchBaseColor, pclBaseColor, pchSweepColor, pclSweepColor, lineWidth, sweepChar, showBos, purgeBars, sweepTextColor, showPeriodLabel, showTimeLabel, showCE, ceColor, ceStyle, showDealingRange, dealingRangeColor, dealingRangeOpacity, showDealingRangeLabel, maxHistory);
		}

		public Indicators.TimeCyclesLiquiditySweep TimeCyclesLiquiditySweep(ISeries<double> input , int startHour, int startMinute, int intervalMins, bool showDividers, Brush dividerColor, string dividerStyle, int dividerWidth, bool showCurrentHL, Brush currentColor, Brush pchBaseColor, Brush pclBaseColor, Brush pchSweepColor, Brush pclSweepColor, int lineWidth, string sweepChar, bool showBos, int purgeBars, Brush sweepTextColor, bool showPeriodLabel, bool showTimeLabel, bool showCE, Brush ceColor, string ceStyle, bool showDealingRange, Brush dealingRangeColor, int dealingRangeOpacity, bool showDealingRangeLabel, int maxHistory)
		{
			return indicator.TimeCyclesLiquiditySweep(input, startHour, startMinute, intervalMins, showDividers, dividerColor, dividerStyle, dividerWidth, showCurrentHL, currentColor, pchBaseColor, pclBaseColor, pchSweepColor, pclSweepColor, lineWidth, sweepChar, showBos, purgeBars, sweepTextColor, showPeriodLabel, showTimeLabel, showCE, ceColor, ceStyle, showDealingRange, dealingRangeColor, dealingRangeOpacity, showDealingRangeLabel, maxHistory);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TimeCyclesLiquiditySweep TimeCyclesLiquiditySweep(int startHour, int startMinute, int intervalMins, bool showDividers, Brush dividerColor, string dividerStyle, int dividerWidth, bool showCurrentHL, Brush currentColor, Brush pchBaseColor, Brush pclBaseColor, Brush pchSweepColor, Brush pclSweepColor, int lineWidth, string sweepChar, bool showBos, int purgeBars, Brush sweepTextColor, bool showPeriodLabel, bool showTimeLabel, bool showCE, Brush ceColor, string ceStyle, bool showDealingRange, Brush dealingRangeColor, int dealingRangeOpacity, bool showDealingRangeLabel, int maxHistory)
		{
			return indicator.TimeCyclesLiquiditySweep(Input, startHour, startMinute, intervalMins, showDividers, dividerColor, dividerStyle, dividerWidth, showCurrentHL, currentColor, pchBaseColor, pclBaseColor, pchSweepColor, pclSweepColor, lineWidth, sweepChar, showBos, purgeBars, sweepTextColor, showPeriodLabel, showTimeLabel, showCE, ceColor, ceStyle, showDealingRange, dealingRangeColor, dealingRangeOpacity, showDealingRangeLabel, maxHistory);
		}

		public Indicators.TimeCyclesLiquiditySweep TimeCyclesLiquiditySweep(ISeries<double> input , int startHour, int startMinute, int intervalMins, bool showDividers, Brush dividerColor, string dividerStyle, int dividerWidth, bool showCurrentHL, Brush currentColor, Brush pchBaseColor, Brush pclBaseColor, Brush pchSweepColor, Brush pclSweepColor, int lineWidth, string sweepChar, bool showBos, int purgeBars, Brush sweepTextColor, bool showPeriodLabel, bool showTimeLabel, bool showCE, Brush ceColor, string ceStyle, bool showDealingRange, Brush dealingRangeColor, int dealingRangeOpacity, bool showDealingRangeLabel, int maxHistory)
		{
			return indicator.TimeCyclesLiquiditySweep(input, startHour, startMinute, intervalMins, showDividers, dividerColor, dividerStyle, dividerWidth, showCurrentHL, currentColor, pchBaseColor, pclBaseColor, pchSweepColor, pclSweepColor, lineWidth, sweepChar, showBos, purgeBars, sweepTextColor, showPeriodLabel, showTimeLabel, showCE, ceColor, ceStyle, showDealingRange, dealingRangeColor, dealingRangeOpacity, showDealingRangeLabel, maxHistory);
		}
	}
}

#endregion
