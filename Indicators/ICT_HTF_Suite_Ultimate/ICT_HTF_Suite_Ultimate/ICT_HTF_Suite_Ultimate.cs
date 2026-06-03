#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript.DrawingTools; 
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public enum ICTKeyLevelKind
	{
		Unknown,
		Open,
		Close,
		High,
		Low,
		GapHigh,
		GapLow,
		GapCE,
		SessionHigh,
		SessionLow
	}

	public class ICTKeyLevelFact
	{
		public string Key;
		public string Source;
		public ICTKeyLevelKind Kind;
		public double Price;
		public DateTime Time;
		public DateTime ValidFrom;
		public DateTime ValidTo;
		public bool IsActive;
		public bool IsFilled;
		public bool IsCurrentSession;
		public string Label;
	}

	public interface IICTKeyLevelFactSource
	{
		List<ICTKeyLevelFact> GetKeyLevelFacts();
		ICTKeyLevelFact GetNearestKeyLevelFactAbove(double price);
		ICTKeyLevelFact GetNearestKeyLevelFactBelow(double price);
	}

	public class ICT_HTF_Suite_Ultimate : Indicator, IICTKeyLevelFactSource
	{
		private class GapZone {
			public string Feature; public double HighPrice; public double LowPrice;
			public DateTime StartTime; public bool IsFilled; 
			public System.Windows.Media.Brush Color;
			public int ExpireDays;
		}

		
		private class TimedLevel {
			public double Price;
			public DateTime Time;
			public TimedLevel(double price, DateTime time) { Price = price; Time = time; }
		}
		private List<GapZone> gaps = new List<GapZone>();
		private Dictionary<string, TimedLevel> levels = new Dictionary<string, TimedLevel>();
		private Dictionary<string, TimedLevel> sessionExtremes = new Dictionary<string, TimedLevel>();
		private Dictionary<string, TimedLevel> previousSessionExtremes = new Dictionary<string, TimedLevel>();
		private TimedLevel currentRthCe = new TimedLevel(0, DateTime.MinValue);
		
		private double pdhRunningHigh = double.MinValue, pdlRunningLow = double.MaxValue;
		private DateTime pdhTime, pdlTime;
		private double po3H, po3L, po3O, po3C;
		private SharpDX.Direct2D1.Brush WickBrushDx, UpFillDx, DownFillDx, TextBrushDx;
		private TextFormat po3Font;
		private int dataSeriesBip = 0;
		private bool useInternalOneMinute = false;

		private bool IsInCurrentTradingSession(DateTime t)
		{
			if (CurrentBar < 0) return false;
			DateTime barZeroTime = Time[0];
			DateTime sessionStart = barZeroTime.Date.AddHours(17);
			if (barZeroTime.TimeOfDay < TimeSpan.FromHours(17)) sessionStart = sessionStart.AddDays(-1);
			DateTime sessionEnd = sessionStart.AddDays(1);
			return t >= sessionStart && t < sessionEnd;
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "ICT_HTF_Suite_Ultimate";
				IsOverlay = true; 
				Calculate = Calculate.OnBarClose; 
				ZOrder = -1;
				IsSuspendedWhileInactive = true;

				// 新增：默认 16:00:00
				RthCloseTime = 160000;

				Days_NDOG = 5; Days_NWOG = 10; Days_RTHGAP = 1;
				Show_DO = Show_WO = Show_NDOG = Show_NWOG = Show_PDH = Show_PDL = Show_PDC = Show_PWH = Show_PWL = Show_PWC = true;
				Show_RTHCLOSE = Show_RTHGAP = Show_RTHMID = Show_RTHOPEN = Show_TDO = true;
				RayDashStyle = DashStyleHelper.Solid; RayWidth = 2; 
				Color_Levels = System.Windows.Media.Brushes.Blue; 
				Color_RTHMID = System.Windows.Media.Brushes.Blue; 
				Color_NDOG = System.Windows.Media.Brushes.DarkBlue; 
				Color_NWOG = System.Windows.Media.Brushes.DarkBlue; 
				Color_RTHGAP = System.Windows.Media.Brushes.Silver; 
				
				Gap_CE_Dash = DashStyleHelper.Dash; Gap_CE_Width = 1;
				NDOG_CE_Dash = DashStyleHelper.Dash; NDOG_CE_Width = 2;
				NWOG_CE_Dash = DashStyleHelper.Dash; NWOG_CE_Width = 2;

				SessionColor = System.Windows.Media.Brushes.DimGray; SessionRayWidth = 1; SessionDashStyle = DashStyleHelper.Dash;
				MyFont = new NinjaTrader.Gui.Tools.SimpleFont("Arial", 11); 
				PO3_DnCol = System.Windows.Media.Brushes.Black; PO3_TextCol = System.Windows.Media.Brushes.Black; PO3_UpCol = System.Windows.Media.Brushes.LimeGreen; PO3_WickCol = System.Windows.Media.Brushes.Black;
				PO3_Opacity = 100; PO3_Offset = 130; PO3_FontSize = 18; PO3_BarWidth = 50; Show_PO3 = true;
				GapOpacity = 0.15f; FilledOpacity = 0.05f; TextPadding = 120; CustomRay_ExpireDays = 1;
				Show_AS = Show_LO = Show_NYAM = Show_NYL = Show_NYPM = true;
				Time_Asia_S = DateTime.Parse("20:00", CultureInfo.InvariantCulture); Time_Asia_E = DateTime.Parse("00:00", CultureInfo.InvariantCulture);
				Time_London_S = DateTime.Parse("02:00", CultureInfo.InvariantCulture); Time_London_E = DateTime.Parse("05:00", CultureInfo.InvariantCulture);
				Time_NYAM_S = DateTime.Parse("08:30", CultureInfo.InvariantCulture); Time_NYAM_E = DateTime.Parse("11:00", CultureInfo.InvariantCulture);
				Time_NYL_S = DateTime.Parse("12:00", CultureInfo.InvariantCulture); Time_NYL_E = DateTime.Parse("13:00", CultureInfo.InvariantCulture);
				Time_NYPM_S = DateTime.Parse("13:30", CultureInfo.InvariantCulture); Time_NYPM_E = DateTime.Parse("16:00", CultureInfo.InvariantCulture);
				Box_Opacity = 10; Box_Font = new NinjaTrader.Gui.Tools.SimpleFont("Arial", 14) { Bold = true, Italic = true };
				Box_TextCol = System.Windows.Media.Brushes.Black; Box_CE_Dash = DashStyleHelper.Dash; Box_CE_Width = 1; Box_CE_Col = System.Windows.Media.Brushes.Gray;
				Color_Box_AS = System.Windows.Media.Brushes.Purple; Color_Box_LO = System.Windows.Media.Brushes.Blue;
				Color_Box_NYAM = System.Windows.Media.Brushes.Green; Color_Box_NYL = System.Windows.Media.Brushes.Orange;
				Color_Box_NYPM = System.Windows.Media.Brushes.Red;
			}
			else if (State == State.Configure)
			{
				useInternalOneMinute = BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 1;
				dataSeriesBip = useInternalOneMinute ? 1 : 0;
				if (useInternalOneMinute)
					AddDataSeries(BarsPeriodType.Minute, 1);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != dataSeriesBip) return;
			if (CurrentBars == null || CurrentBars.Length <= dataSeriesBip || CurrentBars[dataSeriesBip] < 50) return;
			DateTime now = Time[0];
			int nowTime = ToTime(now);
			int prevTime = ToTime(Time[1]);

			if (Bars.IsFirstBarOfSession) 
			{
				levels["DO"] = new TimedLevel(Open[0], now);
				currentRthCe = new TimedLevel(0, DateTime.MinValue);
				double dailyAnchor = 0; DateTime dailyTime = DateTime.MinValue;
				for (int i = 1; i < Math.Min(CurrentBar, 8000); i++) { 
					if (Time[i].DayOfWeek != DayOfWeek.Sunday && ToTime(Time[i]) <= RthCloseTime) { 
						dailyAnchor = Close[i]; dailyTime = Time[i]; break; 
					} 
				}
				if (dailyAnchor > 0) levels["PDC"] = new TimedLevel(dailyAnchor, dailyTime);
				if (now.DayOfWeek == DayOfWeek.Monday || now.DayOfWeek == DayOfWeek.Sunday) {
					if (!levels.ContainsKey("WO") || (now - levels["WO"].Time).TotalDays > 3) {
						levels["WO"] = new TimedLevel(Open[0], now);
						double weeklyAnchor = 0; DateTime weeklyTime = DateTime.MinValue;
						for (int j = 1; j < Math.Min(CurrentBar, 10000); j++) {
							if (Time[j].DayOfWeek == DayOfWeek.Friday && ToTime(Time[j]) <= RthCloseTime) {
								weeklyAnchor = Close[j]; weeklyTime = Time[j]; break;
							}
						}
						if (weeklyAnchor > 0) {
							levels["PWC"] = new TimedLevel(weeklyAnchor, weeklyTime); 
							if (Show_NWOG) CreateGap("NWOG", weeklyAnchor, Open[0], Color_NWOG, Days_NWOG, now);
						}
						UpdatePWH_PWL_Only();
					}
				}
				if (dailyAnchor > 0 && Show_NDOG) CreateGap("NDOG", dailyAnchor, Open[0], Color_NDOG, Days_NDOG, now);
				gaps.RemoveAll(g => (now - g.StartTime).TotalDays > g.ExpireDays);
			}

			if (prevTime < 093000 && nowTime >= 093000) {
				levels["RTHOPEN"] = new TimedLevel(Open[0], now);
				if (levels.ContainsKey("DO") && Show_RTHGAP) {
					CreateGap("RTHGAP", levels["DO"].Price, Open[0], Color_RTHGAP, Days_RTHGAP, now);
					if (Show_RTHMID) currentRthCe = new TimedLevel((levels["DO"].Price + Open[0]) / 2.0, now);
				}
			}

			if (!levels.ContainsKey("RTHCLOSE") || (prevTime < RthCloseTime && nowTime >= RthCloseTime)) 
			{
				for (int i = 0; i < Math.Min(CurrentBar, 8000); i++) { 
					if (Time[i].DayOfWeek != DayOfWeek.Sunday && ToTime(Time[i]) <= RthCloseTime) { 
						levels["RTHCLOSE"] = new TimedLevel(Close[i], Time[i]); 
						break; 
					} 
				}
			}

			RunCommonLogic(now, nowTime, prevTime);
			UpdateGapStatus(now);
		}

		private void UpdateGapStatus(DateTime now) {
			foreach (var gap in gaps) {
				if (!gap.IsFilled && High[0] >= gap.HighPrice && Low[0] <= gap.LowPrice) gap.IsFilled = true;
				string tag = "Gap_" + gap.StartTime.Ticks + gap.Feature;
				int op = (int)((gap.IsFilled ? FilledOpacity : GapOpacity) * 100);
				
				Draw.Rectangle(this, tag + "_rect", false, gap.StartTime, gap.HighPrice, now, gap.LowPrice, System.Windows.Media.Brushes.Transparent, gap.Color, op);
				
				double mid = (gap.HighPrice + gap.LowPrice) / 2.0;
				string gapLabel = "";
				int yOffset = 10; // 默认偏移量

				if (gap.Feature == "NDOG") {
					Draw.Line(this, tag + "_CE", false, gap.StartTime, mid, now, mid, Color_RTHMID, NDOG_CE_Dash, NDOG_CE_Width);
					gapLabel = "NDOG | " + gap.StartTime.ToString("ddd MM/dd");
					yOffset = 10; 
				}
				else if (gap.Feature == "NWOG") {
					Draw.Line(this, tag + "_CE", false, gap.StartTime, mid, now, mid, Color_RTHMID, NWOG_CE_Dash, NWOG_CE_Width);
					gapLabel = "NWOG | " + gap.StartTime.ToString("yyyy-MM-dd");
					yOffset = 27; // 仅修改此处：将 NWOG 向上偏移，避免与 NDOG 重叠
				}
				else if (gap.Feature == "RTHGAP") {
					Draw.Line(this, tag + "_CE", false, gap.StartTime, mid, now, mid, Color_RTHMID, Gap_CE_Dash, Gap_CE_Width);
					gapLabel = "RTH GAP";
					yOffset = -15;
				}

				Draw.Text(this, tag + "_txt", false, gapLabel, now, gap.HighPrice, yOffset, System.Windows.Media.Brushes.Black, MyFont, System.Windows.TextAlignment.Right, System.Windows.Media.Brushes.Transparent, System.Windows.Media.Brushes.Transparent, 0);
			}
		}

		private bool GetSessionWindow(DateTime now, DateTime start, DateTime end, out DateTime sessionStart, out DateTime sessionEnd) {
			TimeSpan startTod = start.TimeOfDay;
			TimeSpan endTod = end.TimeOfDay;
			TimeSpan nowTod = now.TimeOfDay;

			if (endTod <= startTod) {
				if (nowTod >= startTod) {
					sessionStart = now.Date.Add(startTod);
					sessionEnd = now.Date.AddDays(1).Add(endTod);
				}
				else {
					sessionStart = now.Date.AddDays(-1).Add(startTod);
					sessionEnd = now.Date.Add(endTod);
				}
			}
			else {
				sessionStart = now.Date.Add(startTod);
				sessionEnd = now.Date.Add(endTod);
			}

			return now >= sessionStart;
		}
		private void DrawSessionBox(string id, DateTime start, DateTime end, System.Windows.Media.Brush col, string label) {
			DateTime now = Time[0], sDT, eDT;
			if (!GetSessionWindow(now, start, end, out sDT, out eDT)) return;
			if (!IsInCurrentTradingSession(sDT)) return;
			int sIdx = BarsArray[dataSeriesBip].GetBar(sDT); if (sIdx < 0) return;
			double hV = double.MinValue, lV = double.MaxValue;
			int endIdx = (now < eDT) ? CurrentBar : BarsArray[dataSeriesBip].GetBar(eDT);
			if (endIdx < sIdx) return;
			for (int i = sIdx; i <= endIdx; i++) { hV = Math.Max(hV, Highs[dataSeriesBip].GetValueAt(i)); lV = Math.Min(lV, Lows[dataSeriesBip].GetValueAt(i)); }
			Draw.Rectangle(this, "Box_"+id+sDT.Ticks+"_rect", false, sDT, hV, (now < eDT ? now : eDT), lV, System.Windows.Media.Brushes.Transparent, col, Box_Opacity);
			double mid = (hV + lV) / 2.0;
			Draw.Line(this, "Box_"+id+sDT.Ticks+"_CE", false, sDT, mid, (now < eDT ? now : eDT), mid, Box_CE_Col, Box_CE_Dash, Box_CE_Width);
			Draw.Text(this, "Box_"+id+sDT.Ticks+"_txt", false, label, sDT, hV, 18, Box_TextCol, Box_Font, System.Windows.TextAlignment.Left, System.Windows.Media.Brushes.Transparent, System.Windows.Media.Brushes.Transparent, 0);
		}

		private void RunCommonLogic(DateTime now, int nowTime, int prevTime) {
			if ((prevTime < 000000 && nowTime >= 000000) || (now.Date != Time[1].Date && nowTime < 040000)) {
				if (!levels.ContainsKey("TDO") || levels["TDO"].Time.Date != now.Date) levels["TDO"] = new TimedLevel(Open[0], now);
				if (Show_PO3) { po3O = Open[0]; po3H = High[0]; po3L = Low[0]; po3C = Close[0]; }
			}
			if (Show_PO3 && po3O > 0) { 
				po3H = Math.Max(po3H, High[0]); 
				po3L = Math.Min(po3L, Low[0]); 
				po3C = Close[0]; 
			}
			if (nowTime >= 180000 || nowTime < 170000) {
				if (High[0] > pdhRunningHigh) { pdhRunningHigh = High[0]; pdhTime = now; }
				if (Low[0] < pdlRunningLow) { pdlRunningLow = Low[0]; pdlTime = now; }
			}
			if (prevTime < 170000 && nowTime >= 170000) {
				if (pdhRunningHigh != double.MinValue) { levels["PDH"] = new TimedLevel(pdhRunningHigh, pdhTime); levels["PDL"] = new TimedLevel(pdlRunningLow, pdlTime); }
				pdhRunningHigh = double.MinValue; pdlRunningLow = double.MaxValue;
			}
			UpdateSessionExtremes("AS", Time_Asia_S, Time_Asia_E);
			UpdateSessionExtremes("LO", Time_London_S, Time_London_E);
			UpdateSessionExtremes("NYAM", Time_NYAM_S, Time_NYAM_E);
			UpdateSessionExtremes("NYL", Time_NYL_S, Time_NYL_E);
			UpdateSessionExtremes("NYPM", Time_NYPM_S, Time_NYPM_E);
			if (Show_AS) DrawSessionBox("AS", Time_Asia_S, Time_Asia_E, Color_Box_AS, "Asian");
			if (Show_LO) DrawSessionBox("LO", Time_London_S, Time_London_E, Color_Box_LO, "London");
			if (Show_NYAM) DrawSessionBox("NYAM", Time_NYAM_S, Time_NYAM_E, Color_Box_NYAM, "NY AM");
			if (Show_NYL) DrawSessionBox("NYL", Time_NYL_S, Time_NYL_E, Color_Box_NYL, "NY Lunch");
			if (Show_NYPM) DrawSessionBox("NYPM", Time_NYPM_S, Time_NYPM_E, Color_Box_NYPM, "NY PM");
		}

		private void UpdateSessionExtremes(string id, DateTime start, DateTime end) {
			DateTime now = Time[0], sDT, eDT;
			if (!GetSessionWindow(now, start, end, out sDT, out eDT)) return;
			if (!IsInCurrentTradingSession(sDT)) return;
			int sIdx = BarsArray[dataSeriesBip].GetBar(sDT); if (sIdx < 0) return;
			double hV = double.MinValue, lV = double.MaxValue; DateTime hT = sDT, lT = sDT;
			int endIdx = (now < eDT) ? CurrentBar : BarsArray[dataSeriesBip].GetBar(eDT);
			if (endIdx < sIdx) return;
			for (int i = sIdx; i <= endIdx; i++) {
				if (Highs[dataSeriesBip].GetValueAt(i) > hV) { hV = Highs[dataSeriesBip].GetValueAt(i); hT = BarsArray[dataSeriesBip].GetTime(i); }
				if (Lows[dataSeriesBip].GetValueAt(i) < lV) { lV = Lows[dataSeriesBip].GetValueAt(i); lT = BarsArray[dataSeriesBip].GetTime(i); }
			}
			if (hV != double.MinValue) { sessionExtremes[id + " High"] = new TimedLevel(hV, hT); sessionExtremes[id + " Low"] = new TimedLevel(lV, lT); }
			if (now >= eDT && hV != double.MinValue) {
				previousSessionExtremes["Prev " + id + " High"] = new TimedLevel(hV, hT);
				previousSessionExtremes["Prev " + id + " Low"] = new TimedLevel(lV, lT);
			}
		}

		private void UpdatePWH_PWL_Only() {
			DateTime today = Time[0].Date;
			int daysToMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
			DateTime lastMonday = today.AddDays(-daysToMonday - 7);
			DateTime thisMonday = today.AddDays(-daysToMonday);
			double tH = double.MinValue, tL = double.MaxValue; DateTime thT = DateTime.MinValue, tlT = DateTime.MinValue;
			for (int j = 1; j < Math.Min(CurrentBar, 15000); j++) {
				if (Time[j] >= lastMonday && Time[j] < thisMonday) {
					if (High[j] > tH) { tH = High[j]; thT = Time[j]; }
					if (Low[j] < tL) { tL = Low[j]; tlT = Time[j]; }
				}
			}
			if (thT != DateTime.MinValue) { levels["PWH"] = new TimedLevel(tH, thT); levels["PWL"] = new TimedLevel(tL, tlT); }
		}

		private void CreateGap(string f, double p1, double p2, System.Windows.Media.Brush c, int d, DateTime start) {
			gaps.Add(new GapZone { Feature = f, HighPrice = Math.Max(p1, p2), LowPrice = Math.Min(p1, p2), StartTime = start, IsFilled = false, Color = c, ExpireDays = d });
		}

		public override void OnRenderTargetChanged() {
			if (WickBrushDx != null) WickBrushDx.Dispose(); if (UpFillDx != null) UpFillDx.Dispose(); if (DownFillDx != null) DownFillDx.Dispose();
			if (TextBrushDx != null) TextBrushDx.Dispose(); if (po3Font != null) po3Font.Dispose();
			if (RenderTarget != null) {
				WickBrushDx = PO3_WickCol.ToDxBrush(RenderTarget); TextBrushDx = PO3_TextCol.ToDxBrush(RenderTarget);
				UpFillDx = PO3_UpCol.ToDxBrush(RenderTarget); UpFillDx.Opacity = PO3_Opacity / 100f; DownFillDx = PO3_DnCol.ToDxBrush(RenderTarget); DownFillDx.Opacity = PO3_Opacity / 100f;
				po3Font = new TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", (float)PO3_FontSize) { 
					TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading, 
					ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center 
				};
			}
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (RenderTarget == null || chartControl == null || chartScale == null) return;
			bool po3Ready = WickBrushDx != null && UpFillDx != null && DownFillDx != null && TextBrushDx != null && po3Font != null;
			if (Show_PO3 && po3Ready && po3O > 0) {
				float yO = chartScale.GetYByValue(po3O), yH = chartScale.GetYByValue(po3H), yL = chartScale.GetYByValue(po3L), yC = chartScale.GetYByValue(po3C);
				float endX = (float)chartControl.CanvasRight - PO3_Offset, startX = endX - PO3_BarWidth, centerX = startX + (PO3_BarWidth / 2);
				
				RenderTarget.DrawLine(new Vector2(centerX, yH), new Vector2(centerX, yL), WickBrushDx, 2);
				RectangleF bodyRect = new RectangleF(startX, Math.Min(yO, yC), PO3_BarWidth, Math.Max(2, Math.Abs(yO - yC)));
				RenderTarget.FillRectangle(bodyRect, (po3C >= po3O) ? UpFillDx : DownFillDx); 
				RenderTarget.DrawRectangle(bodyRect, WickBrushDx, 1);
				
				string[] labels = { 
					"H: " + BarsArray[0].Instrument.MasterInstrument.FormatPrice(po3H), 
					"O: " + BarsArray[0].Instrument.MasterInstrument.FormatPrice(po3O), 
					"C: " + BarsArray[0].Instrument.MasterInstrument.FormatPrice(po3C), 
					"L: " + BarsArray[0].Instrument.MasterInstrument.FormatPrice(po3L) 
				};
				float[] yPositions = { yH, yO, yC, yL };

				for (int i = 0; i < labels.Length; i++) {
					using (TextLayout layout = new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, labels[i], po3Font, 250f, (float)PO3_FontSize * 1.5f)) {
						RenderTarget.DrawTextLayout(new Vector2(endX + 5, yPositions[i] - (layout.Metrics.Height / 2)), layout, TextBrushDx);
					}
				}
			}

			using (var dxCoreStyle = new SharpDX.Direct2D1.StrokeStyle(NinjaTrader.Core.Globals.D2DFactory, new StrokeStyleProperties { DashStyle = (RayDashStyle == DashStyleHelper.Solid ? SharpDX.Direct2D1.DashStyle.Solid : SharpDX.Direct2D1.DashStyle.Dash) }))
			using (var dxSessStyle = new SharpDX.Direct2D1.StrokeStyle(NinjaTrader.Core.Globals.D2DFactory, new StrokeStyleProperties { DashStyle = (SessionDashStyle == DashStyleHelper.Solid ? SharpDX.Direct2D1.DashStyle.Solid : SharpDX.Direct2D1.DashStyle.Dash) }))
			using (var dxCeStyle = new SharpDX.Direct2D1.StrokeStyle(NinjaTrader.Core.Globals.D2DFactory, new StrokeStyleProperties { DashStyle = (Gap_CE_Dash == DashStyleHelper.Solid ? SharpDX.Direct2D1.DashStyle.Solid : SharpDX.Direct2D1.DashStyle.Dash) }))
			using (TextFormat textFormat = MyFont.ToDirectWriteTextFormat()) {
				List<int> usedY = new List<int>();
				Dictionary<double, string> mergedNames = new Dictionary<double, string>(); 
				Dictionary<double, DateTime> mergedTimes = new Dictionary<double, DateTime>();
				Dictionary<string, TimedLevel> allLevels = new Dictionary<string, TimedLevel>(levels);
				foreach(var se in sessionExtremes) allLevels[se.Key] = se.Value;
				foreach (var kvp in allLevels) {
					if (!IsVisibleLevel(kvp.Key)) continue;
					double pr = Math.Round(kvp.Value.Price, TickSize >= 0.25 ? 2 : 5);
					if (!mergedNames.ContainsKey(pr)) { mergedNames[pr] = kvp.Key; mergedTimes[pr] = kvp.Value.Time; }
					else if (!mergedNames[pr].Contains(kvp.Key)) mergedNames[pr] += " | " + kvp.Key;
				}
				foreach (var item in mergedNames) {
					int y = chartScale.GetYByValue(item.Key);
					float sLX = chartControl.GetXByTime(mergedTimes[item.Key]); if (sLX < 0) sLX = 0; 
					string lbl = string.Format(
					    "{0} | {1} | {2} | {3}",
					    item.Value,
					    mergedTimes[item.Key].ToString("ddd MM/dd"),
					    mergedTimes[item.Key].ToString("HH:mm"),
					    BarsArray[0].Instrument.MasterInstrument.FormatPrice(item.Key)
					);

					int yOff = 0; foreach (int uY in usedY) if (Math.Abs(uY - y) < 18) yOff -= 20;
					usedY.Add(y + yOff);
					bool isHTFCore = (item.Value.Contains("PDC") || item.Value.Contains("RTHCLOSE") || item.Value.Contains("PDH") || item.Value.Contains("PDL") || item.Value.Contains("DO") || item.Value.Contains("WO") || item.Value.Contains("RTHOPEN") || item.Value.Contains("TDO"));
					System.Windows.Media.Brush drawCol = isHTFCore ? Color_Levels : SessionColor;
					int drawWidth = isHTFCore ? RayWidth : SessionRayWidth;
					var drawStyle = isHTFCore ? dxCoreStyle : dxSessStyle;
					DrawRay(chartControl, sLX, y, lbl, drawCol, textFormat, drawStyle, yOff, drawWidth);
				}
				if (Show_RTHMID && currentRthCe.Price > 0) {
					int yCe = chartScale.GetYByValue(currentRthCe.Price);
					float sLXCe = chartControl.GetXByTime(currentRthCe.Time); if (sLXCe < 0) sLXCe = 0;
					string lblCe = string.Format("RTH GAP CE | {0}", BarsArray[0].Instrument.MasterInstrument.FormatPrice(currentRthCe.Price));
					int yOffCe = 0; foreach (int uY in usedY) if (Math.Abs(uY - yCe) < 18) yOffCe -= 20;
					usedY.Add(yCe + yOffCe);
					DrawRay(chartControl, sLXCe, yCe, lblCe, Color_RTHMID, textFormat, dxCeStyle, yOffCe, Gap_CE_Width);
				}
			}
		}

		private bool IsVisibleLevel(string key) {
			if (key == "DO") return Show_DO;
			if (key == "WO") return Show_WO;
			if (key == "TDO") return Show_TDO;
			if (key == "PDC") return Show_PDC;
			if (key == "PWC") return Show_PWC;
			if (key == "PDH") return Show_PDH;
			if (key == "PDL") return Show_PDL;
			if (key == "PWH") return Show_PWH;
			if (key == "PWL") return Show_PWL;
			if (key == "RTHCLOSE") return Show_RTHCLOSE;
			if (key == "RTHOPEN") return Show_RTHOPEN;
			if (key.StartsWith("AS ")) return Show_AS;
			if (key.StartsWith("LO ")) return Show_LO;
			if (key.StartsWith("NYAM ")) return Show_NYAM;
			if (key.StartsWith("NYL ")) return Show_NYL;
			if (key.StartsWith("NYPM ")) return Show_NYPM;
			return true;
		}

		public double GetNearestKeyLevelAbove(double price)
		{
			double best = double.NaN;
			AddNearestAbove(ref best, price, levels);
			AddNearestAbove(ref best, price, sessionExtremes);
			if (IsCurrentRthGapReferenceActive())
				AddCandidateAbove(ref best, price, currentRthCe.Price);
			AddGapCandidates(ref best, price, true);
			return best;
		}

		public double GetNearestKeyLevelBelow(double price)
		{
			double best = double.NaN;
			AddNearestBelow(ref best, price, levels);
			AddNearestBelow(ref best, price, sessionExtremes);
			if (IsCurrentRthGapReferenceActive())
				AddCandidateBelow(ref best, price, currentRthCe.Price);
			AddGapCandidates(ref best, price, false);
			return best;
		}

		public List<ICTKeyLevelFact> GetKeyLevelFacts()
		{
			List<ICTKeyLevelFact> facts = new List<ICTKeyLevelFact>();
			DateTime now = CurrentBar >= 0 ? Time[0] : DateTime.MinValue;

			AddLevelFacts(facts, levels, false, now);
			AddLevelFacts(facts, sessionExtremes, true, now);
			AddLevelFacts(facts, previousSessionExtremes, false, now);

			if (IsCurrentRthGapReferenceActive())
				AddFact(facts, "RTHGAP_CE", "RTH GAP", ICTKeyLevelKind.GapCE, currentRthCe.Price, currentRthCe.Time, currentRthCe.Time, currentRthCe.Time.Date.AddDays(1), true, false, false);

			for (int i = 0; i < gaps.Count; i++)
			{
				GapZone gap = gaps[i];
				bool active = !gap.IsFilled && (now - gap.StartTime).TotalDays <= gap.ExpireDays;
				if (gap.Feature == "RTHGAP" && !IsCurrentRthGapReferenceActive(gap.StartTime))
					active = false;
				if (!active && gap.Feature == "RTHGAP")
					continue;

				DateTime validTo = gap.StartTime.AddDays(Math.Max(1, gap.ExpireDays));
				double ce = (gap.HighPrice + gap.LowPrice) * 0.5;
				AddFact(facts, gap.Feature + "_HIGH_" + gap.StartTime.Ticks, gap.Feature, ICTKeyLevelKind.GapHigh, gap.HighPrice, gap.StartTime, gap.StartTime, validTo, active, gap.IsFilled, false);
				AddFact(facts, gap.Feature + "_LOW_" + gap.StartTime.Ticks, gap.Feature, ICTKeyLevelKind.GapLow, gap.LowPrice, gap.StartTime, gap.StartTime, validTo, active, gap.IsFilled, false);
				AddFact(facts, gap.Feature + "_CE_" + gap.StartTime.Ticks, gap.Feature, ICTKeyLevelKind.GapCE, ce, gap.StartTime, gap.StartTime, validTo, active, gap.IsFilled, false);
			}

			return facts;
		}

		public ICTKeyLevelFact GetNearestKeyLevelFactAbove(double price)
		{
			ICTKeyLevelFact best = null;
			List<ICTKeyLevelFact> facts = GetKeyLevelFacts();
			for (int i = 0; i < facts.Count; i++)
			{
				ICTKeyLevelFact fact = facts[i];
				if (!IsUsableKeyLevelFact(fact) || fact.Price <= price + TickSize)
					continue;
				if (best == null || fact.Price < best.Price)
					best = fact;
			}
			return best;
		}

		public ICTKeyLevelFact GetNearestKeyLevelFactBelow(double price)
		{
			ICTKeyLevelFact best = null;
			List<ICTKeyLevelFact> facts = GetKeyLevelFacts();
			for (int i = 0; i < facts.Count; i++)
			{
				ICTKeyLevelFact fact = facts[i];
				if (!IsUsableKeyLevelFact(fact) || fact.Price >= price - TickSize)
					continue;
				if (best == null || fact.Price > best.Price)
					best = fact;
			}
			return best;
		}

		private void AddLevelFacts(List<ICTKeyLevelFact> facts, Dictionary<string, TimedLevel> source, bool currentSession, DateTime now)
		{
			foreach (var item in source)
			{
				if (!IsVisibleLevel(NormalizeSessionKey(item.Key)))
					continue;
				AddFact(facts, item.Key, SourceFromKey(item.Key), KindFromKey(item.Key), item.Value.Price, item.Value.Time, item.Value.Time, now == DateTime.MinValue ? DateTime.MaxValue : now.AddDays(1), true, false, currentSession);
			}
		}

		private void AddFact(List<ICTKeyLevelFact> facts, string key, string source, ICTKeyLevelKind kind, double price, DateTime time, DateTime validFrom, DateTime validTo, bool active, bool filled, bool currentSession)
		{
			if (price <= 0 || double.IsNaN(price) || double.IsInfinity(price))
				return;
			facts.Add(new ICTKeyLevelFact
			{
				Key = key,
				Source = source,
				Kind = kind,
				Price = price,
				Time = time,
				ValidFrom = validFrom,
				ValidTo = validTo,
				IsActive = active,
				IsFilled = filled,
				IsCurrentSession = currentSession,
				Label = source + " " + kind.ToString()
			});
		}

		private bool IsUsableKeyLevelFact(ICTKeyLevelFact fact)
		{
			return fact != null && fact.IsActive && !fact.IsFilled && fact.Price > 0 && !double.IsNaN(fact.Price) && !double.IsInfinity(fact.Price);
		}

		private string NormalizeSessionKey(string key)
		{
			return key != null && key.StartsWith("Prev ") ? key.Substring(5) : key;
		}

		private string SourceFromKey(string key)
		{
			string normalized = NormalizeSessionKey(key);
			if (normalized.StartsWith("AS ")) return key.StartsWith("Prev ") ? "Previous Asian" : "Asian";
			if (normalized.StartsWith("LO ")) return key.StartsWith("Prev ") ? "Previous London" : "London";
			if (normalized.StartsWith("NYAM ")) return key.StartsWith("Prev ") ? "Previous NY AM" : "NY AM";
			if (normalized.StartsWith("NYL ")) return key.StartsWith("Prev ") ? "Previous NY Lunch" : "NY Lunch";
			if (normalized.StartsWith("NYPM ")) return key.StartsWith("Prev ") ? "Previous NY PM" : "NY PM";
			return normalized;
		}

		private ICTKeyLevelKind KindFromKey(string key)
		{
			string normalized = NormalizeSessionKey(key);
			if (normalized.EndsWith(" High")) return ICTKeyLevelKind.SessionHigh;
			if (normalized.EndsWith(" Low")) return ICTKeyLevelKind.SessionLow;
			if (normalized == "DO" || normalized == "WO" || normalized == "TDO" || normalized == "RTHOPEN") return ICTKeyLevelKind.Open;
			if (normalized == "PDC" || normalized == "PWC" || normalized == "RTHCLOSE") return ICTKeyLevelKind.Close;
			if (normalized == "PDH" || normalized == "PWH") return ICTKeyLevelKind.High;
			if (normalized == "PDL" || normalized == "PWL") return ICTKeyLevelKind.Low;
			return ICTKeyLevelKind.Unknown;
		}

		private void AddNearestAbove(ref double best, double price, Dictionary<string, TimedLevel> source)
		{
			foreach (var item in source)
			{
				if (!IsVisibleLevel(item.Key))
					continue;
				AddCandidateAbove(ref best, price, item.Value.Price);
			}
		}

		private void AddNearestBelow(ref double best, double price, Dictionary<string, TimedLevel> source)
		{
			foreach (var item in source)
			{
				if (!IsVisibleLevel(item.Key))
					continue;
				AddCandidateBelow(ref best, price, item.Value.Price);
			}
		}

		private void AddGapCandidates(ref double best, double price, bool above)
		{
			foreach (var gap in gaps)
			{
				if (gap.IsFilled)
					continue;
				if (gap.Feature == "RTHGAP" && !IsCurrentRthGapReferenceActive(gap.StartTime))
					continue;
				double mid = (gap.HighPrice + gap.LowPrice) * 0.5;
				if (above)
				{
					AddCandidateAbove(ref best, price, gap.LowPrice);
					AddCandidateAbove(ref best, price, mid);
					AddCandidateAbove(ref best, price, gap.HighPrice);
				}
				else
				{
					AddCandidateBelow(ref best, price, gap.HighPrice);
					AddCandidateBelow(ref best, price, mid);
					AddCandidateBelow(ref best, price, gap.LowPrice);
				}
			}
		}

		private bool IsCurrentRthGapReferenceActive()
		{
			return currentRthCe.Price > 0 && IsCurrentRthGapReferenceActive(currentRthCe.Time);
		}

		private bool IsCurrentRthGapReferenceActive(DateTime referenceTime)
		{
			if (referenceTime == DateTime.MinValue)
				return false;
			if (referenceTime.Date != Time[0].Date)
				return false;
			return ToTime(Time[0]) >= 093000;
		}

		private void AddCandidateAbove(ref double best, double price, double candidate)
		{
			if (candidate <= price + TickSize || double.IsNaN(candidate) || double.IsInfinity(candidate))
				return;
			if (double.IsNaN(best) || candidate < best)
				best = candidate;
		}

		private void AddCandidateBelow(ref double best, double price, double candidate)
		{
			if (candidate >= price - TickSize || double.IsNaN(candidate) || double.IsInfinity(candidate))
				return;
			if (double.IsNaN(best) || candidate > best)
				best = candidate;
		}

		private void DrawRay(ChartControl chartControl, float sX, int y, string txt, System.Windows.Media.Brush col, TextFormat fmt, SharpDX.Direct2D1.StrokeStyle style, int yOff, int width) {
			if (RenderTarget == null || chartControl == null || col == null || fmt == null) return;
			using (TextLayout layout = new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, txt, fmt, 800, (float)MyFont.Size))
			using (var dxBrush = col.ToDxBrush(RenderTarget)) {
				float tX = (float)chartControl.CanvasRight - layout.Metrics.Width - TextPadding;
				RenderTarget.DrawLine(new Vector2(Math.Max(-10, sX), y), new Vector2(tX - 5, y), dxBrush, (float)width, style);
				RenderTarget.DrawTextLayout(new Vector2(tX, y - (layout.Metrics.Height/2) + yOff), layout, dxBrush);
			}
		}
		#region Properties
		// 新增属性：RTH收盘参考时间 (格式 HHMMSS)
		[Display(Name="RTH 收盘参考时间", Description="计算PDC/PWC/RTH Close时使用的参考时间 (例如 160000 代表 4:00 PM)", GroupName="1. HTF 核心", Order=0)]
		public int RthCloseTime { get; set; }

		[Display(Name="NDOG 天数", GroupName="1. HTF 核心")] public int Days_NDOG { get; set; }
		[Display(Name="NWOG 天数", GroupName="1. HTF 核心")] public int Days_NWOG { get; set; }
		[Display(Name="RTH GAP 天数", GroupName="1. HTF 核心")] public int Days_RTHGAP { get; set; }
		[Display(Name="Show DO", GroupName="1. HTF 开关")] public bool Show_DO { get; set; }
		[Display(Name="Show NDOG", GroupName="1. HTF 开关")] public bool Show_NDOG { get; set; }
		[Display(Name="Show NWOG", GroupName="1. HTF 开关")] public bool Show_NWOG { get; set; }
		[Display(Name="Show PDC", GroupName="1. HTF 开关")] public bool Show_PDC { get; set; }
		[Display(Name="Show PDH", GroupName="1. HTF 开关")] public bool Show_PDH { get; set; }
		[Display(Name="Show PDL", GroupName="1. HTF 开关")] public bool Show_PDL { get; set; }
		[Display(Name="Show PO3", GroupName="1. HTF 开关")] public bool Show_PO3 { get; set; }
		[Display(Name="Show PWC", GroupName="1. HTF 开关")] public bool Show_PWC { get; set; }
		[Display(Name="Show PWH", GroupName="1. HTF 开关")] public bool Show_PWH { get; set; }
		[Display(Name="Show PWL", GroupName="1. HTF 开关")] public bool Show_PWL { get; set; }
		[Display(Name="Show RTH Close", GroupName="1. HTF 开关")] public bool Show_RTHCLOSE { get; set; }
		[Display(Name="Show RTH GAP", GroupName="1. HTF 开关")] public bool Show_RTHGAP { get; set; }
		[Display(Name="Show RTH GAP CE", GroupName="1. HTF 开关")] public bool Show_RTHMID { get; set; }
		[Display(Name="Show RTH Open", GroupName="1. HTF 开关")] public bool Show_RTHOPEN { get; set; }
		[Display(Name="Show TDO", GroupName="1. HTF 开关")] public bool Show_TDO { get; set; }
		[Display(Name="Show WO", GroupName="1. HTF 开关")] public bool Show_WO { get; set; }
		
		[Display(Name="NDOG CE 线型", GroupName="2. HTF 视觉")] public DashStyleHelper NDOG_CE_Dash { get; set; }
		[Display(Name="NDOG CE 线宽", GroupName="2. HTF 视觉")] public int NDOG_CE_Width { get; set; }
		[Display(Name="NWOG CE 线型", GroupName="2. HTF 视觉")] public DashStyleHelper NWOG_CE_Dash { get; set; }
		[Display(Name="NWOG CE 线宽", GroupName="2. HTF 视觉")] public int NWOG_CE_Width { get; set; }
		[Display(Name="Gap CE 线型", GroupName="2. HTF 视觉")] public DashStyleHelper Gap_CE_Dash { get; set; }
		[Display(Name="Gap CE 线宽", GroupName="2. HTF 视觉")] public int Gap_CE_Width { get; set; }

		[XmlIgnore][Display(Name="CE 颜色", GroupName="2. HTF 视觉")] public System.Windows.Media.Brush Color_RTHMID { get; set; }
		[Browsable(false)] public string ColorRTHMIDS { get { return Serialize.BrushToString(Color_RTHMID); } set { Color_RTHMID = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name="NDOG 颜色", GroupName="2. HTF 视觉")] public System.Windows.Media.Brush Color_NDOG { get; set; }
		[Browsable(false)] public string ColorNDOGS { get { return Serialize.BrushToString(Color_NDOG); } set { Color_NDOG = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name="NWOG 颜色", GroupName="2. HTF 视觉")] public System.Windows.Media.Brush Color_NWOG { get; set; }
		[Browsable(false)] public string ColorNWOGS { get { return Serialize.BrushToString(Color_NWOG); } set { Color_NWOG = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name="RTH GAP 颜色", GroupName="2. HTF 视觉")] public System.Windows.Media.Brush Color_RTHGAP { get; set; }
		[Browsable(false)] public string ColorRTHGAPS { get { return Serialize.BrushToString(Color_RTHGAP); } set { Color_RTHGAP = Serialize.StringToBrush(value); } }
		[Display(Name="全局标签字体", GroupName="2. HTF 视觉")] public NinjaTrader.Gui.Tools.SimpleFont MyFont { get; set; }
		[Display(Name="核心线型", GroupName="2. HTF 视觉")] public DashStyleHelper RayDashStyle { get; set; }
		[Display(Name="核心线宽", GroupName="2. HTF 视觉")] public int RayWidth { get; set; }
		[XmlIgnore][Display(Name="核心颜色", GroupName="2. HTF 视觉")] public System.Windows.Media.Brush Color_Levels { get; set; }
		[Browsable(false)] public string ColorLevelsS { get { return Serialize.BrushToString(Color_Levels); } set { Color_Levels = Serialize.StringToBrush(value); } }
		[Display(Name="PO3 字体大小", GroupName="3. PO3 设置")] public int PO3_FontSize { get; set; }
		[Display(Name="PO3 右边距", GroupName="3. PO3 设置")] public int PO3_Offset { get; set; }
		[Display(Name="PO3 宽度", GroupName="3. PO3 设置")] public int PO3_BarWidth { get; set; }
		[Display(Name="PO3 不透明度", GroupName="3. PO3 设置")] public int PO3_Opacity { get; set; }
		[XmlIgnore][Display(Name="PO3 Up Color", GroupName="3. PO3 设置")] public System.Windows.Media.Brush PO3_UpCol { get; set; }
		[Browsable(false)] public string PO3UpS { get { return Serialize.BrushToString(PO3_UpCol); } set { PO3_UpCol = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name="PO3 Dn Color", GroupName="3. PO3 设置")] public System.Windows.Media.Brush PO3_DnCol { get; set; }
		[Browsable(false)] public string PO3DnS { get { return Serialize.BrushToString(PO3_DnCol); } set { PO3_DnCol = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name="PO3 Wick Color", GroupName="3. PO3 设置")] public System.Windows.Media.Brush PO3_WickCol { get; set; }
		[Browsable(false)] public string PO3WickS { get { return Serialize.BrushToString(PO3_WickCol); } set { PO3_WickCol = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name="PO3 Text Color", GroupName="3. PO3 设置")] public System.Windows.Media.Brush PO3_TextCol { get; set; }
		[Browsable(false)] public string PO3TextS { get { return Serialize.BrushToString(PO3_TextCol); } set { PO3_TextCol = Serialize.StringToBrush(value); } }
		[Display(Name="Gap 不透明度", GroupName="4. 其他视觉")] public float GapOpacity { get; set; }
		[Display(Name="填满后不透明度", GroupName="4. 其他视觉")] public float FilledOpacity { get; set; }
		[Display(Name="文字右边距", GroupName="4. 其他视觉")] public int TextPadding { get; set; }
		[Display(Name="Box 不透明度", GroupName="5. Session Box 配置")] public int Box_Opacity { get; set; }
		[Display(Name="Box 标签字体", GroupName="5. Session Box 配置")] public NinjaTrader.Gui.Tools.SimpleFont Box_Font { get; set; }
		[XmlIgnore][Display(Name="Box 标签颜色", GroupName="5. Session Box 配置")] public System.Windows.Media.Brush Box_TextCol { get; set; }
		[Browsable(false)] public string BoxTextColS { get { return Serialize.BrushToString(Box_TextCol); } set { Box_TextCol = Serialize.StringToBrush(value); } }
		[Display(Name="Box CE 线型", GroupName="5. Session Box 配置")] public DashStyleHelper Box_CE_Dash { get; set; }
		[Display(Name="Box CE 线宽", GroupName="5. Session Box 配置")] public int Box_CE_Width { get; set; }
		[XmlIgnore][Display(Name="Box CE 颜色", GroupName="5. Session Box 配置")] public System.Windows.Media.Brush Box_CE_Col { get; set; }
		[Browsable(false)] public string BoxCEColS { get { return Serialize.BrushToString(Box_CE_Col); } set { Box_CE_Col = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name="Asia Box Color", GroupName="5. Session Box 配置")] public System.Windows.Media.Brush Color_Box_AS { get; set; }
		[Browsable(false)] public string ColorBoxASS { get { return Serialize.BrushToString(Color_Box_AS); } set { Color_Box_AS = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name="London Box Color", GroupName="5. Session Box 配置")] public System.Windows.Media.Brush Color_Box_LO { get; set; }
		[Browsable(false)] public string ColorBoxLOS { get { return Serialize.BrushToString(Color_Box_LO); } set { Color_Box_LO = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name="NY AM Box Color", GroupName="5. Session Box 配置")] public System.Windows.Media.Brush Color_Box_NYAM { get; set; }
		[Browsable(false)] public string ColorBoxNYAMS { get { return Serialize.BrushToString(Color_Box_NYAM); } set { Color_Box_NYAM = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name="NY Lunch Box Color", GroupName="5. Session Box 配置")] public System.Windows.Media.Brush Color_Box_NYL { get; set; }
		[Browsable(false)] public string ColorBoxNYLS { get { return Serialize.BrushToString(Color_Box_NYL); } set { Color_Box_NYL = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name="NY PM Box Color", GroupName="5. Session Box 配置")] public System.Windows.Media.Brush Color_Box_NYPM { get; set; }
		[Browsable(false)] public string ColorBoxNYPMS { get { return Serialize.BrushToString(Color_Box_NYPM); } set { Color_Box_NYPM = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name="Session 颜色", GroupName="6. Session 视觉配置")] public System.Windows.Media.Brush SessionColor { get; set; }
		[Browsable(false)] public string SessionColorS { get { return Serialize.BrushToString(SessionColor); } set { SessionColor = Serialize.StringToBrush(value); } }
		[Display(Name="Session 线宽", GroupName="6. Session 视觉配置")] public int SessionRayWidth { get; set; }
		[Display(Name="Session 线型", GroupName="6. Session 视觉配置")] public DashStyleHelper SessionDashStyle { get; set; }
		[Display(Name="Session 保留天数", GroupName="7. Session 射线开关")] public int CustomRay_ExpireDays { get; set; }
		[Display(Name="Asia On", GroupName="7. Session 射线开关")] public bool Show_AS { get; set; }
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")] [Display(Name="Asia_Start", GroupName="7. Session 射线开关")] public DateTime Time_Asia_S { get; set; }
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")] [Display(Name="Asia_End", GroupName="7. Session 射线开关")] public DateTime Time_Asia_E { get; set; }
		[Display(Name="London On", GroupName="7. Session 射线开关")] public bool Show_LO { get; set; }
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")] [Display(Name="London_Start", GroupName="7. Session 射线开关")] public DateTime Time_London_S { get; set; }
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")] [Display(Name="London_End", GroupName="7. Session 射线开关")] public DateTime Time_London_E { get; set; }
		[Display(Name="NY AM On", GroupName="7. Session 射线开关")] public bool Show_NYAM { get; set; }
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")] [Display(Name="NYAM_Start", GroupName="7. Session 射线开关")] public DateTime Time_NYAM_S { get; set; }
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")] [Display(Name="NYAM_End", GroupName="7. Session 射线开关")] public DateTime Time_NYAM_E { get; set; }
		[Display(Name="NY Lunch On", GroupName="7. Session 射线开关")] public bool Show_NYL { get; set; }
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")] [Display(Name="NYL_Start", GroupName="7. Session 射线开关")] public DateTime Time_NYL_S { get; set; }
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")] [Display(Name="NYL_End", GroupName="7. Session 射线开关")] public DateTime Time_NYL_E { get; set; }
		[Display(Name="NY PM On", GroupName="7. Session 射线开关")] public bool Show_NYPM { get; set; }
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")] [Display(Name="NYPM_Start", GroupName="7. Session 射线开关")] public DateTime Time_NYPM_S { get; set; }
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")] [Display(Name="NYPM_End", GroupName="7. Session 射线开关")] public DateTime Time_NYPM_E { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ICT_HTF_Suite_Ultimate[] cacheICT_HTF_Suite_Ultimate;
		public ICT_HTF_Suite_Ultimate ICT_HTF_Suite_Ultimate()
		{
			return ICT_HTF_Suite_Ultimate(Input);
		}

		public ICT_HTF_Suite_Ultimate ICT_HTF_Suite_Ultimate(ISeries<double> input)
		{
			if (cacheICT_HTF_Suite_Ultimate != null)
				for (int idx = 0; idx < cacheICT_HTF_Suite_Ultimate.Length; idx++)
					if (cacheICT_HTF_Suite_Ultimate[idx] != null &&  cacheICT_HTF_Suite_Ultimate[idx].EqualsInput(input))
						return cacheICT_HTF_Suite_Ultimate[idx];
			return CacheIndicator<ICT_HTF_Suite_Ultimate>(new ICT_HTF_Suite_Ultimate(), input, ref cacheICT_HTF_Suite_Ultimate);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ICT_HTF_Suite_Ultimate ICT_HTF_Suite_Ultimate()
		{
			return indicator.ICT_HTF_Suite_Ultimate(Input);
		}

		public Indicators.ICT_HTF_Suite_Ultimate ICT_HTF_Suite_Ultimate(ISeries<double> input )
		{
			return indicator.ICT_HTF_Suite_Ultimate(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ICT_HTF_Suite_Ultimate ICT_HTF_Suite_Ultimate()
		{
			return indicator.ICT_HTF_Suite_Ultimate(Input);
		}

		public Indicators.ICT_HTF_Suite_Ultimate ICT_HTF_Suite_Ultimate(ISeries<double> input )
		{
			return indicator.ICT_HTF_Suite_Ultimate(input);
		}
	}
}

#endregion
