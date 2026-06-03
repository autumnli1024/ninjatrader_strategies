#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public interface IICTPdaFactSource
	{
		Series<double> NearestBullTop { get; }
		Series<double> NearestBullBottom { get; }
		Series<double> NearestBullCE { get; }
		Series<double> NearestBearTop { get; }
		Series<double> NearestBearBottom { get; }
		Series<double> NearestBearCE { get; }
		Series<double> ActiveBias { get; }
	}

	[Gui.CategoryOrder("HTF Source", 1)]
	[Gui.CategoryOrder("Detection", 2)]
	[Gui.CategoryOrder("Visual", 3)]
	[Gui.CategoryOrder("Output", 4)]
	public class ICT_HTF_PDA_Projector : Indicator, IICTPdaFactSource
	{
		private const int HtfBarsInProgress = 1;

		private readonly List<ProjectedPdaZone> zones = new List<ProjectedPdaZone>();
		private Series<double> nearestBullTop;
		private Series<double> nearestBullBottom;
		private Series<double> nearestBullCe;
		private Series<double> nearestBearTop;
		private Series<double> nearestBearBottom;
		private Series<double> nearestBearCe;
		private Series<double> activeBias;
		private string instanceId;
		private DateTime lastPrimaryTime = DateTime.MinValue;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "ICT_HTF_PDA_Projector";
				Description = "Projects selected HTF PDA/FVG zones onto the current chart for top-down ICT context.";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DrawOnPricePanel = true;
				DisplayInDataBox = false;
				PaintPriceMarkers = false;
				ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				HTFMinutes = 60;
				MaxActiveZones = 12;
				MinFvgTicks = 2;
				RequireSameDirectionCandles = false;
				HideFilledZones = true;
				FillType = PDAFillType.CLOSE_THROUGH;
				ExtendHoursForward = 24;
				DrawBullishZones = true;
				DrawBearishZones = true;
				DrawConsequentEncroachment = true;
				DrawLabels = true;
				ZoneOpacity = 22;
				FilledZoneOpacity = 8;
				LineWidth = 2;
				LabelFont = new SimpleFont("Arial", 10);
				BullZoneBrush = Brushes.MediumAquamarine;
				BearZoneBrush = Brushes.LightCoral;
				CeLineBrush = Brushes.Black;
				LabelTextBrush = Brushes.Black;
			}
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Minute, Math.Max(1, HTFMinutes));
				instanceId = Guid.NewGuid().ToString("N");
				FreezeBrush(BullZoneBrush);
				FreezeBrush(BearZoneBrush);
				FreezeBrush(CeLineBrush);
				FreezeBrush(LabelTextBrush);
			}
			else if (State == State.DataLoaded)
			{
				nearestBullTop = new Series<double>(this);
				nearestBullBottom = new Series<double>(this);
				nearestBullCe = new Series<double>(this);
				nearestBearTop = new Series<double>(this);
				nearestBearBottom = new Series<double>(this);
				nearestBearCe = new Series<double>(this);
				activeBias = new Series<double>(this);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 0)
			{
				lastPrimaryTime = Time[0];
				PublishNearestZones();
				DrawZones();
				return;
			}

			if (BarsInProgress != HtfBarsInProgress || CurrentBars[HtfBarsInProgress] < 3)
				return;

			UpdateZoneState();
			DetectHtfFvg();
			TrimZones();
			DrawZones();
		}

		public override string DisplayName
		{
			get { return Name + "(" + HTFMinutes + "m)"; }
		}

		private void DetectHtfFvg()
		{
			bool bullBarsOk = !RequireSameDirectionCandles
				|| (Closes[HtfBarsInProgress][2] > Opens[HtfBarsInProgress][2]
					&& Closes[HtfBarsInProgress][1] > Opens[HtfBarsInProgress][1]
					&& Closes[HtfBarsInProgress][0] > Opens[HtfBarsInProgress][0]);
			bool bearBarsOk = !RequireSameDirectionCandles
				|| (Closes[HtfBarsInProgress][2] < Opens[HtfBarsInProgress][2]
					&& Closes[HtfBarsInProgress][1] < Opens[HtfBarsInProgress][1]
					&& Closes[HtfBarsInProgress][0] < Opens[HtfBarsInProgress][0]);

			if (bullBarsOk && Lows[HtfBarsInProgress][0] > Highs[HtfBarsInProgress][2])
				AddZone(1, Highs[HtfBarsInProgress][2], Lows[HtfBarsInProgress][0], Times[HtfBarsInProgress][2]);

			if (bearBarsOk && Highs[HtfBarsInProgress][0] < Lows[HtfBarsInProgress][2])
				AddZone(-1, Highs[HtfBarsInProgress][0], Lows[HtfBarsInProgress][2], Times[HtfBarsInProgress][2]);
		}

		private void AddZone(int side, double lower, double upper, DateTime startTime)
		{
			double bottom = Math.Min(lower, upper);
			double top = Math.Max(lower, upper);
			if ((top - bottom) < MinFvgTicks * TickSize)
				return;

			ProjectedPdaZone zone = new ProjectedPdaZone
			{
				Id = instanceId + "_" + CurrentBars[HtfBarsInProgress].ToString() + "_" + (side > 0 ? "B" : "S"),
				Side = side,
				Top = top,
				Bottom = bottom,
				CE = (top + bottom) * 0.5,
				StartTime = startTime,
				FormedTime = Times[HtfBarsInProgress][0],
				EndTime = Times[HtfBarsInProgress][0].AddHours(Math.Max(1, ExtendHoursForward))
			};
			zones.Insert(0, zone);
		}

		private void UpdateZoneState()
		{
			for (int i = 0; i < zones.Count; i++)
			{
				ProjectedPdaZone zone = zones[i];
				if (zone.IsFilled)
					continue;

				bool filled = zone.Side > 0
					? (FillType == PDAFillType.CLOSE_THROUGH ? Closes[HtfBarsInProgress][0] <= zone.Bottom : Lows[HtfBarsInProgress][0] <= zone.Bottom)
					: (FillType == PDAFillType.CLOSE_THROUGH ? Closes[HtfBarsInProgress][0] >= zone.Top : Highs[HtfBarsInProgress][0] >= zone.Top);

				if (filled)
				{
					zone.IsFilled = true;
					zone.FilledTime = Times[HtfBarsInProgress][0];
					if (HideFilledZones)
						RemoveZoneDrawObjects(zone);
				}
				else
					zone.EndTime = Times[HtfBarsInProgress][0].AddHours(Math.Max(1, ExtendHoursForward));
			}
		}

		private void TrimZones()
		{
			for (int i = zones.Count - 1; i >= 0; i--)
			{
				if (HideFilledZones && zones[i].IsFilled)
				{
					zones.RemoveAt(i);
					continue;
				}
			}

			while (zones.Count > Math.Max(1, MaxActiveZones))
			{
				RemoveZoneDrawObjects(zones[zones.Count - 1]);
				zones.RemoveAt(zones.Count - 1);
			}
		}

		private void DrawZones()
		{
			if (CurrentBar < 1)
				return;

			DateTime endTime = lastPrimaryTime == DateTime.MinValue ? Time[0].AddHours(Math.Max(1, ExtendHoursForward)) : lastPrimaryTime.AddHours(Math.Max(1, ExtendHoursForward));
			for (int i = zones.Count - 1; i >= 0; i--)
			{
				ProjectedPdaZone zone = zones[i];
				if (zone.IsFilled && HideFilledZones)
					continue;
				if (zone.Side > 0 && !DrawBullishZones)
					continue;
				if (zone.Side < 0 && !DrawBearishZones)
					continue;

				Brush zoneBrush = zone.Side > 0 ? BullZoneBrush : BearZoneBrush;
				int opacity = zone.IsFilled ? FilledZoneOpacity : ZoneOpacity;
				string prefix = "HTF_PDA_" + zone.Id;

				Draw.Rectangle(this, prefix + "_BOX", false, zone.StartTime, zone.Bottom, endTime, zone.Top, zoneBrush, zoneBrush, opacity, true);

				if (DrawConsequentEncroachment)
					Draw.Line(this, prefix + "_CE", false, zone.StartTime, zone.CE, endTime, zone.CE, CeLineBrush, DashStyleHelper.Dash, LineWidth);

				if (DrawLabels)
				{
					string htfLabel = FormatTimeframeLabel(HTFMinutes);
					string text = (zone.Side > 0 ? htfLabel + " Bull FVG" : htfLabel + " Bear FVG")
						+ " CE " + Instrument.MasterInstrument.FormatPrice(zone.CE);
					Draw.Text(this, prefix + "_TXT", false, text, endTime, zone.CE, 0, LabelTextBrush, LabelFont, TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
				}
			}
		}

		private string FormatTimeframeLabel(int minutes)
		{
			if (minutes <= 0)
				return "HTF";
			if (minutes % 1440 == 0)
				return "D" + (minutes / 1440).ToString();
			if (minutes % 60 == 0)
				return "H" + (minutes / 60).ToString();
			return minutes.ToString() + "m";
		}

		private void PublishNearestZones()
		{
			if (nearestBullTop == null)
				return;

			ProjectedPdaZone bull = null;
			ProjectedPdaZone bear = null;
			double bullDistance = double.MaxValue;
			double bearDistance = double.MaxValue;

			for (int i = 0; i < zones.Count; i++)
			{
				ProjectedPdaZone zone = zones[i];
				if (zone.IsFilled)
					continue;

				double distance = Math.Abs(Close[0] - zone.CE);
				if (zone.Side > 0 && distance < bullDistance)
				{
					bull = zone;
					bullDistance = distance;
				}
				else if (zone.Side < 0 && distance < bearDistance)
				{
					bear = zone;
					bearDistance = distance;
				}
			}

			nearestBullTop[0] = bull != null ? bull.Top : double.NaN;
			nearestBullBottom[0] = bull != null ? bull.Bottom : double.NaN;
			nearestBullCe[0] = bull != null ? bull.CE : double.NaN;
			nearestBearTop[0] = bear != null ? bear.Top : double.NaN;
			nearestBearBottom[0] = bear != null ? bear.Bottom : double.NaN;
			nearestBearCe[0] = bear != null ? bear.CE : double.NaN;

			if (bull != null && bear != null)
				activeBias[0] = bullDistance <= bearDistance ? 1 : -1;
			else if (bull != null)
				activeBias[0] = 1;
			else if (bear != null)
				activeBias[0] = -1;
			else
				activeBias[0] = 0;
		}

		private void RemoveZoneDrawObjects(ProjectedPdaZone zone)
		{
			if (zone == null)
				return;
			string prefix = "HTF_PDA_" + zone.Id;
			RemoveDrawObject(prefix + "_BOX");
			RemoveDrawObject(prefix + "_CE");
			RemoveDrawObject(prefix + "_TXT");
		}

		private void FreezeBrush(Brush brush)
		{
			if (brush != null && brush.CanFreeze)
				brush.Freeze();
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(5, 240)]
		[Display(Name = "HTF Minutes", Order = 10, GroupName = "HTF Source")]
		public int HTFMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Max Active Zones", Order = 20, GroupName = "Detection")]
		public int MaxActiveZones { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Min FVG Ticks", Order = 30, GroupName = "Detection")]
		public int MinFvgTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require Same Direction Candles", Order = 40, GroupName = "Detection")]
		public bool RequireSameDirectionCandles { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fill Type", Order = 50, GroupName = "Detection")]
		public PDAFillType FillType { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Hide Filled Zones", Order = 60, GroupName = "Detection")]
		public bool HideFilledZones { get; set; }

		[NinjaScriptProperty]
		[Range(1, 168)]
		[Display(Name = "Extend Hours Forward", Order = 10, GroupName = "Visual")]
		public int ExtendHoursForward { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw Bullish Zones", Order = 20, GroupName = "Visual")]
		public bool DrawBullishZones { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw Bearish Zones", Order = 30, GroupName = "Visual")]
		public bool DrawBearishZones { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw CE", Order = 40, GroupName = "Visual")]
		public bool DrawConsequentEncroachment { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw Labels", Order = 50, GroupName = "Visual")]
		public bool DrawLabels { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Zone Opacity", Order = 60, GroupName = "Visual")]
		public int ZoneOpacity { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Filled Zone Opacity", Order = 70, GroupName = "Visual")]
		public int FilledZoneOpacity { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Line Width", Order = 80, GroupName = "Visual")]
		public int LineWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label Font", Order = 90, GroupName = "Visual")]
		public SimpleFont LabelFont { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bull Zone Color", Order = 100, GroupName = "Visual")]
		public Brush BullZoneBrush { get; set; }
		[Browsable(false)]
		public string BullZoneBrushSerializable { get { return Serialize.BrushToString(BullZoneBrush); } set { BullZoneBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bear Zone Color", Order = 110, GroupName = "Visual")]
		public Brush BearZoneBrush { get; set; }
		[Browsable(false)]
		public string BearZoneBrushSerializable { get { return Serialize.BrushToString(BearZoneBrush); } set { BearZoneBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "CE Line Color", Order = 120, GroupName = "Visual")]
		public Brush CeLineBrush { get; set; }
		[Browsable(false)]
		public string CeLineBrushSerializable { get { return Serialize.BrushToString(CeLineBrush); } set { CeLineBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Label Text Color", Order = 130, GroupName = "Visual")]
		public Brush LabelTextBrush { get; set; }
		[Browsable(false)]
		public string LabelTextBrushSerializable { get { return Serialize.BrushToString(LabelTextBrush); } set { LabelTextBrush = Serialize.StringToBrush(value); } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> NearestBullTop { get { return nearestBullTop; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> NearestBullBottom { get { return nearestBullBottom; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> NearestBullCE { get { return nearestBullCe; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> NearestBearTop { get { return nearestBearTop; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> NearestBearBottom { get { return nearestBearBottom; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> NearestBearCE { get { return nearestBearCe; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ActiveBias { get { return activeBias; } }
		#endregion

		private class ProjectedPdaZone
		{
			public string Id;
			public int Side;
			public double Top;
			public double Bottom;
			public double CE;
			public DateTime StartTime;
			public DateTime FormedTime;
			public DateTime EndTime;
			public bool IsFilled;
			public DateTime FilledTime;
		}
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ICT_HTF_PDA_Projector[] cacheICT_HTF_PDA_Projector;
		public ICT_HTF_PDA_Projector ICT_HTF_PDA_Projector(int hTFMinutes, int maxActiveZones, int minFvgTicks, bool requireSameDirectionCandles, PDAFillType fillType, bool hideFilledZones, int extendHoursForward, bool drawBullishZones, bool drawBearishZones, bool drawConsequentEncroachment, bool drawLabels, int zoneOpacity, int filledZoneOpacity, int lineWidth, SimpleFont labelFont, Brush bullZoneBrush, Brush bearZoneBrush, Brush ceLineBrush, Brush labelTextBrush)
		{
			return ICT_HTF_PDA_Projector(Input, hTFMinutes, maxActiveZones, minFvgTicks, requireSameDirectionCandles, fillType, hideFilledZones, extendHoursForward, drawBullishZones, drawBearishZones, drawConsequentEncroachment, drawLabels, zoneOpacity, filledZoneOpacity, lineWidth, labelFont, bullZoneBrush, bearZoneBrush, ceLineBrush, labelTextBrush);
		}

		public ICT_HTF_PDA_Projector ICT_HTF_PDA_Projector(ISeries<double> input, int hTFMinutes, int maxActiveZones, int minFvgTicks, bool requireSameDirectionCandles, PDAFillType fillType, bool hideFilledZones, int extendHoursForward, bool drawBullishZones, bool drawBearishZones, bool drawConsequentEncroachment, bool drawLabels, int zoneOpacity, int filledZoneOpacity, int lineWidth, SimpleFont labelFont, Brush bullZoneBrush, Brush bearZoneBrush, Brush ceLineBrush, Brush labelTextBrush)
		{
			if (cacheICT_HTF_PDA_Projector != null)
				for (int idx = 0; idx < cacheICT_HTF_PDA_Projector.Length; idx++)
					if (cacheICT_HTF_PDA_Projector[idx] != null && cacheICT_HTF_PDA_Projector[idx].HTFMinutes == hTFMinutes && cacheICT_HTF_PDA_Projector[idx].MaxActiveZones == maxActiveZones && cacheICT_HTF_PDA_Projector[idx].MinFvgTicks == minFvgTicks && cacheICT_HTF_PDA_Projector[idx].RequireSameDirectionCandles == requireSameDirectionCandles && cacheICT_HTF_PDA_Projector[idx].FillType == fillType && cacheICT_HTF_PDA_Projector[idx].HideFilledZones == hideFilledZones && cacheICT_HTF_PDA_Projector[idx].ExtendHoursForward == extendHoursForward && cacheICT_HTF_PDA_Projector[idx].DrawBullishZones == drawBullishZones && cacheICT_HTF_PDA_Projector[idx].DrawBearishZones == drawBearishZones && cacheICT_HTF_PDA_Projector[idx].DrawConsequentEncroachment == drawConsequentEncroachment && cacheICT_HTF_PDA_Projector[idx].DrawLabels == drawLabels && cacheICT_HTF_PDA_Projector[idx].ZoneOpacity == zoneOpacity && cacheICT_HTF_PDA_Projector[idx].FilledZoneOpacity == filledZoneOpacity && cacheICT_HTF_PDA_Projector[idx].LineWidth == lineWidth && cacheICT_HTF_PDA_Projector[idx].LabelFont == labelFont && cacheICT_HTF_PDA_Projector[idx].BullZoneBrush == bullZoneBrush && cacheICT_HTF_PDA_Projector[idx].BearZoneBrush == bearZoneBrush && cacheICT_HTF_PDA_Projector[idx].CeLineBrush == ceLineBrush && cacheICT_HTF_PDA_Projector[idx].LabelTextBrush == labelTextBrush && cacheICT_HTF_PDA_Projector[idx].EqualsInput(input))
						return cacheICT_HTF_PDA_Projector[idx];
			return CacheIndicator<ICT_HTF_PDA_Projector>(new ICT_HTF_PDA_Projector(){ HTFMinutes = hTFMinutes, MaxActiveZones = maxActiveZones, MinFvgTicks = minFvgTicks, RequireSameDirectionCandles = requireSameDirectionCandles, FillType = fillType, HideFilledZones = hideFilledZones, ExtendHoursForward = extendHoursForward, DrawBullishZones = drawBullishZones, DrawBearishZones = drawBearishZones, DrawConsequentEncroachment = drawConsequentEncroachment, DrawLabels = drawLabels, ZoneOpacity = zoneOpacity, FilledZoneOpacity = filledZoneOpacity, LineWidth = lineWidth, LabelFont = labelFont, BullZoneBrush = bullZoneBrush, BearZoneBrush = bearZoneBrush, CeLineBrush = ceLineBrush, LabelTextBrush = labelTextBrush }, input, ref cacheICT_HTF_PDA_Projector);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ICT_HTF_PDA_Projector ICT_HTF_PDA_Projector(int hTFMinutes, int maxActiveZones, int minFvgTicks, bool requireSameDirectionCandles, PDAFillType fillType, bool hideFilledZones, int extendHoursForward, bool drawBullishZones, bool drawBearishZones, bool drawConsequentEncroachment, bool drawLabels, int zoneOpacity, int filledZoneOpacity, int lineWidth, SimpleFont labelFont, Brush bullZoneBrush, Brush bearZoneBrush, Brush ceLineBrush, Brush labelTextBrush)
		{
			return indicator.ICT_HTF_PDA_Projector(Input, hTFMinutes, maxActiveZones, minFvgTicks, requireSameDirectionCandles, fillType, hideFilledZones, extendHoursForward, drawBullishZones, drawBearishZones, drawConsequentEncroachment, drawLabels, zoneOpacity, filledZoneOpacity, lineWidth, labelFont, bullZoneBrush, bearZoneBrush, ceLineBrush, labelTextBrush);
		}

		public Indicators.ICT_HTF_PDA_Projector ICT_HTF_PDA_Projector(ISeries<double> input , int hTFMinutes, int maxActiveZones, int minFvgTicks, bool requireSameDirectionCandles, PDAFillType fillType, bool hideFilledZones, int extendHoursForward, bool drawBullishZones, bool drawBearishZones, bool drawConsequentEncroachment, bool drawLabels, int zoneOpacity, int filledZoneOpacity, int lineWidth, SimpleFont labelFont, Brush bullZoneBrush, Brush bearZoneBrush, Brush ceLineBrush, Brush labelTextBrush)
		{
			return indicator.ICT_HTF_PDA_Projector(input, hTFMinutes, maxActiveZones, minFvgTicks, requireSameDirectionCandles, fillType, hideFilledZones, extendHoursForward, drawBullishZones, drawBearishZones, drawConsequentEncroachment, drawLabels, zoneOpacity, filledZoneOpacity, lineWidth, labelFont, bullZoneBrush, bearZoneBrush, ceLineBrush, labelTextBrush);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ICT_HTF_PDA_Projector ICT_HTF_PDA_Projector(int hTFMinutes, int maxActiveZones, int minFvgTicks, bool requireSameDirectionCandles, PDAFillType fillType, bool hideFilledZones, int extendHoursForward, bool drawBullishZones, bool drawBearishZones, bool drawConsequentEncroachment, bool drawLabels, int zoneOpacity, int filledZoneOpacity, int lineWidth, SimpleFont labelFont, Brush bullZoneBrush, Brush bearZoneBrush, Brush ceLineBrush, Brush labelTextBrush)
		{
			return indicator.ICT_HTF_PDA_Projector(Input, hTFMinutes, maxActiveZones, minFvgTicks, requireSameDirectionCandles, fillType, hideFilledZones, extendHoursForward, drawBullishZones, drawBearishZones, drawConsequentEncroachment, drawLabels, zoneOpacity, filledZoneOpacity, lineWidth, labelFont, bullZoneBrush, bearZoneBrush, ceLineBrush, labelTextBrush);
		}

		public Indicators.ICT_HTF_PDA_Projector ICT_HTF_PDA_Projector(ISeries<double> input , int hTFMinutes, int maxActiveZones, int minFvgTicks, bool requireSameDirectionCandles, PDAFillType fillType, bool hideFilledZones, int extendHoursForward, bool drawBullishZones, bool drawBearishZones, bool drawConsequentEncroachment, bool drawLabels, int zoneOpacity, int filledZoneOpacity, int lineWidth, SimpleFont labelFont, Brush bullZoneBrush, Brush bearZoneBrush, Brush ceLineBrush, Brush labelTextBrush)
		{
			return indicator.ICT_HTF_PDA_Projector(input, hTFMinutes, maxActiveZones, minFvgTicks, requireSameDirectionCandles, fillType, hideFilledZones, extendHoursForward, drawBullishZones, drawBearishZones, drawConsequentEncroachment, drawLabels, zoneOpacity, filledZoneOpacity, lineWidth, labelFont, bullZoneBrush, bearZoneBrush, ceLineBrush, labelTextBrush);
		}
	}
}

#endregion
