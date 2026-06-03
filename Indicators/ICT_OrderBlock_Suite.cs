#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using Brush = System.Windows.Media.Brush;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class ICT_OrderBlock_Suite : Indicator
	{
		private class ObZone
		{
			public string Tag;
			public int Side;
			public double Top;
			public double Bottom;
			public DateTime Start;
			public bool Mitigated;
			public bool Breaker;
		}

		private readonly List<ObZone> zones = new List<ObZone>();
		private double swingHigh;
		private double swingLow;
		private int seq;

		private Series<double> bullObSignal;
		private Series<double> bearObSignal;
		private Series<double> bullBreakerSignal;
		private Series<double> bearBreakerSignal;
		private Series<double> nearestBullObTop;
		private Series<double> nearestBullObBottom;
		private Series<double> nearestBearObTop;
		private Series<double> nearestBearObBottom;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "ICT_OrderBlock_Suite";
				Description = "Lightweight ICT order block and breaker suite.";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DrawOnPricePanel = true;
				DisplayInDataBox = false;
				PaintPriceMarkers = false;
				IsSuspendedWhileInactive = true;

				SwingLength = 3;
				SearchBars = 10;
				MaxZones = 20;
				HideMitigated = false;
				ShowOB = true;
				ShowBreaker = true;
				BullObBrush = Brushes.SeaGreen;
				BearObBrush = Brushes.IndianRed;
				BreakerBrush = Brushes.DodgerBlue;
				MitigatedBrush = Brushes.DimGray;
				Opacity = 18;
			}
			else if (State == State.DataLoaded)
			{
				bullObSignal = new Series<double>(this);
				bearObSignal = new Series<double>(this);
				bullBreakerSignal = new Series<double>(this);
				bearBreakerSignal = new Series<double>(this);
				nearestBullObTop = new Series<double>(this);
				nearestBullObBottom = new Series<double>(this);
				nearestBearObTop = new Series<double>(this);
				nearestBearObBottom = new Series<double>(this);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < SwingLength * 2 + SearchBars + 2)
				return;

			ResetSignals();
			UpdateSwings();
			DetectNewOrderBlocks();
			UpdateZones();
			PublishNearest();
		}

		private void ResetSignals()
		{
			if (bullObSignal == null) return;
			bullObSignal[0] = 0;
			bearObSignal[0] = 0;
			bullBreakerSignal[0] = 0;
			bearBreakerSignal[0] = 0;
			nearestBullObTop[0] = double.NaN;
			nearestBullObBottom[0] = double.NaN;
			nearestBearObTop[0] = double.NaN;
			nearestBearObBottom[0] = double.NaN;
		}

		private void UpdateSwings()
		{
			double ph = High[SwingLength];
			double pl = Low[SwingLength];
			bool isHigh = true, isLow = true;
			for (int i = 0; i <= SwingLength * 2; i++)
			{
				if (i == SwingLength) continue;
				if (High[i] >= ph) isHigh = false;
				if (Low[i] <= pl) isLow = false;
			}
			if (isHigh) swingHigh = ph;
			if (isLow) swingLow = pl;
		}

		private void DetectNewOrderBlocks()
		{
			if (swingHigh > 0 && Close[0] > swingHigh)
			{
				int idx = FindLastBearishCandle();
				if (idx >= 0)
				{
					AddZone(1, High[idx], Low[idx], Time[idx], false);
					bullObSignal[0] = 1;
				}
			}
			if (swingLow > 0 && Close[0] < swingLow)
			{
				int idx = FindLastBullishCandle();
				if (idx >= 0)
				{
					AddZone(-1, High[idx], Low[idx], Time[idx], false);
					bearObSignal[0] = -1;
				}
			}
		}

		private int FindLastBearishCandle()
		{
			for (int i = 1; i <= SearchBars; i++)
				if (Close[i] < Open[i])
					return i;
			return -1;
		}

		private int FindLastBullishCandle()
		{
			for (int i = 1; i <= SearchBars; i++)
				if (Close[i] > Open[i])
					return i;
			return -1;
		}

		private void AddZone(int side, double top, double bottom, DateTime start, bool breaker)
		{
			ObZone z = new ObZone { Tag = NextTag(breaker ? "BRK" : "OB"), Side = side, Top = Math.Max(top, bottom), Bottom = Math.Min(top, bottom), Start = start, Breaker = breaker };
			zones.Insert(0, z);
			DrawZone(z);
			while (zones.Count > Math.Max(1, MaxZones))
			{
				RemoveDrawObject(zones[zones.Count - 1].Tag);
				zones.RemoveAt(zones.Count - 1);
			}
		}

		private void UpdateZones()
		{
			for (int i = zones.Count - 1; i >= 0; i--)
			{
				ObZone z = zones[i];
				if (!z.Breaker)
				{
					bool invalid = z.Side > 0 ? Close[0] < z.Bottom : Close[0] > z.Top;
					if (invalid)
					{
						z.Breaker = true;
						z.Side = -z.Side;
						if (z.Side > 0) bullBreakerSignal[0] = 1; else bearBreakerSignal[0] = -1;
					}
				}

				bool touched = z.Side > 0 ? Low[0] <= z.Top && High[0] >= z.Bottom : High[0] >= z.Bottom && Low[0] <= z.Top;
				if (touched) z.Mitigated = true;
				if (z.Mitigated && HideMitigated)
				{
					RemoveDrawObject(z.Tag);
					zones.RemoveAt(i);
					continue;
				}
				DrawZone(z);
			}
		}

		private void DrawZone(ObZone z)
		{
			if ((!z.Breaker && !ShowOB) || (z.Breaker && !ShowBreaker)) return;
			Brush b = z.Mitigated ? MitigatedBrush : (z.Breaker ? BreakerBrush : (z.Side > 0 ? BullObBrush : BearObBrush));
			Draw.Rectangle(this, z.Tag, false, z.Start, z.Top, Time[0], z.Bottom, b, b, Opacity, true);
		}

		private void PublishNearest()
		{
			for (int i = 0; i < zones.Count; i++)
			{
				ObZone z = zones[i];
				if (z.Breaker || z.Mitigated) continue;
				if (z.Side > 0 && double.IsNaN(nearestBullObTop[0]))
				{
					nearestBullObTop[0] = z.Top; nearestBullObBottom[0] = z.Bottom;
				}
				if (z.Side < 0 && double.IsNaN(nearestBearObTop[0]))
				{
					nearestBearObTop[0] = z.Top; nearestBearObBottom[0] = z.Bottom;
				}
			}
		}

		private string NextTag(string prefix)
		{
			seq++;
			return "ICT_OB_" + prefix + "_" + seq;
		}

		[NinjaScriptProperty][Range(2, 20)] public int SwingLength { get; set; }
		[NinjaScriptProperty][Range(1, 50)] public int SearchBars { get; set; }
		[NinjaScriptProperty][Range(1, 200)] public int MaxZones { get; set; }
		[NinjaScriptProperty] public bool HideMitigated { get; set; }
		[NinjaScriptProperty] public bool ShowOB { get; set; }
		[NinjaScriptProperty] public bool ShowBreaker { get; set; }
		[XmlIgnore][NinjaScriptProperty] public Brush BullObBrush { get; set; }
		[Browsable(false)] public string BullObBrushSerializable { get { return Serialize.BrushToString(BullObBrush); } set { BullObBrush = Serialize.StringToBrush(value); } }
		[XmlIgnore][NinjaScriptProperty] public Brush BearObBrush { get; set; }
		[Browsable(false)] public string BearObBrushSerializable { get { return Serialize.BrushToString(BearObBrush); } set { BearObBrush = Serialize.StringToBrush(value); } }
		[XmlIgnore][NinjaScriptProperty] public Brush BreakerBrush { get; set; }
		[Browsable(false)] public string BreakerBrushSerializable { get { return Serialize.BrushToString(BreakerBrush); } set { BreakerBrush = Serialize.StringToBrush(value); } }
		[XmlIgnore][NinjaScriptProperty] public Brush MitigatedBrush { get; set; }
		[Browsable(false)] public string MitigatedBrushSerializable { get { return Serialize.BrushToString(MitigatedBrush); } set { MitigatedBrush = Serialize.StringToBrush(value); } }
		[NinjaScriptProperty][Range(1, 100)] public int Opacity { get; set; }

		[Browsable(false)][XmlIgnore] public Series<double> BullObSignal { get { return bullObSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BearObSignal { get { return bearObSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BullBreakerSignal { get { return bullBreakerSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BearBreakerSignal { get { return bearBreakerSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBullObTop { get { return nearestBullObTop; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBullObBottom { get { return nearestBullObBottom; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBearObTop { get { return nearestBearObTop; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBearObBottom { get { return nearestBearObBottom; } }
	}
}
