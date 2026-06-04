#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class TTFM_Child_Context_Reader : Strategy
	{
		private const string PanelTag = "TTFM_CHILD_CONTEXT_PANEL";
		private const string TSpotTag = "TTFM_CHILD_HTF_TSPOT";
		private const string BullRoiTag = "TTFM_CHILD_BULL_ROI";
		private const string BearRoiTag = "TTFM_CHILD_BEAR_ROI";
		private const string ProtectedTag = "TTFM_CHILD_PROTECTED";
		private const string InvalidationTag = "TTFM_CHILD_INVALIDATION";
		private const string ParentCandleTagPrefix = "TTFM_CHILD_PARENT_C";

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "TTFM_Child_Context_Reader";
				Description = "Reads HTF TTFM context from memory bus and projects facts onto child TF. Analysis only; no orders.";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = false;
				IsInstantiatedOnEachOptimizationIteration = false;
				BarsRequiredToTrade = 1;

				ParentContextKey = "MNQ_H1_TTFM_MAIN";
				ParentLabel = "H1";
				MaxParentAgeMinutes = 180;
				ShowPanel = true;
				DrawTSpot = true;
				DrawRoi = true;
				DrawProtectedLevels = true;
				DrawParentSequenceCandles = true;
				PublishChildWindowContext = true;
				ChildContextKey = string.Empty;
				ProjectionBars = 48;
				TSpotOpacity = 18;
				RoiOpacity = 14;
				ParentCandleOpacity = 16;
				ParentCandleOutlineOpacity = 80;
				ParentCandleBorderWidth = 1;
				TSpotBrush = Brushes.MediumSeaGreen;
				BullRoiBrush = Brushes.MediumAquamarine;
				BearRoiBrush = Brushes.LightCoral;
				ProtectedBrush = Brushes.Goldenrod;
				InvalidationBrush = Brushes.Crimson;
				ParentBullCandleBrush = Brushes.MediumSeaGreen;
				ParentBearCandleBrush = Brushes.IndianRed;
				ParentWickBrush = Brushes.DarkGray;
				PanelOpacity = 80;
				DebugPrint = false;
			}
			else if (State == State.Terminated)
			{
				TTFM_Context_Bus.Clear(ResolveChildContextKey(null));
				RemoveAllDrawObjects();
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0 || CurrentBar < BarsRequiredToTrade)
				return;

			TTFM_Context parent;
			bool found = TTFM_Context_Bus.TryGet(ResolveParentKey(), out parent);
			bool fresh = found && IsFresh(parent);

			if (!found || !fresh)
			{
				if (ShowPanel)
					DrawPanel(parent, found, fresh);
				else
					RemoveDrawObject(PanelTag);
				RemoveProjectionObjects();
				return;
			}

			EnrichParentWindowContext(parent);
			if (ShowPanel)
				DrawPanel(parent, found, fresh);
			else
				RemoveDrawObject(PanelTag);

			DrawParentFacts(parent);
			if (PublishChildWindowContext)
				TTFM_Context_Bus.Publish(parent.ChildContextKey, parent);

			if (DebugPrint)
				Print(Time[0].ToString("yyyy-MM-dd HH:mm:ss") + " TTFM_Child read " + parent.Key + " " + parent.ChildTfState + " C" + parent.CurrentCandleNumber);
		}

		private void DrawParentFacts(TTFM_Context parent)
		{
			if (DrawParentSequenceCandles)
				DrawParentSequenceWindows(parent);
			else
				RemoveParentSequenceWindows();

			if (DrawTSpot && IsValidPrice(parent.TSpotUpper) && IsValidPrice(parent.TSpotLower))
				DrawZone(TSpotTag, Math.Max(parent.TSpotUpper, parent.TSpotLower), Math.Min(parent.TSpotUpper, parent.TSpotLower), TSpotBrush, TSpotOpacity, ParentLabel + " T-Spot");
			else
				RemoveZoneObjects(TSpotTag);

			if (DrawRoi && IsValidPrice(parent.NearestBullZoneTop) && IsValidPrice(parent.NearestBullZoneBottom))
				DrawZone(BullRoiTag, GetParentProjectionStartTime(parent, parent.NearestBullZoneStartTime), Math.Max(parent.NearestBullZoneTop, parent.NearestBullZoneBottom), Math.Min(parent.NearestBullZoneTop, parent.NearestBullZoneBottom), parent.NearestBullZoneCE, BullRoiBrush, RoiOpacity, ParentLabel + " Bull ROI");
			else
				RemoveZoneObjects(BullRoiTag);

			if (DrawRoi && IsValidPrice(parent.NearestBearZoneTop) && IsValidPrice(parent.NearestBearZoneBottom))
				DrawZone(BearRoiTag, GetParentProjectionStartTime(parent, parent.NearestBearZoneStartTime), Math.Max(parent.NearestBearZoneTop, parent.NearestBearZoneBottom), Math.Min(parent.NearestBearZoneTop, parent.NearestBearZoneBottom), parent.NearestBearZoneCE, BearRoiBrush, RoiOpacity, ParentLabel + " Bear ROI");
			else
				RemoveZoneObjects(BearRoiTag);

			if (DrawProtectedLevels && IsValidPrice(parent.ProtectedSwing))
				DrawLevel(ProtectedTag, parent.ProtectedSwing, ProtectedBrush, ParentLabel + " Protected");
			else
				RemoveDrawObject(ProtectedTag);

			if (DrawProtectedLevels && IsValidPrice(parent.InvalidationPrice))
				DrawLevel(InvalidationTag, parent.InvalidationPrice, InvalidationBrush, ParentLabel + " Invalid");
			else
				RemoveDrawObject(InvalidationTag);
		}

		private void EnrichParentWindowContext(TTFM_Context parent)
		{
			if (parent == null)
				return;

			parent.ChildContextKey = ResolveChildContextKey(parent);
			parent.ChildTfMinutes = BarsPeriod != null ? Math.Max(1, BarsPeriod.Value) : 0;
			parent.ChildBarTime = Time[0];
			parent.ChildParentCandleNumber = 0;
			parent.ChildParentCandleStartTime = Core.Globals.MinDate;
			parent.ChildParentCandleEndTime = Core.Globals.MinDate;
			parent.ChildParentCandleOpen = double.NaN;
			parent.ChildParentCandleHigh = double.NaN;
			parent.ChildParentCandleLow = double.NaN;
			parent.ChildParentCandleClose = double.NaN;
			parent.IsInsideParentCandleWindow = false;

			for (int number = 1; number <= 6; number++)
			{
				DateTime start;
				DateTime end;
				double open;
				double high;
				double low;
				double close;
				if (!TryGetParentCandle(parent, number, out start, out end, out open, out high, out low, out close))
					continue;

				if (Time[0] >= start && Time[0] < end)
				{
					parent.ChildParentCandleNumber = number;
					parent.ChildParentCandleStartTime = start;
					parent.ChildParentCandleEndTime = end;
					parent.ChildParentCandleOpen = open;
					parent.ChildParentCandleHigh = high;
					parent.ChildParentCandleLow = low;
					parent.ChildParentCandleClose = close;
					parent.IsInsideParentCandleWindow = true;
					break;
				}
			}

			parent.ChildTfState = parent.IsInsideParentCandleWindow
				? "Inside " + ParentLabel + " C" + parent.ChildParentCandleNumber
				: "Outside " + ParentLabel + " C1-C6";

			UpdateAiSetupGate(parent);
		}

		private void UpdateAiSetupGate(TTFM_Context parent)
		{
			parent.AiInterfaceVersion = "TTFM-AI-1";
			parent.AiSetupGate = false;
			parent.AiSetupGateReason = string.Empty;

			if (!parent.IsInsideParentCandleWindow)
			{
				parent.AiSetupGateReason = "Blocked: child bar outside parent C1-C6 window.";
				return;
			}
			if (parent.SetupStatus == TTFMSetupStatus.Failed || parent.SetupStatus == TTFMSetupStatus.Completed)
			{
				parent.AiSetupGateReason = "Blocked: parent setup " + parent.SetupStatus + ".";
				return;
			}
			if (parent.AllowedDirection == ICTContextDirection.None)
			{
				parent.AiSetupGateReason = "Blocked: parent allowed direction is None.";
				return;
			}
			if (parent.ChildParentCandleNumber < 4 || parent.ChildParentCandleNumber > 6)
			{
				parent.AiSetupGateReason = "Watch: parent candle C" + parent.ChildParentCandleNumber + " is not an execution window.";
				return;
			}

			parent.AiSetupGate = true;
			parent.AiSetupGateReason = "Allowed: child bar inside parent C" + parent.ChildParentCandleNumber + " execution window.";
		}

		private void DrawParentSequenceWindows(TTFM_Context parent)
		{
			for (int number = 1; number <= 6; number++)
			{
				DateTime start;
				DateTime end;
				double open;
				double high;
				double low;
				double close;
				if (!TryGetParentCandle(parent, number, out start, out end, out open, out high, out low, out close))
				{
					RemoveParentCandleObjects(number);
					continue;
				}

				DrawParentCandle(number, start, end, open, high, low, close);
			}
		}

		private bool TryGetParentCandle(TTFM_Context parent, int number, out DateTime start, out DateTime end, out double open, out double high, out double low, out double close)
		{
			start = Core.Globals.MinDate;
			end = Core.Globals.MinDate;
			open = high = low = close = double.NaN;

			if (parent == null)
				return false;

			if (number == 1)
			{
				start = parent.C1Time;
				open = parent.C1Open;
				high = parent.C1High;
				low = parent.C1Low;
				close = parent.C1Close;
			}
			else if (number == 2)
			{
				start = parent.C2Time;
				open = parent.C2Open;
				high = parent.C2High;
				low = parent.C2Low;
				close = parent.C2Close;
			}
			else if (number == 3)
			{
				start = parent.C3Time;
				open = parent.C3Open;
				high = parent.C3High;
				low = parent.C3Low;
				close = parent.C3Close;
			}
			else if (number == 4)
			{
				start = parent.C4Time;
				open = parent.C4Open;
				high = parent.C4High;
				low = parent.C4Low;
				close = parent.C4Close;
			}
			else if (number == 5)
			{
				start = parent.C5Time;
				open = parent.C5Open;
				high = parent.C5High;
				low = parent.C5Low;
				close = parent.C5Close;
			}
			else if (number == 6)
			{
				start = parent.C6Time;
				open = parent.C6Open;
				high = parent.C6High;
				low = parent.C6Low;
				close = parent.C6Close;
			}

			if (start == Core.Globals.MinDate || !IsValidPrice(open) || !IsValidPrice(high) || !IsValidPrice(low) || !IsValidPrice(close))
				return false;

			end = GetParentCandleEnd(parent, number, start);
			return end > start;
		}

		private DateTime GetParentCandleEnd(TTFM_Context parent, int number, DateTime start)
		{
			DateTime next = GetParentCandleStart(parent, number + 1);
			if (next != Core.Globals.MinDate && next > start)
				return next;

			int minutes = ResolveParentTfMinutes(parent);
			return start.AddMinutes(Math.Max(1, minutes));
		}

		private DateTime GetParentCandleStart(TTFM_Context parent, int number)
		{
			if (parent == null)
				return Core.Globals.MinDate;
			if (number == 1)
				return parent.C1Time;
			if (number == 2)
				return parent.C2Time;
			if (number == 3)
				return parent.C3Time;
			if (number == 4)
				return parent.C4Time;
			if (number == 5)
				return parent.C5Time;
			if (number == 6)
				return parent.C6Time;
			return Core.Globals.MinDate;
		}

		private void DrawParentCandle(int number, DateTime start, DateTime end, double open, double high, double low, double close)
		{
			string prefix = ParentCandleTagPrefix + number;
			double bodyHigh = Math.Max(open, close);
			double bodyLow = Math.Min(open, close);
			Brush bodyBrush = close >= open ? ParentBullCandleBrush : ParentBearCandleBrush;
			Brush wickBrush = ParentWickBrush;
			Brush bodyBorder = ParentCandleBorderWidth == 0 ? Brushes.Transparent : bodyBrush;
			Brush wickBorder = ParentCandleBorderWidth == 0 ? Brushes.Transparent : wickBrush;

			DrawCandleRectangle(prefix + "_WICK", start, high, end, bodyHigh, wickBorder, wickBrush, ParentCandleOpacity);
			DrawCandleRectangle(prefix + "_TAIL", start, bodyLow, end, low, wickBorder, wickBrush, ParentCandleOpacity);
			DrawCandleRectangle(prefix + "_BODY", start, bodyHigh, end, bodyLow, bodyBorder, bodyBrush, ParentCandleOpacity);
			Draw.Text(this, prefix + "_LABEL", false, ParentLabel + " C" + number, end, high, 0, bodyBrush, new SimpleFont("Arial", 10), System.Windows.TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void DrawCandleRectangle(string tag, DateTime start, double upper, DateTime end, double lower, Brush outline, Brush fill, int opacity)
		{
			var rectangle = Draw.Rectangle(this, tag, false, start, Math.Max(upper, lower), end, Math.Min(upper, lower), outline, fill, Math.Max(0, Math.Min(100, opacity)));
			rectangle.OutlineStroke.Width = Math.Max(0, ParentCandleBorderWidth);
			rectangle.OutlineStroke.Opacity = Math.Max(0, Math.Min(100, ParentCandleOutlineOpacity));
		}

		private void DrawZone(string tag, double upper, double lower, Brush brush, int opacity, string label)
		{
			DrawZone(tag, Time[0], upper, lower, brush, opacity, label);
		}

		private void DrawZone(string tag, DateTime start, double upper, double lower, Brush brush, int opacity, string label)
		{
			DrawZone(tag, start, upper, lower, double.NaN, brush, opacity, label);
		}

		private void DrawZone(string tag, DateTime start, double upper, double lower, double cePrice, Brush brush, int opacity, string label)
		{
			DateTime end = GetProjectionEndTime();
			Draw.Rectangle(this, tag, false, start, upper, end, lower, brush, brush, Math.Max(0, Math.Min(100, opacity)));
			if (IsValidPrice(cePrice))
				Draw.Line(this, tag + "_CE", false, start, cePrice, end, cePrice, brush, DashStyleHelper.Dash, 2);
			else
				RemoveDrawObject(tag + "_CE");
			Draw.Text(this, tag + "_LABEL", false, label + " " + FormatZone(upper, lower), end, (upper + lower) * 0.5, 0, brush, new SimpleFont("Arial", 10), System.Windows.TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void RemoveZoneObjects(string tag)
		{
			RemoveDrawObject(tag);
			RemoveDrawObject(tag + "_CE");
			RemoveDrawObject(tag + "_LABEL");
		}

		private DateTime GetParentProjectionStartTime(TTFM_Context parent, DateTime preferredStart)
		{
			if (preferredStart != Core.Globals.MinDate)
				return preferredStart;
			if (parent != null && parent.ChildParentCandleStartTime != Core.Globals.MinDate)
				return parent.ChildParentCandleStartTime;
			if (parent != null && parent.BarTime != Core.Globals.MinDate)
				return parent.BarTime;
			return Time[0];
		}

		private void DrawLevel(string tag, double price, Brush brush, string label)
		{
			DateTime start = Time[0];
			DateTime end = GetProjectionEndTime();
			Draw.Line(this, tag, false, start, price, end, price, brush, DashStyleHelper.Dash, 2);
			Draw.Text(this, tag + "_LABEL", false, label + " " + FormatPrice(price), end, price, 0, brush, new SimpleFont("Arial", 10), System.Windows.TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private DateTime GetProjectionEndTime()
		{
			double minutes = BarsPeriod != null ? Math.Max(1, BarsPeriod.Value) : 5;
			return Time[0].AddMinutes(minutes * Math.Max(1, ProjectionBars));
		}

		private void DrawPanel(TTFM_Context parent, bool found, bool fresh)
		{
			string text;
			if (!found)
			{
				text = "TTFM Child Context\n" +
					"Parent: " + ResolveParentKey() + "\n" +
					"State: missing bus context\n" +
					"Bus keys: " + FormatBusKeys();
			}
			else
			{
				text = "TTFM Child Context\n" +
					"Parent: " + parent.Key + " | " + ParentLabel + " | Fresh: " + fresh + "\n" +
					"ChildState: " + parent.ChildTfState + " | Allow: " + parent.AllowedDirection + "\n" +
					"Bias: " + DirectionText(parent.Bias) + " | Status: " + parent.SetupStatus + " | C#: " + parent.CurrentCandleNumber + " | Conf: " + parent.Confidence + "\n" +
					"Phase: " + parent.SetupPhase + "\n" +
					"Window: C" + parent.ChildParentCandleNumber + " " + parent.ChildParentCandleStartTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " -> " + parent.ChildParentCandleEndTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "\n" +
					"AI Gate: " + parent.AiSetupGate + " | " + parent.AiSetupGateReason + "\n" +
					"Bar: " + parent.BarTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " | AgeMin: " + AgeMinutes(parent) + "\n" +
					"T-Spot: " + FormatZone(parent.TSpotUpper, parent.TSpotLower) + " | Touched: " + parent.TSpotTouched + " | Violated: " + parent.TSpotViolated + "\n" +
					"Protected/Invalid: " + FormatPrice(parent.ProtectedSwing) + " / " + FormatPrice(parent.InvalidationPrice) + "\n" +
					"ROI: " + parent.RoiSource + " " + parent.RoiHtfMinutes + "m bias " + parent.RoiActiveBias + "\n" +
					"Bull ROI: " + FormatZone(parent.NearestBullZoneTop, parent.NearestBullZoneBottom) + " CE " + FormatPrice(parent.NearestBullZoneCE) + " @ " + FormatTime(parent.NearestBullZoneStartTime) + "\n" +
					"Bear ROI: " + FormatZone(parent.NearestBearZoneTop, parent.NearestBearZoneBottom) + " CE " + FormatPrice(parent.NearestBearZoneCE) + " @ " + FormatTime(parent.NearestBearZoneStartTime);
			}

			Draw.TextFixed(this, PanelTag, text, TextPosition.TopLeft, Brushes.WhiteSmoke, new SimpleFont("Consolas", 12), Brushes.DimGray, Brushes.Black, Math.Max(0, Math.Min(100, PanelOpacity)));
		}

		private bool IsFresh(TTFM_Context parent)
		{
			if (parent == null || parent.BarTime == Core.Globals.MinDate)
				return false;

			double maxAge = Math.Max(1, MaxParentAgeMinutes);
			return Math.Abs((Time[0] - parent.BarTime).TotalMinutes) <= maxAge;
		}

		private int AgeMinutes(TTFM_Context parent)
		{
			if (parent == null || parent.BarTime == Core.Globals.MinDate)
				return -1;
			return (int)Math.Round((Time[0] - parent.BarTime).TotalMinutes);
		}

		private string ResolveParentKey()
		{
			return string.IsNullOrWhiteSpace(ParentContextKey) ? string.Empty : ParentContextKey.Trim();
		}

		private string ResolveChildContextKey(TTFM_Context parent)
		{
			if (!string.IsNullOrWhiteSpace(ChildContextKey))
				return ChildContextKey.Trim();

			string parentKey = parent != null && !string.IsNullOrWhiteSpace(parent.Key) ? parent.Key : ResolveParentKey();
			int childMinutes = BarsPeriod != null ? Math.Max(1, BarsPeriod.Value) : 0;
			return parentKey + "_CHILD_" + childMinutes + "M";
		}

		private int ResolveParentTfMinutes(TTFM_Context parent)
		{
			if (parent != null && parent.ContextTfMinutes > 0)
				return parent.ContextTfMinutes;

			string key = ResolveParentKey().ToUpperInvariant();
			string label = string.IsNullOrWhiteSpace(ParentLabel) ? string.Empty : ParentLabel.Trim().ToUpperInvariant();
			string text = key + " " + label;
			if (text.Contains("H4"))
				return 240;
			if (text.Contains("H1"))
				return 60;
			if (text.Contains("M15"))
				return 15;
			if (text.Contains("M5"))
				return 5;
			if (text.Contains("M1"))
				return 1;
			return Math.Max(1, MaxParentAgeMinutes);
		}

		private string FormatBusKeys()
		{
			string[] keys = TTFM_Context_Bus.GetKeys();
			if (keys == null || keys.Length == 0)
				return "-";
			return string.Join(",", keys);
		}

		private string DirectionText(int direction)
		{
			if (direction > 0)
				return "Bullish";
			if (direction < 0)
				return "Bearish";
			return "Neutral";
		}

		private bool IsValidPrice(double price)
		{
			return price > 0 && !double.IsNaN(price) && !double.IsInfinity(price);
		}

		private string FormatPrice(double price)
		{
			if (!IsValidPrice(price))
				return "-";
			return Instrument.MasterInstrument.FormatPrice(price);
		}

		private string FormatTime(DateTime time)
		{
			return time == Core.Globals.MinDate ? "-" : time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
		}

		private string FormatZone(double top, double bottom)
		{
			if (!IsValidPrice(top) || !IsValidPrice(bottom))
				return "-";
			return FormatPrice(Math.Max(top, bottom)) + "/" + FormatPrice(Math.Min(top, bottom));
		}

		private void RemoveProjectionObjects()
		{
			foreach (string tag in new string[] { TSpotTag, BullRoiTag, BearRoiTag, ProtectedTag, InvalidationTag })
			{
				RemoveDrawObject(tag);
				RemoveDrawObject(tag + "_LABEL");
			}
			RemoveParentSequenceWindows();
		}

		private void RemoveAllDrawObjects()
		{
			RemoveDrawObject(PanelTag);
			RemoveProjectionObjects();
		}

		private void RemoveParentSequenceWindows()
		{
			for (int number = 1; number <= 6; number++)
				RemoveParentCandleObjects(number);
		}

		private void RemoveParentCandleObjects(int number)
		{
			string prefix = ParentCandleTagPrefix + number;
			RemoveDrawObject(prefix + "_WICK");
			RemoveDrawObject(prefix + "_TAIL");
			RemoveDrawObject(prefix + "_BODY");
			RemoveDrawObject(prefix + "_LABEL");
		}

		[NinjaScriptProperty]
		[Display(Name = "Parent Context Key", GroupName = "01. Parent", Order = 0)]
		public string ParentContextKey { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Parent Label", GroupName = "01. Parent", Order = 1)]
		public string ParentLabel { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1440)]
		[Display(Name = "Max Parent Age Minutes", GroupName = "01. Parent", Order = 2)]
		public int MaxParentAgeMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Panel", GroupName = "02. Visual", Order = 0)]
		public bool ShowPanel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw T-Spot", GroupName = "02. Visual", Order = 1)]
		public bool DrawTSpot { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw ROI", GroupName = "02. Visual", Order = 2)]
		public bool DrawRoi { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw Protected Levels", GroupName = "02. Visual", Order = 3)]
		public bool DrawProtectedLevels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw Parent Sequence Candles", GroupName = "02. Visual", Order = 4)]
		public bool DrawParentSequenceCandles { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Publish Child Window Context", GroupName = "01. Parent", Order = 3)]
		public bool PublishChildWindowContext { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Child Context Key", GroupName = "01. Parent", Order = 4)]
		public string ChildContextKey { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Projection Bars", GroupName = "02. Visual", Order = 5)]
		public int ProjectionBars { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "T-Spot Opacity", GroupName = "02. Visual", Order = 6)]
		public int TSpotOpacity { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "ROI Opacity", GroupName = "02. Visual", Order = 7)]
		public int RoiOpacity { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Parent Candle Opacity", GroupName = "02. Visual", Order = 8)]
		public int ParentCandleOpacity { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Parent Candle Outline Opacity", GroupName = "02. Visual", Order = 9)]
		public int ParentCandleOutlineOpacity { get; set; }

		[NinjaScriptProperty]
		[Range(0, 5)]
		[Display(Name = "Parent Candle Border Width", GroupName = "02. Visual", Order = 10)]
		public int ParentCandleBorderWidth { get; set; }

		[XmlIgnore]
		[Display(Name = "T-Spot Color", GroupName = "02. Visual", Order = 11)]
		public Brush TSpotBrush { get; set; }
		[Browsable(false)]
		public string TSpotBrushSerializable { get { return Serialize.BrushToString(TSpotBrush); } set { TSpotBrush = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Bull ROI Color", GroupName = "02. Visual", Order = 12)]
		public Brush BullRoiBrush { get; set; }
		[Browsable(false)]
		public string BullRoiBrushSerializable { get { return Serialize.BrushToString(BullRoiBrush); } set { BullRoiBrush = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Bear ROI Color", GroupName = "02. Visual", Order = 13)]
		public Brush BearRoiBrush { get; set; }
		[Browsable(false)]
		public string BearRoiBrushSerializable { get { return Serialize.BrushToString(BearRoiBrush); } set { BearRoiBrush = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Protected Color", GroupName = "02. Visual", Order = 14)]
		public Brush ProtectedBrush { get; set; }
		[Browsable(false)]
		public string ProtectedBrushSerializable { get { return Serialize.BrushToString(ProtectedBrush); } set { ProtectedBrush = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Invalidation Color", GroupName = "02. Visual", Order = 15)]
		public Brush InvalidationBrush { get; set; }
		[Browsable(false)]
		public string InvalidationBrushSerializable { get { return Serialize.BrushToString(InvalidationBrush); } set { InvalidationBrush = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Parent Bull Candle Color", GroupName = "02. Visual", Order = 16)]
		public Brush ParentBullCandleBrush { get; set; }
		[Browsable(false)]
		public string ParentBullCandleBrushSerializable { get { return Serialize.BrushToString(ParentBullCandleBrush); } set { ParentBullCandleBrush = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Parent Bear Candle Color", GroupName = "02. Visual", Order = 17)]
		public Brush ParentBearCandleBrush { get; set; }
		[Browsable(false)]
		public string ParentBearCandleBrushSerializable { get { return Serialize.BrushToString(ParentBearCandleBrush); } set { ParentBearCandleBrush = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Parent Wick Color", GroupName = "02. Visual", Order = 18)]
		public Brush ParentWickBrush { get; set; }
		[Browsable(false)]
		public string ParentWickBrushSerializable { get { return Serialize.BrushToString(ParentWickBrush); } set { ParentWickBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Panel Opacity", GroupName = "02. Visual", Order = 19)]
		public int PanelOpacity { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Debug Print", GroupName = "03. Debug", Order = 0)]
		public bool DebugPrint { get; set; }
	}
}
