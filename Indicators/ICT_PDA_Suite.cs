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
using NinjaTrader.NinjaScript.DrawingTools;
using Brush = System.Windows.Media.Brush;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	[Gui.CategoryOrder("PDA Data Series", 1)]
	[Gui.CategoryOrder("Parameters", 3)]
	[Gui.CategoryOrder("Time Ranges", 5)]
	[Gui.CategoryOrder("PDA Colors", 7)]
	[Gui.CategoryOrder("PDA Data Series Label", 9)]
	public class ICT_PDA_Suite : Indicator
	{
		private const int MinBarsRequired = 4;
		private const string CeTag = "_CE";

		private int dataSeriesIdx;
		private string instanceId;
		private string dataSeriesLabel;
		private bool isDataLoaded;
		private DateTime future;
		private DateTime sessionEnd;
		private TimeSpan endTime1;
		private TimeSpan endTime2;
		private TimeSpan endTime3;
		private SessionIterator sessionIterator;

		private readonly List<PdaZone> fvgList = new List<PdaZone>();
		private readonly List<PdaZone> ifvgList = new List<PdaZone>();
		private readonly List<PdaZone> bprList = new List<PdaZone>();
		private readonly List<PdaZone> unicornList = new List<PdaZone>();

		private Series<double> bullFvgSignal;
		private Series<double> bearFvgSignal;
		private Series<double> bullIfvgSignal;
		private Series<double> bearIfvgSignal;
		private Series<double> bullBprSignal;
		private Series<double> bearBprSignal;
		private Series<double> fvgFilledSignal;
		private Series<double> nearestBullTop;
		private Series<double> nearestBullBottom;
		private Series<double> nearestBearTop;
		private Series<double> nearestBearBottom;
		private Series<double> nearestBullIfvgTop;
		private Series<double> nearestBullIfvgBottom;
		private Series<double> nearestBearIfvgTop;
		private Series<double> nearestBearIfvgBottom;
		private Series<double> nearestBullBprTop;
		private Series<double> nearestBullBprBottom;
		private Series<double> nearestBearBprTop;
		private Series<double> nearestBearBprBottom;
		private Series<double> bullUnicornSignal;
		private Series<double> bearUnicornSignal;
		private Series<double> nearestUnicornTop;
		private Series<double> nearestUnicornBottom;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "ICT PDA suite modeled after Fair Value Gap v0.0.3.1: FVG, IFVG, BPR.";
				Name = "ICT_PDA_Suite";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DrawOnPricePanel = true;
				DisplayInDataBox = false;
				PaintPriceMarkers = false;
				ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				UsePDADataSeries = false;
				PDABarsPeriodType = PDAPeriodTypes.Minute;
				PDASeriesPeriod = 1;

				ShowFVG = true;
				ShowIFVG = true;
				ShowBPR = true;
				ShowUnicorn = true;
				MaxDaysForward = 50;
				MaxActiveZones = 30;
				UseATR = true;
				ImpulseFactor = 1.1;
				ATRPeriod = 10;
				MinimumFVGSize = 2;
				AllBarsSameDirection = true;
				FillType = PDAFillType.CLOSE_THROUGH;
				HideFilledGaps = false;
				DisplayCE = true;
				MergeSameSideFVG = true;
				FvgMergeOverlapPct = 0.50;
				IncludeFilledFvgForBPR = true;
				BprBarsSince = 50;
				BprMinOverlap = 0;
				UnicornMinOverlap = 0;

				UseTimeRange1 = false;
				StartTime1 = new TimeSpan(3, 0, 0);
				TimeRangeMinutes1 = 60;
				UseTimeRange2 = false;
				StartTime2 = new TimeSpan(10, 0, 0);
				TimeRangeMinutes2 = 60;
				UseTimeRange3 = false;
				StartTime3 = new TimeSpan(14, 0, 0);
				TimeRangeMinutes3 = 60;

				DrawLabel = false;
				LabelPosition = TextPosition.TopRight;
				LabelFont = new SimpleFont("Verdana", 12);
				LabelTextBrush = Brushes.WhiteSmoke;
				LabelBorderBrush = Brushes.DimGray;
				LabelFillBrush = Brushes.DarkSlateGray;
				LabelFillOpacity = 50;

				BullFvgBrush = Brushes.LimeGreen;
				BullFvgAreaBrush = Brushes.LimeGreen;
				BullFvgFilledBrush = Brushes.Green;
				BearFvgBrush = Brushes.Crimson;
				BearFvgAreaBrush = Brushes.Crimson;
				BearFvgFilledBrush = Brushes.DarkRed;
				BullIfvgBrush = Brushes.DeepSkyBlue;
				BearIfvgBrush = Brushes.DodgerBlue;
				BprBrush = Brushes.Goldenrod;
				UnicornBrush = Brushes.MediumPurple;
				ActiveAreaOpacity = 20;
				FilledAreaOpacity = 15;
				IfvgAreaOpacity = 16;
				BprAreaOpacity = 18;
				UnicornAreaOpacity = 20;
			}
			else if (State == State.Configure)
			{
				if (UsePDADataSeries)
				{
					AddDataSeries((BarsPeriodType)PDABarsPeriodType, PDASeriesPeriod);
					dataSeriesIdx = 1;
				}
				else
					dataSeriesIdx = 0;

				instanceId = Guid.NewGuid().ToString();
				endTime1 = StartTime1.Add(TimeSpan.FromMinutes(TimeRangeMinutes1));
				endTime2 = StartTime2.Add(TimeSpan.FromMinutes(TimeRangeMinutes2));
				endTime3 = StartTime3.Add(TimeSpan.FromMinutes(TimeRangeMinutes3));
				FreezeBrushes();
			}
			else if (State == State.DataLoaded)
			{
				isDataLoaded = true;
				dataSeriesLabel = "PDA(" + BarsArray[dataSeriesIdx].BarsPeriod.ToString() + ")";

				bullFvgSignal = new Series<double>(this);
				bearFvgSignal = new Series<double>(this);
				bullIfvgSignal = new Series<double>(this);
				bearIfvgSignal = new Series<double>(this);
				bullBprSignal = new Series<double>(this);
				bearBprSignal = new Series<double>(this);
				fvgFilledSignal = new Series<double>(this);
				nearestBullTop = new Series<double>(this);
				nearestBullBottom = new Series<double>(this);
				nearestBearTop = new Series<double>(this);
				nearestBearBottom = new Series<double>(this);
				nearestBullIfvgTop = new Series<double>(this);
				nearestBullIfvgBottom = new Series<double>(this);
				nearestBearIfvgTop = new Series<double>(this);
				nearestBearIfvgBottom = new Series<double>(this);
				nearestBullBprTop = new Series<double>(this);
				nearestBullBprBottom = new Series<double>(this);
				nearestBearBprTop = new Series<double>(this);
				nearestBearBprBottom = new Series<double>(this);
				bullUnicornSignal = new Series<double>(this);
				bearUnicornSignal = new Series<double>(this);
				nearestUnicornTop = new Series<double>(this);
				nearestUnicornBottom = new Series<double>(this);
			}
			else if (State == State.Historical)
			{
				sessionIterator = new SessionIterator(Bars);
				sessionIterator.GetNextSession(Bars.GetTime(0), true);
				sessionEnd = sessionIterator.ActualSessionEnd;
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != dataSeriesIdx || CurrentBars[dataSeriesIdx] < MinBarsRequired)
				return;

			if (sessionIterator != null && Bars.IsFirstBarOfSession)
			{
				sessionIterator.GetNextSession(Times[dataSeriesIdx][0], true);
				sessionEnd = sessionIterator.ActualSessionEnd;
			}

			ResetSignals();

			if (DrawLabel)
				Draw.TextFixed(this, "PDALABEL_" + instanceId, DataSeriesStatusText(), LabelPosition, LabelTextBrush, LabelFont, LabelBorderBrush, LabelFillBrush, LabelFillOpacity);

			UpdateExistingZones();
			DetectNewFvg();
			PublishNearestFvg();
		}

		public override string DisplayName
		{
			get { return isDataLoaded ? (UsePDADataSeries ? dataSeriesLabel : "PDA") : Name; }
		}

		private void ResetSignals()
		{
			if (bullFvgSignal == null) return;
			bullFvgSignal[0] = 0;
			bearFvgSignal[0] = 0;
			bullIfvgSignal[0] = 0;
			bearIfvgSignal[0] = 0;
			bullBprSignal[0] = 0;
			bearBprSignal[0] = 0;
			bullUnicornSignal[0] = 0;
			bearUnicornSignal[0] = 0;
			fvgFilledSignal[0] = 0;
			nearestBullTop[0] = double.NaN;
			nearestBullBottom[0] = double.NaN;
			nearestBearTop[0] = double.NaN;
			nearestBearBottom[0] = double.NaN;
			nearestBullIfvgTop[0] = double.NaN;
			nearestBullIfvgBottom[0] = double.NaN;
			nearestBearIfvgTop[0] = double.NaN;
			nearestBearIfvgBottom[0] = double.NaN;
			nearestBullBprTop[0] = double.NaN;
			nearestBullBprBottom[0] = double.NaN;
			nearestBearBprTop[0] = double.NaN;
			nearestBearBprBottom[0] = double.NaN;
			nearestUnicornTop[0] = double.NaN;
			nearestUnicornBottom[0] = double.NaN;
		}

		private void DetectNewFvg()
		{
			if (!(ShowFVG || ShowIFVG || ShowBPR || ShowUnicorn) || !IsInTimeRange() || !HasImpulse())
				return;

			int daysToAdd = (Times[dataSeriesIdx][0].DayOfWeek == DayOfWeek.Friday && MaxDaysForward > 0) ? MaxDaysForward + 2 : MaxDaysForward;
			future = sessionEnd == DateTime.MinValue ? Times[dataSeriesIdx][0].AddDays(daysToAdd) : sessionEnd.AddDays(daysToAdd);

			bool bullBarsOk = !AllBarsSameDirection || (Closes[dataSeriesIdx][2] > Opens[dataSeriesIdx][2] && Closes[dataSeriesIdx][1] > Opens[dataSeriesIdx][1] && Closes[dataSeriesIdx][0] > Opens[dataSeriesIdx][0]);
			bool bearBarsOk = !AllBarsSameDirection || (Closes[dataSeriesIdx][2] < Opens[dataSeriesIdx][2] && Closes[dataSeriesIdx][1] < Opens[dataSeriesIdx][1] && Closes[dataSeriesIdx][0] < Opens[dataSeriesIdx][0]);

			if (bullBarsOk && Lows[dataSeriesIdx][0] > Highs[dataSeriesIdx][2] && IsLargeEnough(Lows[dataSeriesIdx][0], Highs[dataSeriesIdx][2]))
			{
				PdaZone zone = NewZone(PdaZoneKind.FVG, 1, Highs[dataSeriesIdx][2], Lows[dataSeriesIdx][0], Times[dataSeriesIdx][2], future);
				zone = AddOrMergeFvg(zone);
				bullFvgSignal[0] = Closes[dataSeriesIdx][0] > Highs[dataSeriesIdx][1] ? 2 : 1;
				if (ShowFVG) DrawZone(zone);
				DetectBpr(zone);
			}

			if (bearBarsOk && Highs[dataSeriesIdx][0] < Lows[dataSeriesIdx][2] && IsLargeEnough(Lows[dataSeriesIdx][2], Highs[dataSeriesIdx][0]))
			{
				PdaZone zone = NewZone(PdaZoneKind.FVG, -1, Highs[dataSeriesIdx][0], Lows[dataSeriesIdx][2], Times[dataSeriesIdx][2], future);
				zone = AddOrMergeFvg(zone);
				bearFvgSignal[0] = Closes[dataSeriesIdx][0] < Lows[dataSeriesIdx][1] ? -2 : -1;
				if (ShowFVG) DrawZone(zone);
				DetectBpr(zone);
			}
		}

		private void UpdateExistingZones()
		{
			CheckFvgFillOrInvert();
			ExtendOpenZones(ifvgList);
			ExtendOpenZones(bprList);
			ExtendOpenZones(unicornList);
		}

		private void CheckFvgFillOrInvert()
		{
			List<PdaZone> completed = new List<PdaZone>();

			for (int i = 0; i < fvgList.Count; i++)
			{
				PdaZone fvg = fvgList[i];
				if (fvg.IsFilled || fvg.IsExpired)
					continue;

				if (Times[dataSeriesIdx][0] > fvg.EndDateTime)
				{
					fvg.IsExpired = true;
					fvg.FilledDateTime = fvg.EndDateTime;
					completed.Add(fvg);
				}
				else if (IsZoneFilled(fvg))
				{
					fvg.IsFilled = true;
					fvg.FilledDateTime = Times[dataSeriesIdx][0];
					fvgFilledSignal[0] = fvg.Side;
					completed.Add(fvg);

					if (ShowIFVG && IsInvertedByClose(fvg))
						CreateIfvg(fvg);
				}
				else
					ExtendZone(fvg);
			}

			for (int i = 0; i < completed.Count; i++)
				HandleCompletedFvg(completed[i]);
		}

		private bool IsZoneFilled(PdaZone fvg)
		{
			if (fvg.Side > 0)
				return FillType == PDAFillType.CLOSE_THROUGH ? Closes[dataSeriesIdx][0] <= fvg.LowerPrice : Lows[dataSeriesIdx][0] <= fvg.LowerPrice;
			return FillType == PDAFillType.CLOSE_THROUGH ? Closes[dataSeriesIdx][0] >= fvg.UpperPrice : Highs[dataSeriesIdx][0] >= fvg.UpperPrice;
		}

		private bool IsInvertedByClose(PdaZone fvg)
		{
			return (fvg.Side > 0 && Closes[dataSeriesIdx][0] <= fvg.LowerPrice) || (fvg.Side < 0 && Closes[dataSeriesIdx][0] >= fvg.UpperPrice);
		}

		private void CreateIfvg(PdaZone source)
		{
			PdaZone ifvg = NewZone(PdaZoneKind.IFVG, -source.Side, source.LowerPrice, source.UpperPrice, Times[dataSeriesIdx][0], future);
			ifvg.SourceTag = source.Tag;
			AddZone(ifvgList, ifvg);
			DrawZone(ifvg);
			if (ifvg.Side > 0) bullIfvgSignal[0] = 1;
			else bearIfvgSignal[0] = -1;
			DetectUnicornFromIfvg(ifvg);
		}

		private void HandleCompletedFvg(PdaZone fvg)
		{
			RemoveZoneDrawObjects(fvg);

			if (ShowFVG && !HideFilledGaps && fvg.IsFilled)
			{
				Brush filledBrush = fvg.Side > 0 ? BullFvgFilledBrush : BearFvgFilledBrush;
				Draw.Rectangle(this, "FILLED_" + fvg.Tag, false, fvg.StartDateTime, fvg.LowerPrice, fvg.FilledDateTime, fvg.UpperPrice, filledBrush, filledBrush, FilledAreaOpacity, true);
			}

			if ((HideFilledGaps && !IncludeFilledFvgForBPR) || fvg.IsExpired)
				fvgList.Remove(fvg);
		}

		private void ExtendOpenZones(List<PdaZone> zones)
		{
			for (int i = zones.Count - 1; i >= 0; i--)
			{
				PdaZone zone = zones[i];
				if (Times[dataSeriesIdx][0] > zone.EndDateTime)
				{
					RemoveZoneDrawObjects(zone);
					zones.RemoveAt(i);
					continue;
				}
				ExtendZone(zone);
			}
		}

		private void ExtendZone(PdaZone zone)
		{
			DrawZone(zone);
		}

		private PdaZone NewZone(PdaZoneKind kind, int side, double lower, double upper, DateTime start, DateTime end)
		{
			double lowerPrice = Math.Min(lower, upper);
			double upperPrice = Math.Max(lower, upper);
			return new PdaZone {
				Tag = kind.ToString() + "_" + (side > 0 ? "UP_" : "DOWN_") + instanceId + "_" + CurrentBars[dataSeriesIdx],
				Kind = kind,
				Side = side,
				LowerPrice = lowerPrice,
				UpperPrice = upperPrice,
				ConsequentEncroachmentPrice = (lowerPrice + upperPrice) / 2.0,
				StartDateTime = start,
				EndDateTime = end,
				FormedBar = CurrentBars[dataSeriesIdx]
			};
		}

		private PdaZone AddOrMergeFvg(PdaZone zone)
		{
			if (!MergeSameSideFVG)
				return AddZone(fvgList, zone);

			for (int i = 0; i < fvgList.Count; i++)
			{
				PdaZone existing = fvgList[i];
				if (existing.IsFilled || existing.IsExpired || existing.Side != zone.Side)
					continue;
				if (!ShouldMerge(existing, zone))
					continue;

				existing.LowerPrice = Math.Min(existing.LowerPrice, zone.LowerPrice);
				existing.UpperPrice = Math.Max(existing.UpperPrice, zone.UpperPrice);
				existing.ConsequentEncroachmentPrice = (existing.LowerPrice + existing.UpperPrice) / 2.0;
				if (zone.StartDateTime < existing.StartDateTime) existing.StartDateTime = zone.StartDateTime;
				existing.EndDateTime = zone.EndDateTime;
				fvgList.RemoveAt(i);
				fvgList.Insert(0, existing);
				return existing;
			}

			return AddZone(fvgList, zone);
		}

		private PdaZone AddZone(List<PdaZone> list, PdaZone zone)
		{
			list.Insert(0, zone);
			while (list.Count > Math.Max(1, MaxActiveZones))
			{
				RemoveZoneDrawObjects(list[list.Count - 1]);
				list.RemoveAt(list.Count - 1);
			}
			return zone;
		}

		private bool ShouldMerge(PdaZone a, PdaZone b)
		{
			double overlap = Math.Min(a.UpperPrice, b.UpperPrice) - Math.Max(a.LowerPrice, b.LowerPrice);
			if (overlap <= 0) return false;
			double minSize = Math.Min(Math.Max(TickSize, a.UpperPrice - a.LowerPrice), Math.Max(TickSize, b.UpperPrice - b.LowerPrice));
			return overlap / minSize >= FvgMergeOverlapPct;
		}

		private void DetectBpr(PdaZone newest)
		{
			if (!ShowBPR || newest == null)
				return;

			for (int i = 0; i < fvgList.Count; i++)
			{
				PdaZone other = fvgList[i];
				if (other == newest || other.IsExpired || other.Side == newest.Side)
					continue;
				if (other.IsFilled && !IncludeFilledFvgForBPR)
					continue;
				if (CurrentBars[dataSeriesIdx] - other.FormedBar > BprBarsSince)
					continue;

				double lower = Math.Max(newest.LowerPrice, other.LowerPrice);
				double upper = Math.Min(newest.UpperPrice, other.UpperPrice);
				if (upper <= lower || upper - lower < BprMinOverlap)
					continue;

				PdaZone bpr = NewZone(PdaZoneKind.BPR, newest.Side, lower, upper, Times[dataSeriesIdx][0], future);
				AddZone(bprList, bpr);
				DrawZone(bpr);
				if (bpr.Side > 0) bullBprSignal[0] = 1;
				else bearBprSignal[0] = -1;
				DetectUnicornFromBpr(bpr);
				break;
			}
		}

		private void DetectUnicornFromIfvg(PdaZone ifvg)
		{
			if (!ShowUnicorn || ifvg == null)
				return;
			for (int i = 0; i < bprList.Count; i++)
			{
				PdaZone bpr = bprList[i];
				if (bpr.IsExpired || bpr.Side != ifvg.Side)
					continue;
				if (CreateUnicornIfOverlap(ifvg, bpr))
					break;
			}
		}

		private void DetectUnicornFromBpr(PdaZone bpr)
		{
			if (!ShowUnicorn || bpr == null)
				return;
			for (int i = 0; i < ifvgList.Count; i++)
			{
				PdaZone ifvg = ifvgList[i];
				if (ifvg.IsExpired || ifvg.Side != bpr.Side)
					continue;
				if (CreateUnicornIfOverlap(ifvg, bpr))
					break;
			}
		}

		private bool CreateUnicornIfOverlap(PdaZone ifvg, PdaZone bpr)
		{
			double lower = Math.Max(ifvg.LowerPrice, bpr.LowerPrice);
			double upper = Math.Min(ifvg.UpperPrice, bpr.UpperPrice);
			if (upper <= lower || upper - lower < UnicornMinOverlap)
				return false;

			for (int i = 0; i < unicornList.Count; i++)
			{
				PdaZone existing = unicornList[i];
				if (existing.IsExpired || existing.Side != ifvg.Side)
					continue;
				if (Math.Abs(existing.LowerPrice - lower) <= TickSize && Math.Abs(existing.UpperPrice - upper) <= TickSize)
					return true;
			}

			PdaZone unicorn = NewZone(PdaZoneKind.UNICORN, ifvg.Side, lower, upper, Times[dataSeriesIdx][0], future);
			unicorn.SourceTag = ifvg.Tag + "|" + bpr.Tag;
			AddZone(unicornList, unicorn);
			DrawZone(unicorn);
			if (unicorn.Side > 0) bullUnicornSignal[0] = 1;
			else bearUnicornSignal[0] = -1;
			return true;
		}

		private bool IsInTimeRange()
		{
			bool any = UseTimeRange1 || UseTimeRange2 || UseTimeRange3;
			if (!any) return true;

			TimeSpan t0 = Times[dataSeriesIdx][0].TimeOfDay;
			TimeSpan t1 = Times[dataSeriesIdx][1].TimeOfDay;
			return (UseTimeRange1 && t1 >= StartTime1 && t0 <= endTime1)
				|| (UseTimeRange2 && t1 >= StartTime2 && t0 <= endTime2)
				|| (UseTimeRange3 && t1 >= StartTime3 && t0 <= endTime3);
		}

		private bool HasImpulse()
		{
			return !UseATR || Math.Abs(Highs[dataSeriesIdx][1] - Lows[dataSeriesIdx][1]) >= ImpulseFactor * AverageRange(ATRPeriod);
		}

		private double AverageRange(int period)
		{
			double sum = 0;
			int count = Math.Min(CurrentBars[dataSeriesIdx] + 1, Math.Max(1, period));
			for (int i = 0; i < count; i++)
				sum += Highs[dataSeriesIdx][i] - Lows[dataSeriesIdx][i];
			return count > 0 ? sum / count : 0;
		}

		private bool IsLargeEnough(double a, double b)
		{
			return Math.Abs(a - b) >= MinimumFVGSize;
		}

		private void DrawZone(PdaZone zone)
		{
			if (zone == null) return;
			if (zone.Kind == PdaZoneKind.FVG && !ShowFVG) return;
			if (zone.Kind == PdaZoneKind.IFVG && !ShowIFVG) return;
			if (zone.Kind == PdaZoneKind.BPR && !ShowBPR) return;
			if (zone.Kind == PdaZoneKind.UNICORN && !ShowUnicorn) return;

			Brush outline = GetOutlineBrush(zone);
			Brush area = GetAreaBrush(zone);
			int opacity = GetOpacity(zone);
			Draw.Rectangle(this, zone.Tag, false, zone.StartDateTime, zone.LowerPrice, zone.EndDateTime, zone.UpperPrice, outline, area, opacity, true);

			if (DisplayCE)
				Draw.Line(this, zone.Tag + CeTag, false, zone.StartDateTime, zone.ConsequentEncroachmentPrice, zone.EndDateTime, zone.ConsequentEncroachmentPrice, outline, DashStyleHelper.Dash, 1);
		}

		private Brush GetOutlineBrush(PdaZone zone)
		{
			if (zone.Kind == PdaZoneKind.BPR) return BprBrush;
			if (zone.Kind == PdaZoneKind.UNICORN) return UnicornBrush;
			if (zone.Kind == PdaZoneKind.IFVG) return zone.Side > 0 ? BullIfvgBrush : BearIfvgBrush;
			return zone.Side > 0 ? BullFvgBrush : BearFvgBrush;
		}

		private Brush GetAreaBrush(PdaZone zone)
		{
			if (zone.Kind == PdaZoneKind.BPR) return BprBrush;
			if (zone.Kind == PdaZoneKind.UNICORN) return UnicornBrush;
			if (zone.Kind == PdaZoneKind.IFVG) return zone.Side > 0 ? BullIfvgBrush : BearIfvgBrush;
			return zone.Side > 0 ? BullFvgAreaBrush : BearFvgAreaBrush;
		}

		private int GetOpacity(PdaZone zone)
		{
			if (zone.Kind == PdaZoneKind.BPR) return BprAreaOpacity;
			if (zone.Kind == PdaZoneKind.UNICORN) return UnicornAreaOpacity;
			if (zone.Kind == PdaZoneKind.IFVG) return IfvgAreaOpacity;
			return ActiveAreaOpacity;
		}

		private void RemoveZoneDrawObjects(PdaZone zone)
		{
			if (zone == null || string.IsNullOrEmpty(zone.Tag)) return;
			RemoveDrawObject(zone.Tag);
			RemoveDrawObject(zone.Tag + CeTag);
		}

		private void PublishNearestFvg()
		{
			PublishNearestFromList(fvgList, nearestBullTop, nearestBullBottom, nearestBearTop, nearestBearBottom, true);
			PublishNearestFromList(ifvgList, nearestBullIfvgTop, nearestBullIfvgBottom, nearestBearIfvgTop, nearestBearIfvgBottom, false);
			PublishNearestFromList(bprList, nearestBullBprTop, nearestBullBprBottom, nearestBearBprTop, nearestBearBprBottom, false);
			PublishNearestUnicorn();
		}

		private void PublishNearestFromList(List<PdaZone> source, Series<double> bullTop, Series<double> bullBottom, Series<double> bearTop, Series<double> bearBottom, bool skipFilled)
		{
			PdaZone bull = null;
			PdaZone bear = null;
			double bullDistance = double.MaxValue;
			double bearDistance = double.MaxValue;

			for (int i = 0; i < source.Count; i++)
			{
				PdaZone zone = source[i];
				if (zone.IsExpired || (skipFilled && zone.IsFilled))
					continue;
				double distance = Closes[dataSeriesIdx][0] > zone.UpperPrice ? Closes[dataSeriesIdx][0] - zone.UpperPrice : (Closes[dataSeriesIdx][0] < zone.LowerPrice ? zone.LowerPrice - Closes[dataSeriesIdx][0] : 0);
				if (zone.Side > 0 && distance < bullDistance)
				{
					bullDistance = distance;
					bull = zone;
				}
				else if (zone.Side < 0 && distance < bearDistance)
				{
					bearDistance = distance;
					bear = zone;
				}
			}

			if (bull != null)
			{
				bullTop[0] = bull.UpperPrice;
				bullBottom[0] = bull.LowerPrice;
			}
			if (bear != null)
			{
				bearTop[0] = bear.UpperPrice;
				bearBottom[0] = bear.LowerPrice;
			}
		}

		private void PublishNearestUnicorn()
		{
			PdaZone best = null;
			double bestDistance = double.MaxValue;
			for (int i = 0; i < unicornList.Count; i++)
			{
				PdaZone zone = unicornList[i];
				if (zone.IsExpired)
					continue;
				double distance = Closes[dataSeriesIdx][0] > zone.UpperPrice ? Closes[dataSeriesIdx][0] - zone.UpperPrice : (Closes[dataSeriesIdx][0] < zone.LowerPrice ? zone.LowerPrice - Closes[dataSeriesIdx][0] : 0);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = zone;
				}
			}
			if (best != null)
			{
				nearestUnicornTop[0] = best.UpperPrice;
				nearestUnicornBottom[0] = best.LowerPrice;
			}
		}

		private string DataSeriesStatusText()
		{
			return dataSeriesLabel
				+ " | FVG " + CountActive(fvgList).ToString()
				+ " | IFVG " + CountActive(ifvgList).ToString()
				+ " | BPR " + CountActive(bprList).ToString()
				+ " | Unicorn " + CountActive(unicornList).ToString();
		}

		private int CountActive(List<PdaZone> zones)
		{
			int count = 0;
			for (int i = 0; i < zones.Count; i++)
				if (!zones[i].IsExpired)
					count++;
			return count;
		}

		private void FreezeBrushes()
		{
			FreezeBrush(BullFvgBrush);
			FreezeBrush(BullFvgAreaBrush);
			FreezeBrush(BullFvgFilledBrush);
			FreezeBrush(BearFvgBrush);
			FreezeBrush(BearFvgAreaBrush);
			FreezeBrush(BearFvgFilledBrush);
			FreezeBrush(BullIfvgBrush);
			FreezeBrush(BearIfvgBrush);
			FreezeBrush(BprBrush);
			FreezeBrush(UnicornBrush);
			FreezeBrush(LabelTextBrush);
			FreezeBrush(LabelBorderBrush);
			FreezeBrush(LabelFillBrush);
		}

		private void FreezeBrush(Brush brush)
		{
			if (brush != null && brush.CanFreeze && !brush.IsFrozen)
				brush.Freeze();
		}

		#region Properties
		#region PDA Data Series
		[NinjaScriptProperty]
		[Display(Name = "Use PDA Data Series", Order = 90, GroupName = "PDA Data Series")]
		public bool UsePDADataSeries { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "PDA Data Series Type", Order = 100, GroupName = "PDA Data Series")]
		public PDAPeriodTypes PDABarsPeriodType { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "PDA Data Series Period", Order = 200, GroupName = "PDA Data Series")]
		public int PDASeriesPeriod { get; set; }
		#endregion

		#region Parameters
		[NinjaScriptProperty]
		[Display(Name = "Show FVG", Order = 10, GroupName = "Parameters")]
		public bool ShowFVG { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show IFVG", Order = 20, GroupName = "Parameters")]
		public bool ShowIFVG { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show BPR", Order = 30, GroupName = "Parameters")]
		public bool ShowBPR { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Unicorn Candidates", Order = 40, GroupName = "Parameters")]
		public bool ShowUnicorn { get; set; }

		[NinjaScriptProperty]
		[Range(0, 7300)]
		[Display(Name = "Max Days to extend forward", Order = 100, GroupName = "Parameters")]
		public int MaxDaysForward { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Max Active Zones", Order = 110, GroupName = "Parameters")]
		public int MaxActiveZones { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require Impulse Move", Order = 190, GroupName = "Parameters")]
		public bool UseATR { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Min. ATR Impulse Move", Order = 200, GroupName = "Parameters")]
		public double ImpulseFactor { get; set; }

		[NinjaScriptProperty]
		[Range(3, int.MaxValue)]
		[Display(Name = "ATR Period", Order = 300, GroupName = "Parameters")]
		public int ATRPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.000000001, double.MaxValue)]
		[Display(Name = "Min. FV Gap Size (Points)", Order = 310, GroupName = "Parameters")]
		public double MinimumFVGSize { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require FVG bars in same direction", Order = 320, GroupName = "Parameters")]
		public bool AllBarsSameDirection { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG Fill Condition", Order = 325, GroupName = "Parameters")]
		public PDAFillType FillType { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Hide Filled FVG", Order = 350, GroupName = "Parameters")]
		public bool HideFilledGaps { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Consequent Encroachment", Order = 400, GroupName = "Parameters")]
		public bool DisplayCE { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Merge Same Side FVG", Order = 500, GroupName = "Parameters")]
		public bool MergeSameSideFVG { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 1.0)]
		[Display(Name = "FVG Merge Overlap Pct", Order = 510, GroupName = "Parameters")]
		public double FvgMergeOverlapPct { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Include Filled FVG For BPR", Order = 590, GroupName = "Parameters")]
		public bool IncludeFilledFvgForBPR { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "BPR Bars Since", Order = 600, GroupName = "Parameters")]
		public int BprBarsSince { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "BPR Min Overlap", Order = 610, GroupName = "Parameters")]
		public double BprMinOverlap { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "Unicorn Min Overlap", Order = 700, GroupName = "Parameters")]
		public double UnicornMinOverlap { get; set; }
		#endregion

		#region Time Ranges
		[NinjaScriptProperty]
		[Display(Name = "Restrict to Time Range #1", Order = 0, GroupName = "Time Ranges")]
		public bool UseTimeRange1 { get; set; }

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Start Time #1", Order = 2, GroupName = "Time Ranges")]
		public TimeSpan StartTime1 { get; set; }

		[Browsable(false)]
		public string StartTime1Serializable { get { return StartTime1.ToString(); } set { StartTime1 = TimeSpan.Parse(value); } }

		[NinjaScriptProperty]
		[Range(1, 1439)]
		[Display(Name = "Time Range #1 Minutes", Order = 4, GroupName = "Time Ranges")]
		public int TimeRangeMinutes1 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Restrict to Time Range #2", Order = 10, GroupName = "Time Ranges")]
		public bool UseTimeRange2 { get; set; }

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Start Time #2", Order = 12, GroupName = "Time Ranges")]
		public TimeSpan StartTime2 { get; set; }

		[Browsable(false)]
		public string StartTime2Serializable { get { return StartTime2.ToString(); } set { StartTime2 = TimeSpan.Parse(value); } }

		[NinjaScriptProperty]
		[Range(1, 1439)]
		[Display(Name = "Time Range #2 Minutes", Order = 14, GroupName = "Time Ranges")]
		public int TimeRangeMinutes2 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Restrict to Time Range #3", Order = 20, GroupName = "Time Ranges")]
		public bool UseTimeRange3 { get; set; }

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Start Time #3", Order = 22, GroupName = "Time Ranges")]
		public TimeSpan StartTime3 { get; set; }

		[Browsable(false)]
		public string StartTime3Serializable { get { return StartTime3.ToString(); } set { StartTime3 = TimeSpan.Parse(value); } }

		[NinjaScriptProperty]
		[Range(1, 1439)]
		[Display(Name = "Time Range #3 Minutes", Order = 24, GroupName = "Time Ranges")]
		public int TimeRangeMinutes3 { get; set; }
		#endregion

		#region PDA Colors
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bullish FVG Border Color", Order = 0, GroupName = "PDA Colors")]
		public Brush BullFvgBrush { get; set; }
		[Browsable(false)] public string BullFvgBrushSerializable { get { return Serialize.BrushToString(BullFvgBrush); } set { BullFvgBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bullish FVG Area Color", Order = 2, GroupName = "PDA Colors")]
		public Brush BullFvgAreaBrush { get; set; }
		[Browsable(false)] public string BullFvgAreaBrushSerializable { get { return Serialize.BrushToString(BullFvgAreaBrush); } set { BullFvgAreaBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bullish FVG Filled Color", Order = 4, GroupName = "PDA Colors")]
		public Brush BullFvgFilledBrush { get; set; }
		[Browsable(false)] public string BullFvgFilledBrushSerializable { get { return Serialize.BrushToString(BullFvgFilledBrush); } set { BullFvgFilledBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bearish FVG Border Color", Order = 6, GroupName = "PDA Colors")]
		public Brush BearFvgBrush { get; set; }
		[Browsable(false)] public string BearFvgBrushSerializable { get { return Serialize.BrushToString(BearFvgBrush); } set { BearFvgBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bearish FVG Area Color", Order = 8, GroupName = "PDA Colors")]
		public Brush BearFvgAreaBrush { get; set; }
		[Browsable(false)] public string BearFvgAreaBrushSerializable { get { return Serialize.BrushToString(BearFvgAreaBrush); } set { BearFvgAreaBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bearish FVG Filled Color", Order = 10, GroupName = "PDA Colors")]
		public Brush BearFvgFilledBrush { get; set; }
		[Browsable(false)] public string BearFvgFilledBrushSerializable { get { return Serialize.BrushToString(BearFvgFilledBrush); } set { BearFvgFilledBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bullish IFVG Color", Order = 12, GroupName = "PDA Colors")]
		public Brush BullIfvgBrush { get; set; }
		[Browsable(false)] public string BullIfvgBrushSerializable { get { return Serialize.BrushToString(BullIfvgBrush); } set { BullIfvgBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bearish IFVG Color", Order = 14, GroupName = "PDA Colors")]
		public Brush BearIfvgBrush { get; set; }
		[Browsable(false)] public string BearIfvgBrushSerializable { get { return Serialize.BrushToString(BearIfvgBrush); } set { BearIfvgBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "BPR Color", Order = 16, GroupName = "PDA Colors")]
		public Brush BprBrush { get; set; }
		[Browsable(false)] public string BprBrushSerializable { get { return Serialize.BrushToString(BprBrush); } set { BprBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Unicorn Color", Order = 18, GroupName = "PDA Colors")]
		public Brush UnicornBrush { get; set; }
		[Browsable(false)] public string UnicornBrushSerializable { get { return Serialize.BrushToString(UnicornBrush); } set { UnicornBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Active Gap Opacity", Order = 20, GroupName = "PDA Colors")]
		public int ActiveAreaOpacity { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Filled Gap Opacity", Order = 22, GroupName = "PDA Colors")]
		public int FilledAreaOpacity { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "IFVG Opacity", Order = 24, GroupName = "PDA Colors")]
		public int IfvgAreaOpacity { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "BPR Opacity", Order = 26, GroupName = "PDA Colors")]
		public int BprAreaOpacity { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Unicorn Opacity", Order = 28, GroupName = "PDA Colors")]
		public int UnicornAreaOpacity { get; set; }
		#endregion

		#region PDA Data Series Label
		[NinjaScriptProperty]
		[Display(Name = "Display Label", Order = 100, GroupName = "PDA Data Series Label")]
		public bool DrawLabel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label Position", Order = 200, GroupName = "PDA Data Series Label")]
		public TextPosition LabelPosition { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label Font", Order = 300, GroupName = "PDA Data Series Label")]
		public SimpleFont LabelFont { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Label Text Color", Order = 400, GroupName = "PDA Data Series Label")]
		public Brush LabelTextBrush { get; set; }
		[Browsable(false)] public string LabelTextBrushSerializable { get { return Serialize.BrushToString(LabelTextBrush); } set { LabelTextBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Label Border Color", Order = 500, GroupName = "PDA Data Series Label")]
		public Brush LabelBorderBrush { get; set; }
		[Browsable(false)] public string LabelBorderBrushSerializable { get { return Serialize.BrushToString(LabelBorderBrush); } set { LabelBorderBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Label Fill Color", Order = 600, GroupName = "PDA Data Series Label")]
		public Brush LabelFillBrush { get; set; }
		[Browsable(false)] public string LabelFillBrushSerializable { get { return Serialize.BrushToString(LabelFillBrush); } set { LabelFillBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Label Fill Opacity", Order = 700, GroupName = "PDA Data Series Label")]
		public int LabelFillOpacity { get; set; }
		#endregion

		[Browsable(false)]
		[XmlIgnore]
		public List<PdaZone> FVGList { get { return new List<PdaZone>(fvgList); } }

		[Browsable(false)]
		[XmlIgnore]
		public List<PdaZone> IFVGList { get { return new List<PdaZone>(ifvgList); } }

		[Browsable(false)]
		[XmlIgnore]
		public List<PdaZone> BPRList { get { return new List<PdaZone>(bprList); } }

		[Browsable(false)]
		[XmlIgnore]
		public List<PdaZone> UnicornList { get { return new List<PdaZone>(unicornList); } }

		[Browsable(false)][XmlIgnore] public Series<double> BullFvgSignal { get { return bullFvgSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BearFvgSignal { get { return bearFvgSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BullIfvgSignal { get { return bullIfvgSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BearIfvgSignal { get { return bearIfvgSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BullBprSignal { get { return bullBprSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BearBprSignal { get { return bearBprSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BullUnicornSignal { get { return bullUnicornSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> BearUnicornSignal { get { return bearUnicornSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> FvgFilledSignal { get { return fvgFilledSignal; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBullFvgTop { get { return nearestBullTop; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBullFvgBottom { get { return nearestBullBottom; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBearFvgTop { get { return nearestBearTop; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBearFvgBottom { get { return nearestBearBottom; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBullIfvgTop { get { return nearestBullIfvgTop; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBullIfvgBottom { get { return nearestBullIfvgBottom; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBearIfvgTop { get { return nearestBearIfvgTop; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBearIfvgBottom { get { return nearestBearIfvgBottom; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBullBprTop { get { return nearestBullBprTop; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBullBprBottom { get { return nearestBullBprBottom; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBearBprTop { get { return nearestBearBprTop; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestBearBprBottom { get { return nearestBearBprBottom; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestUnicornTop { get { return nearestUnicornTop; } }
		[Browsable(false)][XmlIgnore] public Series<double> NearestUnicornBottom { get { return nearestUnicornBottom; } }
		#endregion

		public class PdaZone
		{
			public string Tag;
			public string SourceTag;
			public PdaZoneKind Kind;
			public int Side;
			public double UpperPrice;
			public double ConsequentEncroachmentPrice;
			public double LowerPrice;
			public DateTime StartDateTime;
			public DateTime EndDateTime;
			public int FormedBar;
			public bool IsFilled;
			public DateTime FilledDateTime;
			public bool IsExpired;
		}
	}
}

public enum PDAFillType
{
	CLOSE_THROUGH,
	PIERCE_THROUGH
}

public enum PDAPeriodTypes
{
	Tick = BarsPeriodType.Tick,
	Volume = BarsPeriodType.Volume,
	Second = BarsPeriodType.Second,
	Minute = BarsPeriodType.Minute,
	Day = BarsPeriodType.Day,
	Week = BarsPeriodType.Week,
	Month = BarsPeriodType.Month,
	Year = BarsPeriodType.Year
}

public enum PdaZoneKind
{
	FVG,
	IFVG,
	BPR,
	UNICORN
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ICT_PDA_Suite[] cacheICT_PDA_Suite;
		public ICT_PDA_Suite ICT_PDA_Suite(bool usePDADataSeries, PDAPeriodTypes pDABarsPeriodType, int pDASeriesPeriod, bool showFVG, bool showIFVG, bool showBPR, bool showUnicorn, int maxDaysForward, int maxActiveZones, bool useATR, double impulseFactor, int aTRPeriod, double minimumFVGSize, bool allBarsSameDirection, PDAFillType fillType, bool hideFilledGaps, bool displayCE, bool mergeSameSideFVG, double fvgMergeOverlapPct, bool includeFilledFvgForBPR, int bprBarsSince, double bprMinOverlap, double unicornMinOverlap, bool useTimeRange1, TimeSpan startTime1, int timeRangeMinutes1, bool useTimeRange2, TimeSpan startTime2, int timeRangeMinutes2, bool useTimeRange3, TimeSpan startTime3, int timeRangeMinutes3, Brush bullFvgBrush, Brush bullFvgAreaBrush, Brush bullFvgFilledBrush, Brush bearFvgBrush, Brush bearFvgAreaBrush, Brush bearFvgFilledBrush, Brush bullIfvgBrush, Brush bearIfvgBrush, Brush bprBrush, Brush unicornBrush, int activeAreaOpacity, int filledAreaOpacity, int ifvgAreaOpacity, int bprAreaOpacity, int unicornAreaOpacity, bool drawLabel, TextPosition labelPosition, SimpleFont labelFont, Brush labelTextBrush, Brush labelBorderBrush, Brush labelFillBrush, int labelFillOpacity)
		{
			return ICT_PDA_Suite(Input, usePDADataSeries, pDABarsPeriodType, pDASeriesPeriod, showFVG, showIFVG, showBPR, showUnicorn, maxDaysForward, maxActiveZones, useATR, impulseFactor, aTRPeriod, minimumFVGSize, allBarsSameDirection, fillType, hideFilledGaps, displayCE, mergeSameSideFVG, fvgMergeOverlapPct, includeFilledFvgForBPR, bprBarsSince, bprMinOverlap, unicornMinOverlap, useTimeRange1, startTime1, timeRangeMinutes1, useTimeRange2, startTime2, timeRangeMinutes2, useTimeRange3, startTime3, timeRangeMinutes3, bullFvgBrush, bullFvgAreaBrush, bullFvgFilledBrush, bearFvgBrush, bearFvgAreaBrush, bearFvgFilledBrush, bullIfvgBrush, bearIfvgBrush, bprBrush, unicornBrush, activeAreaOpacity, filledAreaOpacity, ifvgAreaOpacity, bprAreaOpacity, unicornAreaOpacity, drawLabel, labelPosition, labelFont, labelTextBrush, labelBorderBrush, labelFillBrush, labelFillOpacity);
		}

		public ICT_PDA_Suite ICT_PDA_Suite(ISeries<double> input, bool usePDADataSeries, PDAPeriodTypes pDABarsPeriodType, int pDASeriesPeriod, bool showFVG, bool showIFVG, bool showBPR, bool showUnicorn, int maxDaysForward, int maxActiveZones, bool useATR, double impulseFactor, int aTRPeriod, double minimumFVGSize, bool allBarsSameDirection, PDAFillType fillType, bool hideFilledGaps, bool displayCE, bool mergeSameSideFVG, double fvgMergeOverlapPct, bool includeFilledFvgForBPR, int bprBarsSince, double bprMinOverlap, double unicornMinOverlap, bool useTimeRange1, TimeSpan startTime1, int timeRangeMinutes1, bool useTimeRange2, TimeSpan startTime2, int timeRangeMinutes2, bool useTimeRange3, TimeSpan startTime3, int timeRangeMinutes3, Brush bullFvgBrush, Brush bullFvgAreaBrush, Brush bullFvgFilledBrush, Brush bearFvgBrush, Brush bearFvgAreaBrush, Brush bearFvgFilledBrush, Brush bullIfvgBrush, Brush bearIfvgBrush, Brush bprBrush, Brush unicornBrush, int activeAreaOpacity, int filledAreaOpacity, int ifvgAreaOpacity, int bprAreaOpacity, int unicornAreaOpacity, bool drawLabel, TextPosition labelPosition, SimpleFont labelFont, Brush labelTextBrush, Brush labelBorderBrush, Brush labelFillBrush, int labelFillOpacity)
		{
			if (cacheICT_PDA_Suite != null)
				for (int idx = 0; idx < cacheICT_PDA_Suite.Length; idx++)
					if (cacheICT_PDA_Suite[idx] != null && cacheICT_PDA_Suite[idx].UsePDADataSeries == usePDADataSeries && cacheICT_PDA_Suite[idx].PDABarsPeriodType == pDABarsPeriodType && cacheICT_PDA_Suite[idx].PDASeriesPeriod == pDASeriesPeriod && cacheICT_PDA_Suite[idx].ShowFVG == showFVG && cacheICT_PDA_Suite[idx].ShowIFVG == showIFVG && cacheICT_PDA_Suite[idx].ShowBPR == showBPR && cacheICT_PDA_Suite[idx].ShowUnicorn == showUnicorn && cacheICT_PDA_Suite[idx].MaxDaysForward == maxDaysForward && cacheICT_PDA_Suite[idx].MaxActiveZones == maxActiveZones && cacheICT_PDA_Suite[idx].UseATR == useATR && cacheICT_PDA_Suite[idx].ImpulseFactor == impulseFactor && cacheICT_PDA_Suite[idx].ATRPeriod == aTRPeriod && cacheICT_PDA_Suite[idx].MinimumFVGSize == minimumFVGSize && cacheICT_PDA_Suite[idx].AllBarsSameDirection == allBarsSameDirection && cacheICT_PDA_Suite[idx].FillType == fillType && cacheICT_PDA_Suite[idx].HideFilledGaps == hideFilledGaps && cacheICT_PDA_Suite[idx].DisplayCE == displayCE && cacheICT_PDA_Suite[idx].MergeSameSideFVG == mergeSameSideFVG && cacheICT_PDA_Suite[idx].FvgMergeOverlapPct == fvgMergeOverlapPct && cacheICT_PDA_Suite[idx].IncludeFilledFvgForBPR == includeFilledFvgForBPR && cacheICT_PDA_Suite[idx].BprBarsSince == bprBarsSince && cacheICT_PDA_Suite[idx].BprMinOverlap == bprMinOverlap && cacheICT_PDA_Suite[idx].UnicornMinOverlap == unicornMinOverlap && cacheICT_PDA_Suite[idx].UseTimeRange1 == useTimeRange1 && cacheICT_PDA_Suite[idx].StartTime1 == startTime1 && cacheICT_PDA_Suite[idx].TimeRangeMinutes1 == timeRangeMinutes1 && cacheICT_PDA_Suite[idx].UseTimeRange2 == useTimeRange2 && cacheICT_PDA_Suite[idx].StartTime2 == startTime2 && cacheICT_PDA_Suite[idx].TimeRangeMinutes2 == timeRangeMinutes2 && cacheICT_PDA_Suite[idx].UseTimeRange3 == useTimeRange3 && cacheICT_PDA_Suite[idx].StartTime3 == startTime3 && cacheICT_PDA_Suite[idx].TimeRangeMinutes3 == timeRangeMinutes3 && cacheICT_PDA_Suite[idx].BullFvgBrush == bullFvgBrush && cacheICT_PDA_Suite[idx].BullFvgAreaBrush == bullFvgAreaBrush && cacheICT_PDA_Suite[idx].BullFvgFilledBrush == bullFvgFilledBrush && cacheICT_PDA_Suite[idx].BearFvgBrush == bearFvgBrush && cacheICT_PDA_Suite[idx].BearFvgAreaBrush == bearFvgAreaBrush && cacheICT_PDA_Suite[idx].BearFvgFilledBrush == bearFvgFilledBrush && cacheICT_PDA_Suite[idx].BullIfvgBrush == bullIfvgBrush && cacheICT_PDA_Suite[idx].BearIfvgBrush == bearIfvgBrush && cacheICT_PDA_Suite[idx].BprBrush == bprBrush && cacheICT_PDA_Suite[idx].UnicornBrush == unicornBrush && cacheICT_PDA_Suite[idx].ActiveAreaOpacity == activeAreaOpacity && cacheICT_PDA_Suite[idx].FilledAreaOpacity == filledAreaOpacity && cacheICT_PDA_Suite[idx].IfvgAreaOpacity == ifvgAreaOpacity && cacheICT_PDA_Suite[idx].BprAreaOpacity == bprAreaOpacity && cacheICT_PDA_Suite[idx].UnicornAreaOpacity == unicornAreaOpacity && cacheICT_PDA_Suite[idx].DrawLabel == drawLabel && cacheICT_PDA_Suite[idx].LabelPosition == labelPosition && cacheICT_PDA_Suite[idx].LabelFont == labelFont && cacheICT_PDA_Suite[idx].LabelTextBrush == labelTextBrush && cacheICT_PDA_Suite[idx].LabelBorderBrush == labelBorderBrush && cacheICT_PDA_Suite[idx].LabelFillBrush == labelFillBrush && cacheICT_PDA_Suite[idx].LabelFillOpacity == labelFillOpacity && cacheICT_PDA_Suite[idx].EqualsInput(input))
						return cacheICT_PDA_Suite[idx];
			return CacheIndicator<ICT_PDA_Suite>(new ICT_PDA_Suite(){ UsePDADataSeries = usePDADataSeries, PDABarsPeriodType = pDABarsPeriodType, PDASeriesPeriod = pDASeriesPeriod, ShowFVG = showFVG, ShowIFVG = showIFVG, ShowBPR = showBPR, ShowUnicorn = showUnicorn, MaxDaysForward = maxDaysForward, MaxActiveZones = maxActiveZones, UseATR = useATR, ImpulseFactor = impulseFactor, ATRPeriod = aTRPeriod, MinimumFVGSize = minimumFVGSize, AllBarsSameDirection = allBarsSameDirection, FillType = fillType, HideFilledGaps = hideFilledGaps, DisplayCE = displayCE, MergeSameSideFVG = mergeSameSideFVG, FvgMergeOverlapPct = fvgMergeOverlapPct, IncludeFilledFvgForBPR = includeFilledFvgForBPR, BprBarsSince = bprBarsSince, BprMinOverlap = bprMinOverlap, UnicornMinOverlap = unicornMinOverlap, UseTimeRange1 = useTimeRange1, StartTime1 = startTime1, TimeRangeMinutes1 = timeRangeMinutes1, UseTimeRange2 = useTimeRange2, StartTime2 = startTime2, TimeRangeMinutes2 = timeRangeMinutes2, UseTimeRange3 = useTimeRange3, StartTime3 = startTime3, TimeRangeMinutes3 = timeRangeMinutes3, BullFvgBrush = bullFvgBrush, BullFvgAreaBrush = bullFvgAreaBrush, BullFvgFilledBrush = bullFvgFilledBrush, BearFvgBrush = bearFvgBrush, BearFvgAreaBrush = bearFvgAreaBrush, BearFvgFilledBrush = bearFvgFilledBrush, BullIfvgBrush = bullIfvgBrush, BearIfvgBrush = bearIfvgBrush, BprBrush = bprBrush, UnicornBrush = unicornBrush, ActiveAreaOpacity = activeAreaOpacity, FilledAreaOpacity = filledAreaOpacity, IfvgAreaOpacity = ifvgAreaOpacity, BprAreaOpacity = bprAreaOpacity, UnicornAreaOpacity = unicornAreaOpacity, DrawLabel = drawLabel, LabelPosition = labelPosition, LabelFont = labelFont, LabelTextBrush = labelTextBrush, LabelBorderBrush = labelBorderBrush, LabelFillBrush = labelFillBrush, LabelFillOpacity = labelFillOpacity }, input, ref cacheICT_PDA_Suite);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ICT_PDA_Suite ICT_PDA_Suite(bool usePDADataSeries, PDAPeriodTypes pDABarsPeriodType, int pDASeriesPeriod, bool showFVG, bool showIFVG, bool showBPR, bool showUnicorn, int maxDaysForward, int maxActiveZones, bool useATR, double impulseFactor, int aTRPeriod, double minimumFVGSize, bool allBarsSameDirection, PDAFillType fillType, bool hideFilledGaps, bool displayCE, bool mergeSameSideFVG, double fvgMergeOverlapPct, bool includeFilledFvgForBPR, int bprBarsSince, double bprMinOverlap, double unicornMinOverlap, bool useTimeRange1, TimeSpan startTime1, int timeRangeMinutes1, bool useTimeRange2, TimeSpan startTime2, int timeRangeMinutes2, bool useTimeRange3, TimeSpan startTime3, int timeRangeMinutes3, Brush bullFvgBrush, Brush bullFvgAreaBrush, Brush bullFvgFilledBrush, Brush bearFvgBrush, Brush bearFvgAreaBrush, Brush bearFvgFilledBrush, Brush bullIfvgBrush, Brush bearIfvgBrush, Brush bprBrush, Brush unicornBrush, int activeAreaOpacity, int filledAreaOpacity, int ifvgAreaOpacity, int bprAreaOpacity, int unicornAreaOpacity, bool drawLabel, TextPosition labelPosition, SimpleFont labelFont, Brush labelTextBrush, Brush labelBorderBrush, Brush labelFillBrush, int labelFillOpacity)
		{
			return indicator.ICT_PDA_Suite(Input, usePDADataSeries, pDABarsPeriodType, pDASeriesPeriod, showFVG, showIFVG, showBPR, showUnicorn, maxDaysForward, maxActiveZones, useATR, impulseFactor, aTRPeriod, minimumFVGSize, allBarsSameDirection, fillType, hideFilledGaps, displayCE, mergeSameSideFVG, fvgMergeOverlapPct, includeFilledFvgForBPR, bprBarsSince, bprMinOverlap, unicornMinOverlap, useTimeRange1, startTime1, timeRangeMinutes1, useTimeRange2, startTime2, timeRangeMinutes2, useTimeRange3, startTime3, timeRangeMinutes3, bullFvgBrush, bullFvgAreaBrush, bullFvgFilledBrush, bearFvgBrush, bearFvgAreaBrush, bearFvgFilledBrush, bullIfvgBrush, bearIfvgBrush, bprBrush, unicornBrush, activeAreaOpacity, filledAreaOpacity, ifvgAreaOpacity, bprAreaOpacity, unicornAreaOpacity, drawLabel, labelPosition, labelFont, labelTextBrush, labelBorderBrush, labelFillBrush, labelFillOpacity);
		}

		public Indicators.ICT_PDA_Suite ICT_PDA_Suite(ISeries<double> input , bool usePDADataSeries, PDAPeriodTypes pDABarsPeriodType, int pDASeriesPeriod, bool showFVG, bool showIFVG, bool showBPR, bool showUnicorn, int maxDaysForward, int maxActiveZones, bool useATR, double impulseFactor, int aTRPeriod, double minimumFVGSize, bool allBarsSameDirection, PDAFillType fillType, bool hideFilledGaps, bool displayCE, bool mergeSameSideFVG, double fvgMergeOverlapPct, bool includeFilledFvgForBPR, int bprBarsSince, double bprMinOverlap, double unicornMinOverlap, bool useTimeRange1, TimeSpan startTime1, int timeRangeMinutes1, bool useTimeRange2, TimeSpan startTime2, int timeRangeMinutes2, bool useTimeRange3, TimeSpan startTime3, int timeRangeMinutes3, Brush bullFvgBrush, Brush bullFvgAreaBrush, Brush bullFvgFilledBrush, Brush bearFvgBrush, Brush bearFvgAreaBrush, Brush bearFvgFilledBrush, Brush bullIfvgBrush, Brush bearIfvgBrush, Brush bprBrush, Brush unicornBrush, int activeAreaOpacity, int filledAreaOpacity, int ifvgAreaOpacity, int bprAreaOpacity, int unicornAreaOpacity, bool drawLabel, TextPosition labelPosition, SimpleFont labelFont, Brush labelTextBrush, Brush labelBorderBrush, Brush labelFillBrush, int labelFillOpacity)
		{
			return indicator.ICT_PDA_Suite(input, usePDADataSeries, pDABarsPeriodType, pDASeriesPeriod, showFVG, showIFVG, showBPR, showUnicorn, maxDaysForward, maxActiveZones, useATR, impulseFactor, aTRPeriod, minimumFVGSize, allBarsSameDirection, fillType, hideFilledGaps, displayCE, mergeSameSideFVG, fvgMergeOverlapPct, includeFilledFvgForBPR, bprBarsSince, bprMinOverlap, unicornMinOverlap, useTimeRange1, startTime1, timeRangeMinutes1, useTimeRange2, startTime2, timeRangeMinutes2, useTimeRange3, startTime3, timeRangeMinutes3, bullFvgBrush, bullFvgAreaBrush, bullFvgFilledBrush, bearFvgBrush, bearFvgAreaBrush, bearFvgFilledBrush, bullIfvgBrush, bearIfvgBrush, bprBrush, unicornBrush, activeAreaOpacity, filledAreaOpacity, ifvgAreaOpacity, bprAreaOpacity, unicornAreaOpacity, drawLabel, labelPosition, labelFont, labelTextBrush, labelBorderBrush, labelFillBrush, labelFillOpacity);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ICT_PDA_Suite ICT_PDA_Suite(bool usePDADataSeries, PDAPeriodTypes pDABarsPeriodType, int pDASeriesPeriod, bool showFVG, bool showIFVG, bool showBPR, bool showUnicorn, int maxDaysForward, int maxActiveZones, bool useATR, double impulseFactor, int aTRPeriod, double minimumFVGSize, bool allBarsSameDirection, PDAFillType fillType, bool hideFilledGaps, bool displayCE, bool mergeSameSideFVG, double fvgMergeOverlapPct, bool includeFilledFvgForBPR, int bprBarsSince, double bprMinOverlap, double unicornMinOverlap, bool useTimeRange1, TimeSpan startTime1, int timeRangeMinutes1, bool useTimeRange2, TimeSpan startTime2, int timeRangeMinutes2, bool useTimeRange3, TimeSpan startTime3, int timeRangeMinutes3, Brush bullFvgBrush, Brush bullFvgAreaBrush, Brush bullFvgFilledBrush, Brush bearFvgBrush, Brush bearFvgAreaBrush, Brush bearFvgFilledBrush, Brush bullIfvgBrush, Brush bearIfvgBrush, Brush bprBrush, Brush unicornBrush, int activeAreaOpacity, int filledAreaOpacity, int ifvgAreaOpacity, int bprAreaOpacity, int unicornAreaOpacity, bool drawLabel, TextPosition labelPosition, SimpleFont labelFont, Brush labelTextBrush, Brush labelBorderBrush, Brush labelFillBrush, int labelFillOpacity)
		{
			return indicator.ICT_PDA_Suite(Input, usePDADataSeries, pDABarsPeriodType, pDASeriesPeriod, showFVG, showIFVG, showBPR, showUnicorn, maxDaysForward, maxActiveZones, useATR, impulseFactor, aTRPeriod, minimumFVGSize, allBarsSameDirection, fillType, hideFilledGaps, displayCE, mergeSameSideFVG, fvgMergeOverlapPct, includeFilledFvgForBPR, bprBarsSince, bprMinOverlap, unicornMinOverlap, useTimeRange1, startTime1, timeRangeMinutes1, useTimeRange2, startTime2, timeRangeMinutes2, useTimeRange3, startTime3, timeRangeMinutes3, bullFvgBrush, bullFvgAreaBrush, bullFvgFilledBrush, bearFvgBrush, bearFvgAreaBrush, bearFvgFilledBrush, bullIfvgBrush, bearIfvgBrush, bprBrush, unicornBrush, activeAreaOpacity, filledAreaOpacity, ifvgAreaOpacity, bprAreaOpacity, unicornAreaOpacity, drawLabel, labelPosition, labelFont, labelTextBrush, labelBorderBrush, labelFillBrush, labelFillOpacity);
		}

		public Indicators.ICT_PDA_Suite ICT_PDA_Suite(ISeries<double> input , bool usePDADataSeries, PDAPeriodTypes pDABarsPeriodType, int pDASeriesPeriod, bool showFVG, bool showIFVG, bool showBPR, bool showUnicorn, int maxDaysForward, int maxActiveZones, bool useATR, double impulseFactor, int aTRPeriod, double minimumFVGSize, bool allBarsSameDirection, PDAFillType fillType, bool hideFilledGaps, bool displayCE, bool mergeSameSideFVG, double fvgMergeOverlapPct, bool includeFilledFvgForBPR, int bprBarsSince, double bprMinOverlap, double unicornMinOverlap, bool useTimeRange1, TimeSpan startTime1, int timeRangeMinutes1, bool useTimeRange2, TimeSpan startTime2, int timeRangeMinutes2, bool useTimeRange3, TimeSpan startTime3, int timeRangeMinutes3, Brush bullFvgBrush, Brush bullFvgAreaBrush, Brush bullFvgFilledBrush, Brush bearFvgBrush, Brush bearFvgAreaBrush, Brush bearFvgFilledBrush, Brush bullIfvgBrush, Brush bearIfvgBrush, Brush bprBrush, Brush unicornBrush, int activeAreaOpacity, int filledAreaOpacity, int ifvgAreaOpacity, int bprAreaOpacity, int unicornAreaOpacity, bool drawLabel, TextPosition labelPosition, SimpleFont labelFont, Brush labelTextBrush, Brush labelBorderBrush, Brush labelFillBrush, int labelFillOpacity)
		{
			return indicator.ICT_PDA_Suite(input, usePDADataSeries, pDABarsPeriodType, pDASeriesPeriod, showFVG, showIFVG, showBPR, showUnicorn, maxDaysForward, maxActiveZones, useATR, impulseFactor, aTRPeriod, minimumFVGSize, allBarsSameDirection, fillType, hideFilledGaps, displayCE, mergeSameSideFVG, fvgMergeOverlapPct, includeFilledFvgForBPR, bprBarsSince, bprMinOverlap, unicornMinOverlap, useTimeRange1, startTime1, timeRangeMinutes1, useTimeRange2, startTime2, timeRangeMinutes2, useTimeRange3, startTime3, timeRangeMinutes3, bullFvgBrush, bullFvgAreaBrush, bullFvgFilledBrush, bearFvgBrush, bearFvgAreaBrush, bearFvgFilledBrush, bullIfvgBrush, bearIfvgBrush, bprBrush, unicornBrush, activeAreaOpacity, filledAreaOpacity, ifvgAreaOpacity, bprAreaOpacity, unicornAreaOpacity, drawLabel, labelPosition, labelFont, labelTextBrush, labelBorderBrush, labelFillBrush, labelFillOpacity);
		}
	}
}

#endregion
