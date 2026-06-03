#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using Brush = System.Windows.Media.Brush;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public interface IICTStructureFactSource
	{
		Series<double> BullBosSignal { get; }
		Series<double> BearBosSignal { get; }
		Series<double> BullChochSignal { get; }
		Series<double> BearChochSignal { get; }
		Series<double> BullCisdSignal { get; }
		Series<double> BearCisdSignal { get; }
		Series<double> LastSwingHigh { get; }
		Series<double> LastSwingLow { get; }
	}

	[Gui.CategoryOrder("Structure", 1)]
	[Gui.CategoryOrder("CISD", 2)]
	[Gui.CategoryOrder("Style", 3)]
	public class ICT_Structure_Suite : Indicator, IICTStructureFactSource
	{
		private struct SwingPoint
		{
			public double Price;
			public DateTime Time;
			public int Bar;
		}

		private SwingPoint lastHigh;
		private SwingPoint prevHigh;
		private SwingPoint lastLow;
		private SwingPoint prevLow;
		private int structureBias;
		private int seq;

		private Series<double> bullBosSignal;
		private Series<double> bearBosSignal;
		private Series<double> bullChochSignal;
		private Series<double> bearChochSignal;
		private Series<double> bullCisdSignal;
		private Series<double> bearCisdSignal;
		private Series<double> lastSwingHighSeries;
		private Series<double> lastSwingLowSeries;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "ICT_Structure_Suite";
				Description = "Lightweight ICT structure suite: swing pivots, BOS/CHoCH, CISD.";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DrawOnPricePanel = true;
				DisplayInDataBox = false;
				PaintPriceMarkers = false;
				IsSuspendedWhileInactive = true;

				SwingLength = 3;
				CisdLookback = 5;
				RequireDisplacement = false;
				DisplacementFactor = 0.5;
				ShowStructure = true;
				ShowCISD = true;
				BullColor = Brushes.LimeGreen;
				BearColor = Brushes.Crimson;
				CisdColor = Brushes.Goldenrod;
				LineWidth = 2;
				LabelFont = new SimpleFont("Arial", 10);
			}
			else if (State == State.DataLoaded)
			{
				bullBosSignal = new Series<double>(this);
				bearBosSignal = new Series<double>(this);
				bullChochSignal = new Series<double>(this);
				bearChochSignal = new Series<double>(this);
				bullCisdSignal = new Series<double>(this);
				bearCisdSignal = new Series<double>(this);
				lastSwingHighSeries = new Series<double>(this);
				lastSwingLowSeries = new Series<double>(this);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < SwingLength * 2 + CisdLookback + 2)
				return;

			ResetSignals();
			DetectSwings();
			DetectStructureBreaks();
			DetectCisd();
			PublishLevels();
		}

		private void ResetSignals()
		{
			if (bullBosSignal == null) return;
			bullBosSignal[0] = 0;
			bearBosSignal[0] = 0;
			bullChochSignal[0] = 0;
			bearChochSignal[0] = 0;
			bullCisdSignal[0] = 0;
			bearCisdSignal[0] = 0;
		}

		private void DetectSwings()
		{
			double ph = High[SwingLength];
			double pl = Low[SwingLength];
			bool isHigh = true;
			bool isLow = true;
			for (int i = 0; i <= SwingLength * 2; i++)
			{
				if (i == SwingLength) continue;
				if (High[i] >= ph) isHigh = false;
				if (Low[i] <= pl) isLow = false;
			}

			if (isHigh)
			{
				prevHigh = lastHigh;
				lastHigh = new SwingPoint { Price = ph, Time = Time[SwingLength], Bar = CurrentBar - SwingLength };
				if (ShowStructure) Draw.Text(this, NextTag("SH"), false, "SH", lastHigh.Time, lastHigh.Price, 8, BearColor, LabelFont, System.Windows.TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
			}
			if (isLow)
			{
				prevLow = lastLow;
				lastLow = new SwingPoint { Price = pl, Time = Time[SwingLength], Bar = CurrentBar - SwingLength };
				if (ShowStructure) Draw.Text(this, NextTag("SL"), false, "SL", lastLow.Time, lastLow.Price, -8, BullColor, LabelFont, System.Windows.TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
			}
		}

		private void DetectStructureBreaks()
		{
			if (lastHigh.Price > 0 && Close[0] > lastHigh.Price)
			{
				if (RequireDisplacement && !HasDisplacement()) return;
				bool choch = structureBias < 0;
				structureBias = 1;
				if (choch) bullChochSignal[0] = 1; else bullBosSignal[0] = 1;
				if (ShowStructure) DrawBreak(lastHigh.Time, lastHigh.Price, choch ? "CHoCH" : "BOS", BullColor);
			}
			if (lastLow.Price > 0 && Close[0] < lastLow.Price)
			{
				if (RequireDisplacement && !HasDisplacement()) return;
				bool choch = structureBias > 0;
				structureBias = -1;
				if (choch) bearChochSignal[0] = -1; else bearBosSignal[0] = -1;
				if (ShowStructure) DrawBreak(lastLow.Time, lastLow.Price, choch ? "CHoCH" : "BOS", BearColor);
			}
		}

		private void DetectCisd()
		{
			if (!ShowCISD) return;
			double highBody = double.MinValue;
			double lowBody = double.MaxValue;
			for (int i = 1; i <= CisdLookback; i++)
			{
				highBody = Math.Max(highBody, Math.Max(Open[i], Close[i]));
				lowBody = Math.Min(lowBody, Math.Min(Open[i], Close[i]));
			}
			if (structureBias >= 0 && Close[0] < lowBody)
			{
				bearCisdSignal[0] = -1;
				DrawCisd(lowBody, "CISD", BearColor);
			}
			else if (structureBias <= 0 && Close[0] > highBody)
			{
				bullCisdSignal[0] = 1;
				DrawCisd(highBody, "CISD", BullColor);
			}
		}

		private bool HasDisplacement()
		{
			double avg = 0;
			int count = Math.Min(CurrentBar, 14);
			for (int i = 1; i <= count; i++) avg += High[i] - Low[i];
			avg = count > 0 ? avg / count : 0;
			return (High[0] - Low[0]) >= avg * DisplacementFactor;
		}

		private void DrawBreak(DateTime start, double price, string label, Brush color)
		{
			string tag = NextTag(label);
			Draw.Line(this, tag, false, start, price, Time[0], price, color, DashStyleHelper.Solid, LineWidth);
			Draw.Text(this, tag + "_TXT", false, label, Time[0], price, 0, color, LabelFont, System.Windows.TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void DrawCisd(double price, string label, Brush color)
		{
			string tag = NextTag("CISD");
			Draw.Line(this, tag, false, Time[Math.Min(CurrentBar, CisdLookback)], price, Time[0], price, color, DashStyleHelper.Dash, LineWidth);
			Draw.Text(this, tag + "_TXT", false, label, Time[0], price, 0, color, LabelFont, System.Windows.TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void PublishLevels()
		{
			if (lastSwingHighSeries == null) return;
			lastSwingHighSeries[0] = lastHigh.Price > 0 ? lastHigh.Price : double.NaN;
			lastSwingLowSeries[0] = lastLow.Price > 0 ? lastLow.Price : double.NaN;
		}

		private string NextTag(string prefix)
		{
			seq++;
			return "ICT_STRUCT_" + prefix + "_" + seq;
		}

		[NinjaScriptProperty][Range(2, 20)]
		public int SwingLength { get; set; }
		[NinjaScriptProperty][Range(2, 50)]
		public int CisdLookback { get; set; }
		[NinjaScriptProperty]
		public bool RequireDisplacement { get; set; }
		[NinjaScriptProperty][Range(0.1, 5)]
		public double DisplacementFactor { get; set; }
		[NinjaScriptProperty]
		public bool ShowStructure { get; set; }
		[NinjaScriptProperty]
		public bool ShowCISD { get; set; }
		[XmlIgnore][NinjaScriptProperty] public Brush BullColor { get; set; }
		[Browsable(false)] public string BullColorSerializable { get { return Serialize.BrushToString(BullColor); } set { BullColor = Serialize.StringToBrush(value); } }
		[XmlIgnore][NinjaScriptProperty] public Brush BearColor { get; set; }
		[Browsable(false)] public string BearColorSerializable { get { return Serialize.BrushToString(BearColor); } set { BearColor = Serialize.StringToBrush(value); } }
		[XmlIgnore][NinjaScriptProperty] public Brush CisdColor { get; set; }
		[Browsable(false)] public string CisdColorSerializable { get { return Serialize.BrushToString(CisdColor); } set { CisdColor = Serialize.StringToBrush(value); } }
		[NinjaScriptProperty][Range(1, 6)]
		public int LineWidth { get; set; }
		[NinjaScriptProperty]
		public SimpleFont LabelFont { get; set; }

		[Browsable(false)][XmlIgnore] public Series<double> BullBosSignal { get { return bullBosSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BearBosSignal { get { return bearBosSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BullChochSignal { get { return bullChochSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BearChochSignal { get { return bearChochSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BullCisdSignal { get { return bullCisdSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BearCisdSignal { get { return bearCisdSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> LastSwingHigh { get { return lastSwingHighSeries; } }
		[Browsable(false)][XmlIgnore] public Series<double> LastSwingLow { get { return lastSwingLowSeries; } }
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ICT_Structure_Suite[] cacheICT_Structure_Suite;
		public ICT_Structure_Suite ICT_Structure_Suite(int swingLength, int cisdLookback, bool requireDisplacement, double displacementFactor, bool showStructure, bool showCISD, Brush bullColor, Brush bearColor, Brush cisdColor, int lineWidth, SimpleFont labelFont)
		{
			return ICT_Structure_Suite(Input, swingLength, cisdLookback, requireDisplacement, displacementFactor, showStructure, showCISD, bullColor, bearColor, cisdColor, lineWidth, labelFont);
		}

		public ICT_Structure_Suite ICT_Structure_Suite(ISeries<double> input, int swingLength, int cisdLookback, bool requireDisplacement, double displacementFactor, bool showStructure, bool showCISD, Brush bullColor, Brush bearColor, Brush cisdColor, int lineWidth, SimpleFont labelFont)
		{
			if (cacheICT_Structure_Suite != null)
				for (int idx = 0; idx < cacheICT_Structure_Suite.Length; idx++)
					if (cacheICT_Structure_Suite[idx] != null && cacheICT_Structure_Suite[idx].SwingLength == swingLength && cacheICT_Structure_Suite[idx].CisdLookback == cisdLookback && cacheICT_Structure_Suite[idx].RequireDisplacement == requireDisplacement && cacheICT_Structure_Suite[idx].DisplacementFactor == displacementFactor && cacheICT_Structure_Suite[idx].ShowStructure == showStructure && cacheICT_Structure_Suite[idx].ShowCISD == showCISD && cacheICT_Structure_Suite[idx].BullColor == bullColor && cacheICT_Structure_Suite[idx].BearColor == bearColor && cacheICT_Structure_Suite[idx].CisdColor == cisdColor && cacheICT_Structure_Suite[idx].LineWidth == lineWidth && cacheICT_Structure_Suite[idx].LabelFont == labelFont && cacheICT_Structure_Suite[idx].EqualsInput(input))
						return cacheICT_Structure_Suite[idx];
			return CacheIndicator<ICT_Structure_Suite>(new ICT_Structure_Suite(){ SwingLength = swingLength, CisdLookback = cisdLookback, RequireDisplacement = requireDisplacement, DisplacementFactor = displacementFactor, ShowStructure = showStructure, ShowCISD = showCISD, BullColor = bullColor, BearColor = bearColor, CisdColor = cisdColor, LineWidth = lineWidth, LabelFont = labelFont }, input, ref cacheICT_Structure_Suite);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ICT_Structure_Suite ICT_Structure_Suite(int swingLength, int cisdLookback, bool requireDisplacement, double displacementFactor, bool showStructure, bool showCISD, Brush bullColor, Brush bearColor, Brush cisdColor, int lineWidth, SimpleFont labelFont)
		{
			return indicator.ICT_Structure_Suite(Input, swingLength, cisdLookback, requireDisplacement, displacementFactor, showStructure, showCISD, bullColor, bearColor, cisdColor, lineWidth, labelFont);
		}

		public Indicators.ICT_Structure_Suite ICT_Structure_Suite(ISeries<double> input , int swingLength, int cisdLookback, bool requireDisplacement, double displacementFactor, bool showStructure, bool showCISD, Brush bullColor, Brush bearColor, Brush cisdColor, int lineWidth, SimpleFont labelFont)
		{
			return indicator.ICT_Structure_Suite(input, swingLength, cisdLookback, requireDisplacement, displacementFactor, showStructure, showCISD, bullColor, bearColor, cisdColor, lineWidth, labelFont);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ICT_Structure_Suite ICT_Structure_Suite(int swingLength, int cisdLookback, bool requireDisplacement, double displacementFactor, bool showStructure, bool showCISD, Brush bullColor, Brush bearColor, Brush cisdColor, int lineWidth, SimpleFont labelFont)
		{
			return indicator.ICT_Structure_Suite(Input, swingLength, cisdLookback, requireDisplacement, displacementFactor, showStructure, showCISD, bullColor, bearColor, cisdColor, lineWidth, labelFont);
		}

		public Indicators.ICT_Structure_Suite ICT_Structure_Suite(ISeries<double> input , int swingLength, int cisdLookback, bool requireDisplacement, double displacementFactor, bool showStructure, bool showCISD, Brush bullColor, Brush bearColor, Brush cisdColor, int lineWidth, SimpleFont labelFont)
		{
			return indicator.ICT_Structure_Suite(input, swingLength, cisdLookback, requireDisplacement, displacementFactor, showStructure, showCISD, bullColor, bearColor, cisdColor, lineWidth, labelFont);
		}
	}
}

#endregion
