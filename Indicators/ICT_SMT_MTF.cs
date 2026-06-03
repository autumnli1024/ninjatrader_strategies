#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using Brush = System.Windows.Media.Brush;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	[Gui.CategoryOrder("Comparison Assets", 1)]
	[Gui.CategoryOrder("Pivot", 2)]
	[Gui.CategoryOrder("Timeframes", 3)]
	[Gui.CategoryOrder("Filters", 4)]
	[Gui.CategoryOrder("Invalidation", 5)]
	[Gui.CategoryOrder("Style", 6)]
	public class ICT_SMT_MTF : Indicator
	{
		private class PivotState
		{
			public double PrevHigh = double.NaN;
			public double LastHigh = double.NaN;
			public DateTime PrevHighTime = DateTime.MinValue;
			public DateTime LastHighTime = DateTime.MinValue;
			public double PrevLow = double.NaN;
			public double LastLow = double.NaN;
			public DateTime PrevLowTime = DateTime.MinValue;
			public DateTime LastLowTime = DateTime.MinValue;
			public int LastHighBar = -1;
			public int LastLowBar = -1;
		}

		private class TfSlot
		{
			public int Minutes;
			public string Label;
			public int PrimaryBip;
			public int AssetABip;
			public int AssetBBip;
			public PivotState Primary = new PivotState();
			public PivotState AssetA = new PivotState();
			public PivotState AssetB = new PivotState();
		}

		private class ActiveSignal
		{
			public string LineTag;
			public string LabelTag;
			public bool Bullish;
			public double InvalidLevel;
			public Brush OriginalBrush;
		}

		private readonly List<TfSlot> slots = new List<TfSlot>();
		private readonly List<ActiveSignal> activeSignals = new List<ActiveSignal>();
		private readonly HashSet<string> emittedSignals = new HashSet<string>();
		private Series<double> bullSmtSignal;
		private Series<double> bearSmtSignal;
		private Series<double> bullSmtTimeframe;
		private Series<double> bearSmtTimeframe;
		private int seq;
		private int nextBip;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "ICT_SMT_MTF";
				Description = "Multi-timeframe SMT divergence for NT8, based on SMT-MTF TradingView logic.";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DrawOnPricePanel = true;
				DisplayInDataBox = false;
				PaintPriceMarkers = false;
				IsSuspendedWhileInactive = true;

				UseAssetA = true;
				AssetA = "MES 06-26";
				UseAssetB = true;
				AssetB = "MYM 06-26";
				PivotLookback = 3;
				CandleDirectionValidation = false;
				UseMinimumDistance = false;
				MinDistanceUsesATR = true;
				MinDistanceATR = 0.5;
				MinDistancePoints = 10;
				MaxSyncMinutes = 240;
				MaxSignals = 80;

				TF240 = true;
				TF90 = true;
				TF60 = true;
				TF30 = true;
				TF15 = true;
				TF5 = true;

				ShowLines = true;
				ShowLabels = true;
				InvalidationMode = "Dashed";
				BullColor = Brushes.LimeGreen;
				BearColor = Brushes.Crimson;
				InvalidColor = Brushes.Gray;
				LineWidth240 = 4;
				LineWidth90 = 3;
				LineWidth60 = 3;
				LineWidth30 = 2;
				LineWidth15 = 2;
				LineWidth5 = 1;
				LabelFont = new SimpleFont("Arial", 10);
			}
			else if (State == State.Configure)
			{
				slots.Clear();
				emittedSignals.Clear();
				nextBip = 1;
				AddTfSlot(240, "240");
				AddTfSlot(90, "90");
				AddTfSlot(60, "60");
				AddTfSlot(30, "30");
				AddTfSlot(15, "15");
				AddTfSlot(5, "5");
			}
			else if (State == State.DataLoaded)
			{
				bullSmtSignal = new Series<double>(this);
				bearSmtSignal = new Series<double>(this);
				bullSmtTimeframe = new Series<double>(this);
				bearSmtTimeframe = new Series<double>(this);
			}
		}

		private void AddTfSlot(int minutes, string label)
		{
			TfSlot slot = new TfSlot { Minutes = minutes, Label = label };
			AddDataSeries(BarsPeriodType.Minute, minutes);
			slot.PrimaryBip = nextBip++;
			if (UseAssetA && !string.IsNullOrWhiteSpace(AssetA))
			{
				AddDataSeries(AssetA, BarsPeriodType.Minute, minutes);
				slot.AssetABip = nextBip++;
			}
			else slot.AssetABip = -1;

			if (UseAssetB && !string.IsNullOrWhiteSpace(AssetB))
			{
				AddDataSeries(AssetB, BarsPeriodType.Minute, minutes);
				slot.AssetBBip = nextBip++;
			}
			else slot.AssetBBip = -1;

			slots.Add(slot);
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 0)
			{
				ResetSignals();
				ProcessInvalidations();
				return;
			}

			TfSlot slot = FindSlot(BarsInProgress);
			if (slot == null || !IsTfEnabled(slot.Minutes) || CurrentBars[BarsInProgress] < PivotLookback * 2 + 2)
				return;

			PivotState state = GetState(slot, BarsInProgress);
			if (state == null)
				return;

			bool newHigh;
			bool newLow;
			double highPivot;
			double lowPivot;
			DateTime pivotTime;
			DetectPivot(BarsInProgress, out newHigh, out highPivot, out newLow, out lowPivot, out pivotTime);

			if (newHigh)
			{
				state.PrevHigh = state.LastHigh;
				state.PrevHighTime = state.LastHighTime;
				state.LastHigh = highPivot;
				state.LastHighTime = pivotTime;
				state.LastHighBar = CurrentBars[BarsInProgress] - PivotLookback;
			}

			if (newLow)
			{
				state.PrevLow = state.LastLow;
				state.PrevLowTime = state.LastLowTime;
				state.LastLow = lowPivot;
				state.LastLowTime = pivotTime;
				state.LastLowBar = CurrentBars[BarsInProgress] - PivotLookback;
			}

			if (BarsInProgress == slot.PrimaryBip)
			{
				if (UseAssetA && slot.AssetABip > 0)
					CheckPair(slot, slot.Primary, slot.AssetA, slot.AssetABip, AssetA);
				if (UseAssetB && slot.AssetBBip > 0)
					CheckPair(slot, slot.Primary, slot.AssetB, slot.AssetBBip, AssetB);
			}
			else if (BarsInProgress == slot.AssetABip && UseAssetA)
				CheckPair(slot, slot.Primary, slot.AssetA, slot.AssetABip, AssetA);
			else if (BarsInProgress == slot.AssetBBip && UseAssetB)
				CheckPair(slot, slot.Primary, slot.AssetB, slot.AssetBBip, AssetB);
		}

		private void ResetSignals()
		{
			if (bullSmtSignal == null) return;
			bullSmtSignal[0] = 0;
			bearSmtSignal[0] = 0;
			bullSmtTimeframe[0] = 0;
			bearSmtTimeframe[0] = 0;
		}

		private TfSlot FindSlot(int bip)
		{
			for (int i = 0; i < slots.Count; i++)
				if (slots[i].PrimaryBip == bip || slots[i].AssetABip == bip || slots[i].AssetBBip == bip)
					return slots[i];
			return null;
		}

		private PivotState GetState(TfSlot slot, int bip)
		{
			if (bip == slot.PrimaryBip) return slot.Primary;
			if (bip == slot.AssetABip) return slot.AssetA;
			if (bip == slot.AssetBBip) return slot.AssetB;
			return null;
		}

		private void DetectPivot(int bip, out bool newHigh, out double highPivot, out bool newLow, out double lowPivot, out DateTime pivotTime)
		{
			newHigh = true;
			newLow = true;
			highPivot = Highs[bip][PivotLookback];
			lowPivot = Lows[bip][PivotLookback];
			pivotTime = Times[bip][PivotLookback];

			for (int i = 0; i <= PivotLookback * 2; i++)
			{
				if (i == PivotLookback) continue;
				if (Highs[bip][i] >= highPivot) newHigh = false;
				if (Lows[bip][i] <= lowPivot) newLow = false;
			}

			if (CandleDirectionValidation)
			{
				if (Closes[bip][PivotLookback] <= Opens[bip][PivotLookback]) newHigh = false;
				if (Closes[bip][PivotLookback] >= Opens[bip][PivotLookback]) newLow = false;
			}
		}

		private void CheckPair(TfSlot slot, PivotState main, PivotState comp, int compBip, string compName)
		{
			if (main == null || comp == null)
				return;

			if (HasHighPair(main, comp))
			{
				bool diverges = (main.LastHigh - main.PrevHigh) * (comp.LastHigh - comp.PrevHigh) < 0;
				if (diverges && DistOk(main.LastHigh, main.PrevHigh, slot.PrimaryBip) && DistOk(comp.LastHigh, comp.PrevHigh, compBip))
					RegisterSignal(false, slot, main.PrevHighTime, main.PrevHigh, main.LastHighTime, main.LastHigh, compName);
			}

			if (HasLowPair(main, comp))
			{
				bool diverges = (main.LastLow - main.PrevLow) * (comp.LastLow - comp.PrevLow) < 0;
				if (diverges && DistOk(main.LastLow, main.PrevLow, slot.PrimaryBip) && DistOk(comp.LastLow, comp.PrevLow, compBip))
					RegisterSignal(true, slot, main.PrevLowTime, main.PrevLow, main.LastLowTime, main.LastLow, compName);
			}
		}

		private bool HasHighPair(PivotState main, PivotState comp)
		{
			return !double.IsNaN(main.PrevHigh) && !double.IsNaN(main.LastHigh)
				&& !double.IsNaN(comp.PrevHigh) && !double.IsNaN(comp.LastHigh)
				&& Math.Abs((main.LastHighTime - comp.LastHighTime).TotalMinutes) <= MaxSyncMinutes;
		}

		private bool HasLowPair(PivotState main, PivotState comp)
		{
			return !double.IsNaN(main.PrevLow) && !double.IsNaN(main.LastLow)
				&& !double.IsNaN(comp.PrevLow) && !double.IsNaN(comp.LastLow)
				&& Math.Abs((main.LastLowTime - comp.LastLowTime).TotalMinutes) <= MaxSyncMinutes;
		}

		private bool DistOk(double a, double b, int bip)
		{
			if (!UseMinimumDistance) return true;
			double diff = Math.Abs(a - b);
			if (!MinDistanceUsesATR) return diff >= MinDistancePoints;
			double avg = AverageRange(bip, 14);
			return diff >= avg * MinDistanceATR;
		}

		private double AverageRange(int bip, int period)
		{
			int count = Math.Min(CurrentBars[bip] + 1, Math.Max(1, period));
			double sum = 0;
			for (int i = 0; i < count; i++)
				sum += Highs[bip][i] - Lows[bip][i];
			return count > 0 ? sum / count : 0;
		}

		private void RegisterSignal(bool bullish, TfSlot slot, DateTime prevTime, double prevPrice, DateTime nowTime, double nowPrice, string compName)
		{
			string signalKey = slot.Label + "|" + compName + "|" + bullish + "|" + nowTime.Ticks.ToString() + "|" + nowPrice.ToString("R");
			if (emittedSignals.Contains(signalKey))
				return;
			emittedSignals.Add(signalKey);

			if (bullSmtSignal != null)
			{
				if (bullish)
				{
					bullSmtSignal[0] = 1;
					bullSmtTimeframe[0] = slot.Minutes;
				}
				else
				{
					bearSmtSignal[0] = -1;
					bearSmtTimeframe[0] = slot.Minutes;
				}
			}

			Brush color = bullish ? BullColor : BearColor;
			int width = WidthFor(slot.Minutes);
			string tag = NextTag("SMT");
			string labelTag = tag + "_LBL";
			string label = slot.Label + " SMT";

			if (ShowLines)
				Draw.Line(this, tag, false, prevTime, prevPrice, nowTime, nowPrice, color, DashStyleHelper.Solid, width);
			if (ShowLabels)
				Draw.Text(this, labelTag, false, label + " vs " + CleanName(compName), nowTime, nowPrice, bullish ? -10 : 10, color, LabelFont, System.Windows.TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);

			activeSignals.Add(new ActiveSignal { LineTag = tag, LabelTag = labelTag, Bullish = bullish, InvalidLevel = nowPrice, OriginalBrush = color });
			while (activeSignals.Count > Math.Max(1, MaxSignals))
			{
				RemoveDrawObject(activeSignals[0].LineTag);
				RemoveDrawObject(activeSignals[0].LabelTag);
				activeSignals.RemoveAt(0);
			}
		}

		private void ProcessInvalidations()
		{
			for (int i = activeSignals.Count - 1; i >= 0; i--)
			{
				ActiveSignal s = activeSignals[i];
				bool invalid = s.Bullish ? Close[0] < s.InvalidLevel : Close[0] > s.InvalidLevel;
				if (!invalid) continue;

				if (InvalidationMode == "Hide")
				{
					RemoveDrawObject(s.LineTag);
					RemoveDrawObject(s.LabelTag);
				}
				else if (InvalidationMode == "Recolor")
					Draw.Text(this, s.LabelTag + "_INV", false, "invalid", Time[0], s.InvalidLevel, 0, InvalidColor, LabelFont, System.Windows.TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
				else
					Draw.Line(this, s.LineTag + "_INV", false, Time[0], s.InvalidLevel, Time[0].AddMinutes(1), s.InvalidLevel, InvalidColor, DashStyleHelper.Dash, 1);

				activeSignals.RemoveAt(i);
			}
		}

		public void ClearVisuals()
		{
			for (int i = activeSignals.Count - 1; i >= 0; i--)
			{
				RemoveDrawObject(activeSignals[i].LineTag);
				RemoveDrawObject(activeSignals[i].LabelTag);
				RemoveDrawObject(activeSignals[i].LabelTag + "_INV");
				RemoveDrawObject(activeSignals[i].LineTag + "_INV");
			}
			activeSignals.Clear();
		}

		private string NextTag(string prefix)
		{
			seq++;
			return "ICT_SMT_MTF_" + prefix + "_" + seq;
		}

		private string CleanName(string name)
		{
			if (string.IsNullOrEmpty(name)) return "";
			int idx = name.IndexOf(' ');
			return idx > 0 ? name.Substring(0, idx) : name;
		}

		private bool IsTfEnabled(int minutes)
		{
			if (minutes == 240) return TF240;
			if (minutes == 90) return TF90;
			if (minutes == 60) return TF60;
			if (minutes == 30) return TF30;
			if (minutes == 15) return TF15;
			if (minutes == 5) return TF5;
			return false;
		}

		private int WidthFor(int minutes)
		{
			if (minutes == 240) return LineWidth240;
			if (minutes == 90) return LineWidth90;
			if (minutes == 60) return LineWidth60;
			if (minutes == 30) return LineWidth30;
			if (minutes == 5) return LineWidth5;
			return LineWidth15;
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Asset A", GroupName = "Comparison Assets", Order = 1)]
		public bool UseAssetA { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Asset A", GroupName = "Comparison Assets", Order = 2)]
		public string AssetA { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Asset B", GroupName = "Comparison Assets", Order = 3)]
		public bool UseAssetB { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Asset B", GroupName = "Comparison Assets", Order = 4)]
		public string AssetB { get; set; }

		[NinjaScriptProperty]
		[Range(2, 20)]
		[Display(Name = "Pivot Lookback", GroupName = "Pivot", Order = 1)]
		public int PivotLookback { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Candle Direction Validation", GroupName = "Pivot", Order = 2)]
		public bool CandleDirectionValidation { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "TF 240", GroupName = "Timeframes", Order = 1)]
		public bool TF240 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "TF 90", GroupName = "Timeframes", Order = 2)]
		public bool TF90 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "TF 60", GroupName = "Timeframes", Order = 3)]
		public bool TF60 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "TF 30", GroupName = "Timeframes", Order = 4)]
		public bool TF30 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "TF 15", GroupName = "Timeframes", Order = 5)]
		public bool TF15 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "TF 5", GroupName = "Timeframes", Order = 6)]
		public bool TF5 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Minimum Distance", GroupName = "Filters", Order = 1)]
		public bool UseMinimumDistance { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Minimum Distance Uses ATR", GroupName = "Filters", Order = 2)]
		public bool MinDistanceUsesATR { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, 10)]
		[Display(Name = "Min Distance ATR", GroupName = "Filters", Order = 3)]
		public double MinDistanceATR { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, double.MaxValue)]
		[Display(Name = "Min Distance Points", GroupName = "Filters", Order = 4)]
		public double MinDistancePoints { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1440)]
		[Display(Name = "Max Sync Minutes", GroupName = "Filters", Order = 5)]
		public int MaxSyncMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Invalidation Mode", GroupName = "Invalidation", Order = 1)]
		public string InvalidationMode { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Max Signals", GroupName = "Invalidation", Order = 2)]
		public int MaxSignals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Lines", GroupName = "Style", Order = 1)]
		public bool ShowLines { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Labels", GroupName = "Style", Order = 2)]
		public bool ShowLabels { get; set; }

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Bull Color", GroupName = "Style", Order = 3)]
		public Brush BullColor { get; set; }
		[Browsable(false)] public string BullColorSerializable { get { return Serialize.BrushToString(BullColor); } set { BullColor = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Bear Color", GroupName = "Style", Order = 4)]
		public Brush BearColor { get; set; }
		[Browsable(false)] public string BearColorSerializable { get { return Serialize.BrushToString(BearColor); } set { BearColor = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Invalid Color", GroupName = "Style", Order = 5)]
		public Brush InvalidColor { get; set; }
		[Browsable(false)] public string InvalidColorSerializable { get { return Serialize.BrushToString(InvalidColor); } set { InvalidColor = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty][Range(1, 6)] public int LineWidth240 { get; set; }
		[NinjaScriptProperty][Range(1, 6)] public int LineWidth90 { get; set; }
		[NinjaScriptProperty][Range(1, 6)] public int LineWidth60 { get; set; }
		[NinjaScriptProperty][Range(1, 6)] public int LineWidth30 { get; set; }
		[NinjaScriptProperty][Range(1, 6)] public int LineWidth15 { get; set; }
		[NinjaScriptProperty][Range(1, 6)] public int LineWidth5 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label Font", GroupName = "Style", Order = 20)]
		public SimpleFont LabelFont { get; set; }

		[Browsable(false)][XmlIgnore] public Series<double> BullSmtSignal { get { return bullSmtSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BearSmtSignal { get { return bearSmtSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BullSmtTimeframe { get { return bullSmtTimeframe; } }
		[Browsable(false)][XmlIgnore] public Series<double> BearSmtTimeframe { get { return bearSmtTimeframe; } }
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ICT_SMT_MTF[] cacheICT_SMT_MTF;
		public ICT_SMT_MTF ICT_SMT_MTF(bool useAssetA, string assetA, bool useAssetB, string assetB, int pivotLookback, bool candleDirectionValidation, bool tF240, bool tF90, bool tF60, bool tF30, bool tF15, bool useMinimumDistance, bool minDistanceUsesATR, double minDistanceATR, double minDistancePoints, int maxSyncMinutes, string invalidationMode, int maxSignals, bool showLines, bool showLabels, Brush bullColor, Brush bearColor, Brush invalidColor, int lineWidth240, int lineWidth90, int lineWidth60, int lineWidth30, int lineWidth15, SimpleFont labelFont)
		{
			return ICT_SMT_MTF(Input, useAssetA, assetA, useAssetB, assetB, pivotLookback, candleDirectionValidation, tF240, tF90, tF60, tF30, tF15, useMinimumDistance, minDistanceUsesATR, minDistanceATR, minDistancePoints, maxSyncMinutes, invalidationMode, maxSignals, showLines, showLabels, bullColor, bearColor, invalidColor, lineWidth240, lineWidth90, lineWidth60, lineWidth30, lineWidth15, labelFont);
		}

		public ICT_SMT_MTF ICT_SMT_MTF(ISeries<double> input, bool useAssetA, string assetA, bool useAssetB, string assetB, int pivotLookback, bool candleDirectionValidation, bool tF240, bool tF90, bool tF60, bool tF30, bool tF15, bool useMinimumDistance, bool minDistanceUsesATR, double minDistanceATR, double minDistancePoints, int maxSyncMinutes, string invalidationMode, int maxSignals, bool showLines, bool showLabels, Brush bullColor, Brush bearColor, Brush invalidColor, int lineWidth240, int lineWidth90, int lineWidth60, int lineWidth30, int lineWidth15, SimpleFont labelFont)
		{
			if (cacheICT_SMT_MTF != null)
				for (int idx = 0; idx < cacheICT_SMT_MTF.Length; idx++)
					if (cacheICT_SMT_MTF[idx] != null && cacheICT_SMT_MTF[idx].UseAssetA == useAssetA && cacheICT_SMT_MTF[idx].AssetA == assetA && cacheICT_SMT_MTF[idx].UseAssetB == useAssetB && cacheICT_SMT_MTF[idx].AssetB == assetB && cacheICT_SMT_MTF[idx].PivotLookback == pivotLookback && cacheICT_SMT_MTF[idx].CandleDirectionValidation == candleDirectionValidation && cacheICT_SMT_MTF[idx].TF240 == tF240 && cacheICT_SMT_MTF[idx].TF90 == tF90 && cacheICT_SMT_MTF[idx].TF60 == tF60 && cacheICT_SMT_MTF[idx].TF30 == tF30 && cacheICT_SMT_MTF[idx].TF15 == tF15 && cacheICT_SMT_MTF[idx].UseMinimumDistance == useMinimumDistance && cacheICT_SMT_MTF[idx].MinDistanceUsesATR == minDistanceUsesATR && cacheICT_SMT_MTF[idx].MinDistanceATR == minDistanceATR && cacheICT_SMT_MTF[idx].MinDistancePoints == minDistancePoints && cacheICT_SMT_MTF[idx].MaxSyncMinutes == maxSyncMinutes && cacheICT_SMT_MTF[idx].InvalidationMode == invalidationMode && cacheICT_SMT_MTF[idx].MaxSignals == maxSignals && cacheICT_SMT_MTF[idx].ShowLines == showLines && cacheICT_SMT_MTF[idx].ShowLabels == showLabels && cacheICT_SMT_MTF[idx].BullColor == bullColor && cacheICT_SMT_MTF[idx].BearColor == bearColor && cacheICT_SMT_MTF[idx].InvalidColor == invalidColor && cacheICT_SMT_MTF[idx].LineWidth240 == lineWidth240 && cacheICT_SMT_MTF[idx].LineWidth90 == lineWidth90 && cacheICT_SMT_MTF[idx].LineWidth60 == lineWidth60 && cacheICT_SMT_MTF[idx].LineWidth30 == lineWidth30 && cacheICT_SMT_MTF[idx].LineWidth15 == lineWidth15 && cacheICT_SMT_MTF[idx].LabelFont == labelFont && cacheICT_SMT_MTF[idx].EqualsInput(input))
						return cacheICT_SMT_MTF[idx];
			return CacheIndicator<ICT_SMT_MTF>(new ICT_SMT_MTF(){ UseAssetA = useAssetA, AssetA = assetA, UseAssetB = useAssetB, AssetB = assetB, PivotLookback = pivotLookback, CandleDirectionValidation = candleDirectionValidation, TF240 = tF240, TF90 = tF90, TF60 = tF60, TF30 = tF30, TF15 = tF15, UseMinimumDistance = useMinimumDistance, MinDistanceUsesATR = minDistanceUsesATR, MinDistanceATR = minDistanceATR, MinDistancePoints = minDistancePoints, MaxSyncMinutes = maxSyncMinutes, InvalidationMode = invalidationMode, MaxSignals = maxSignals, ShowLines = showLines, ShowLabels = showLabels, BullColor = bullColor, BearColor = bearColor, InvalidColor = invalidColor, LineWidth240 = lineWidth240, LineWidth90 = lineWidth90, LineWidth60 = lineWidth60, LineWidth30 = lineWidth30, LineWidth15 = lineWidth15, LabelFont = labelFont }, input, ref cacheICT_SMT_MTF);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ICT_SMT_MTF ICT_SMT_MTF(bool useAssetA, string assetA, bool useAssetB, string assetB, int pivotLookback, bool candleDirectionValidation, bool tF240, bool tF90, bool tF60, bool tF30, bool tF15, bool useMinimumDistance, bool minDistanceUsesATR, double minDistanceATR, double minDistancePoints, int maxSyncMinutes, string invalidationMode, int maxSignals, bool showLines, bool showLabels, Brush bullColor, Brush bearColor, Brush invalidColor, int lineWidth240, int lineWidth90, int lineWidth60, int lineWidth30, int lineWidth15, SimpleFont labelFont)
		{
			return indicator.ICT_SMT_MTF(Input, useAssetA, assetA, useAssetB, assetB, pivotLookback, candleDirectionValidation, tF240, tF90, tF60, tF30, tF15, useMinimumDistance, minDistanceUsesATR, minDistanceATR, minDistancePoints, maxSyncMinutes, invalidationMode, maxSignals, showLines, showLabels, bullColor, bearColor, invalidColor, lineWidth240, lineWidth90, lineWidth60, lineWidth30, lineWidth15, labelFont);
		}

		public Indicators.ICT_SMT_MTF ICT_SMT_MTF(ISeries<double> input , bool useAssetA, string assetA, bool useAssetB, string assetB, int pivotLookback, bool candleDirectionValidation, bool tF240, bool tF90, bool tF60, bool tF30, bool tF15, bool useMinimumDistance, bool minDistanceUsesATR, double minDistanceATR, double minDistancePoints, int maxSyncMinutes, string invalidationMode, int maxSignals, bool showLines, bool showLabels, Brush bullColor, Brush bearColor, Brush invalidColor, int lineWidth240, int lineWidth90, int lineWidth60, int lineWidth30, int lineWidth15, SimpleFont labelFont)
		{
			return indicator.ICT_SMT_MTF(input, useAssetA, assetA, useAssetB, assetB, pivotLookback, candleDirectionValidation, tF240, tF90, tF60, tF30, tF15, useMinimumDistance, minDistanceUsesATR, minDistanceATR, minDistancePoints, maxSyncMinutes, invalidationMode, maxSignals, showLines, showLabels, bullColor, bearColor, invalidColor, lineWidth240, lineWidth90, lineWidth60, lineWidth30, lineWidth15, labelFont);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ICT_SMT_MTF ICT_SMT_MTF(bool useAssetA, string assetA, bool useAssetB, string assetB, int pivotLookback, bool candleDirectionValidation, bool tF240, bool tF90, bool tF60, bool tF30, bool tF15, bool useMinimumDistance, bool minDistanceUsesATR, double minDistanceATR, double minDistancePoints, int maxSyncMinutes, string invalidationMode, int maxSignals, bool showLines, bool showLabels, Brush bullColor, Brush bearColor, Brush invalidColor, int lineWidth240, int lineWidth90, int lineWidth60, int lineWidth30, int lineWidth15, SimpleFont labelFont)
		{
			return indicator.ICT_SMT_MTF(Input, useAssetA, assetA, useAssetB, assetB, pivotLookback, candleDirectionValidation, tF240, tF90, tF60, tF30, tF15, useMinimumDistance, minDistanceUsesATR, minDistanceATR, minDistancePoints, maxSyncMinutes, invalidationMode, maxSignals, showLines, showLabels, bullColor, bearColor, invalidColor, lineWidth240, lineWidth90, lineWidth60, lineWidth30, lineWidth15, labelFont);
		}

		public Indicators.ICT_SMT_MTF ICT_SMT_MTF(ISeries<double> input , bool useAssetA, string assetA, bool useAssetB, string assetB, int pivotLookback, bool candleDirectionValidation, bool tF240, bool tF90, bool tF60, bool tF30, bool tF15, bool useMinimumDistance, bool minDistanceUsesATR, double minDistanceATR, double minDistancePoints, int maxSyncMinutes, string invalidationMode, int maxSignals, bool showLines, bool showLabels, Brush bullColor, Brush bearColor, Brush invalidColor, int lineWidth240, int lineWidth90, int lineWidth60, int lineWidth30, int lineWidth15, SimpleFont labelFont)
		{
			return indicator.ICT_SMT_MTF(input, useAssetA, assetA, useAssetB, assetB, pivotLookback, candleDirectionValidation, tF240, tF90, tF60, tF30, tF15, useMinimumDistance, minDistanceUsesATR, minDistanceATR, minDistancePoints, maxSyncMinutes, invalidationMode, maxSignals, showLines, showLabels, bullColor, bearColor, invalidColor, lineWidth240, lineWidth90, lineWidth60, lineWidth30, lineWidth15, labelFont);
		}
	}
}

#endregion
