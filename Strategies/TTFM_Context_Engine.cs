#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Gui;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class TTFM_Context_Engine : Strategy
	{
		private ICT_HTF_PDA_Projector htfPdaProjector;
		private int setupId;
		private int setupDirection;
		private int c1BarIndex = -1;
		private int c2BarIndex = -1;
		private int c3BarIndex = -1;
		private int failureBarIndex = -1;
		private TTFMSetupStatus fractalStatus = TTFMSetupStatus.Forming;
		private string setupPhase = "Waiting C2";
		private string failureReason = string.Empty;
		private readonly HashSet<string> drawnFractalTags = new HashSet<string>();
		private bool legacyFractalTagsScrubbed;
		private int cisdBarsInProgress = -1;
		private int cisdHtfBarsInProgress = -1;
		private int cisdConfirmedPrimaryBar = -1;
		private int cisdConfirmedSeriesBar = -1;
		private int cisdDirection;
		private int cisdRunDirection;
		private double cisdPendingLevel = double.NaN;
		private int cisdPendingDirection;
		private double cisdPendingBullLevel = double.NaN;
		private double cisdPendingBearLevel = double.NaN;
		private DateTime cisdPendingBullTime = Core.Globals.MinDate;
		private DateTime cisdPendingBearTime = Core.Globals.MinDate;
		private double cisdConfirmedLevel = double.NaN;
		private DateTime cisdPendingTime = Core.Globals.MinDate;
		private DateTime cisdConfirmedTime = Core.Globals.MinDate;
		private DateTime cisdLastStoredClosedTime = Core.Globals.MinDate;
		private readonly List<CisdCandle> cisdHistory = new List<CisdCandle>();
		private double c1Open = double.NaN, c1High = double.NaN, c1Low = double.NaN, c1Close = double.NaN;
		private double c2Open = double.NaN, c2High = double.NaN, c2Low = double.NaN, c2Close = double.NaN;
		private double c3Open = double.NaN, c3High = double.NaN, c3Low = double.NaN, c3Close = double.NaN;
		private DateTime c1Time = Core.Globals.MinDate, c2Time = Core.Globals.MinDate, c3Time = Core.Globals.MinDate;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "TTFM_Context_Engine";
				Description = "TTFM fractal context publisher. Analysis only; no orders.";
				Calculate = Calculate.OnEachTick;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = false;
				IsInstantiatedOnEachOptimizationIteration = false;
				BarsRequiredToTrade = 50;

				ContextKey = string.Empty;
				Profile = "D1-H1-M5-M1";
				ExternalBias = 0;
				UseHtfPdaProjector = true;
				HtfPdaProjectorMinutes = 60;
				ShowHtfPdaProjectorVisuals = false;
				FvgLookbackBars = 80;
				MinDisplacementBodyTicks = 20;
				DojiBodyMaxTicks = 2;
				DojiBodyMaxPercent = 5.0;
				DojiBodyProportionSize = 9;
				DojiLongWickProportion = 10;
				C2SweepLookbackBars = 3;
				RequireStrongC3Close = true;
				MaxBarsToWaitC3 = 3;
				MaxC4TrackingBars = 12;
				UseCisd = true;
				CisdMinutes = 15;
				ShowCisdLine = true;
				CisdLineProjectionBars = 5;
				PendingCisdLineBrush = Brushes.Gray;
				PendingCisdTextBrush = Brushes.Black;
				BullCisdBrush = Brushes.Blue;
				BearCisdBrush = Brushes.Red;
				PendingCisdLineWidth = 1;
				ConfirmedCisdLineWidth = 2;
				ShowPanel = true;
				DrawTSpotZone = true;
				ShowCLabels = true;
				CLabelLookbackBars = 80;
				CLabelTickOffset = 20;
				TSpotOpacity = 18;
				TSpotStartOffsetBars = 0;
				TSpotProjectionBars = 2;
				BullTSpotBrush = Brushes.MediumSeaGreen;
				BearTSpotBrush = Brushes.IndianRed;
				ExportContextSnapshot = true;
				ContextSnapshotFile = string.Empty;
				DebugPrint = false;
			}
			else if (State == State.Configure)
			{
				Calculate = Calculate.OnEachTick;

				if (UseCisd)
				{
					AddDataSeries(BarsPeriodType.Minute, Math.Max(1, CisdMinutes));
					cisdBarsInProgress = 1;
					AddDataSeries(BarsPeriodType.Minute, Math.Max(Math.Max(1, CisdMinutes), Math.Max(5, HtfPdaProjectorMinutes)));
					cisdHtfBarsInProgress = 2;
				}
			}
			else if (State == State.DataLoaded)
			{
				if (UseHtfPdaProjector)
				{
					try
					{
						htfPdaProjector = ICT_HTF_PDA_Projector(
							Math.Max(5, HtfPdaProjectorMinutes),
							12,
							2,
							false,
							PDAFillType.CLOSE_THROUGH,
							true,
							24,
							ShowHtfPdaProjectorVisuals,
							ShowHtfPdaProjectorVisuals,
							true,
							ShowHtfPdaProjectorVisuals,
							22,
							8,
							2,
							new SimpleFont("Arial", 10),
							Brushes.MediumAquamarine,
							Brushes.LightCoral,
							Brushes.Black,
							Brushes.Black);

						if (ShowHtfPdaProjectorVisuals)
							AddChartIndicator(htfPdaProjector);
					}
					catch (Exception ex)
					{
						htfPdaProjector = null;
						if (DebugPrint)
							Print("TTFM_Context HTF PDA projector init failed: " + ex.Message);
					}
				}
			}
			else if (State == State.Terminated)
			{
				TTFM_Context_Bus.Clear(ResolveContextKey());
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress == cisdBarsInProgress)
			{
				UpdateCisdSeries();
				return;
			}
			if (BarsInProgress == cisdHtfBarsInProgress)
				return;

			if (BarsInProgress != 0 || CurrentBar < BarsRequiredToTrade || CurrentBar < 3)
				return;

			UpdateCisdFromPrimaryBar();

			TTFM_Context context = BuildContext();
			TTFM_Context_Bus.Publish(context.Key, context);

			if (ShowPanel)
				DrawPanel(context);
			else
				RemoveDrawObject("TTFM_CONTEXT_PANEL");

			if (DrawTSpotZone)
				DrawTSpot(context);
			else
				RemoveDrawObject("TTFM_TSPOT");

			if (ShowCLabels)
				DrawFractalLabels(context);
			else
				RemoveFractalLabels();

			if (ShowCisdLine)
				DrawCisd(context);
			else
				RemoveCisdDrawObjects();

			if (ExportContextSnapshot)
				WriteContextSnapshot(context);

			if (DebugPrint)
			{
				Print(Time[0].ToString("yyyy-MM-dd HH:mm:ss") + " TTFM_Context published bus key=" + context.Key + " keys=" + string.Join(",", TTFM_Context_Bus.GetKeys()));
				Print(Time[0].ToString("yyyy-MM-dd HH:mm:ss") + " TTFM_Context " + context.Narrative);
			}
		}

		private TTFM_Context BuildContext()
		{
			TTFM_Context context = new TTFM_Context();
			context.Key = ResolveContextKey();
			context.Instrument = Instrument.FullName;
			context.Profile = string.IsNullOrWhiteSpace(Profile) ? "D1-H1-M5-M1" : Profile.Trim();
			context.ContextTfMinutes = BarsPeriod != null ? Math.Max(1, BarsPeriod.Value) : 0;
			context.PublishedAt = DateTime.Now;
			context.BarTime = Time[0];
			context.CurrentPrice = Close[0];

			UpdateFractalState();
			PopulateCandlesFromState(context);
			ComputeTSpot(context);
			ComputeSequenceFacts(context);
			MergeHtfPdaProjectorZones(context);
			FindNearestFvgZones(context);
			ScoreAndState(context);
			BuildNarrative(context);
			return context;
		}

		private void UpdateFractalState()
		{
			if (fractalStatus == TTFMSetupStatus.Failed)
			{
				ResetSetup();
				DetectC2OnCurrentBar();
				return;
			}
			else if (fractalStatus == TTFMSetupStatus.Completed)
			{
				ResetSetup();
				DetectC2OnCurrentBar();
				return;
			}

			if (c3BarIndex >= 0 && CurrentBar - c3BarIndex > Math.Max(1, MaxC4TrackingBars))
			{
				ResetSetup();
				DetectC2OnCurrentBar();
				return;
			}

			if (c2BarIndex >= 0 && c3BarIndex < 0)
			{
				UpdateWaitingC3();
				return;
			}

			if (c2BarIndex >= 0 && c3BarIndex >= 0)
			{
				UpdateC4Delivery();
				return;
			}

			DetectC2OnCurrentBar();
		}

		private void DetectC2OnCurrentBar()
		{
			if (CurrentBar < 1)
				return;

			bool bullC2 = IsBullishC2At(0);
			bool bearC2 = IsBearishC2At(0);
			if (bullC2 == bearC2)
			{
				fractalStatus = TTFMSetupStatus.Forming;
				setupPhase = "Waiting C2";
				return;
			}

			setupId++;
			setupDirection = bullC2 ? 1 : -1;
			c1BarIndex = CurrentBar - 1;
			c2BarIndex = CurrentBar;
			c3BarIndex = -1;
			failureBarIndex = -1;
			failureReason = string.Empty;

			LockCandle(1, out c1Open, out c1High, out c1Low, out c1Close, out c1Time);
			LockCandle(0, out c2Open, out c2High, out c2Low, out c2Close, out c2Time);

			fractalStatus = TTFMSetupStatus.WaitingC3;
			setupPhase = setupDirection > 0 ? "Bullish C2; waiting C3" : "Bearish C2; waiting C3";
		}

		private void UpdateWaitingC3()
		{
			int age = CurrentBar - c2BarIndex;
			if (age <= 0)
				return;

			bool refreshedBullC2 = setupDirection > 0 && IsBullishC2At(0);
			bool refreshedBearC2 = setupDirection < 0 && IsBearishC2At(0);
			if (refreshedBullC2 || refreshedBearC2)
			{
				c1BarIndex = CurrentBar - 1;
				c2BarIndex = CurrentBar;
				LockCandle(1, out c1Open, out c1High, out c1Low, out c1Close, out c1Time);
				LockCandle(0, out c2Open, out c2High, out c2Low, out c2Close, out c2Time);
				fractalStatus = TTFMSetupStatus.WaitingC3;
				setupPhase = setupDirection > 0 ? "Bullish C2 refreshed; waiting C3" : "Bearish C2 refreshed; waiting C3";
				return;
			}

			bool bullExpansion = setupDirection > 0 && IsBullExpansion(c2Low, c2High, Low[0], Close[0], RequireStrongC3Close);
			bool bearExpansion = setupDirection < 0 && IsBearExpansion(c2Low, c2High, High[0], Close[0], RequireStrongC3Close);
			if (bullExpansion || bearExpansion)
			{
				c3BarIndex = CurrentBar;
				LockCandle(0, out c3Open, out c3High, out c3Low, out c3Close, out c3Time);

				fractalStatus = TTFMSetupStatus.Valid;
				setupPhase = setupDirection > 0 ? "Bullish C3 confirmed" : "Bearish C3 confirmed";
				return;
			}

			bool invalidated = IsProtectedSwingBrokenByClosedBar();
			if (invalidated)
			{
				FailSetup("C2 protected swing broken before C3");
				return;
			}

			fractalStatus = TTFMSetupStatus.WaitingC3;
			setupPhase = "Waiting C3, age " + age;
		}

		private void UpdateC4Delivery()
		{
			int age = CurrentBar - c3BarIndex;
			if (age <= 0)
			{
				fractalStatus = TTFMSetupStatus.Valid;
				setupPhase = setupDirection > 0 ? "Bullish C3 confirmed" : "Bearish C3 confirmed";
				return;
			}

			if (TryPromoteC3ToC2OnC4FailedContinuation(age))
				return;

			bool invalidated = IsProtectedSwingBrokenByClosedBar();
			if (invalidated)
			{
				FailSetup("Protected swing broken");
				return;
			}

			double c3Eq = (c3High + c3Low) * 0.5;
			bool crossedEq = setupDirection > 0 ? Low[0] < c3Eq : High[0] > c3Eq;
			bool touchedTSpot = setupDirection > 0
				? IsBetween(Low[0], c3Eq, c3High)
				: IsBetween(High[0], c3Low, c3Eq);
			int candleNumber = Math.Min(6, 3 + age);

			if (age > 3)
			{
				fractalStatus = TTFMSetupStatus.Completed;
				setupPhase = "C6 completed";
				return;
			}

			if (crossedEq)
			{
				fractalStatus = TTFMSetupStatus.Paused;
				setupPhase = "C" + candleNumber + " T-Spot violated";
			}
			else
			{
				fractalStatus = TTFMSetupStatus.Valid;
				if (touchedTSpot)
					setupPhase = "C" + candleNumber + " tagged T-Spot";
				else if (age == 1)
					setupPhase = "C4 respecting T-Spot";
				else if (age <= 3)
					setupPhase = "C" + candleNumber + " continuation";
			}
		}

		private bool TryPromoteC3ToC2OnC4FailedContinuation(int age)
		{
			if (age != 1 || c2BarIndex < 0 || c3BarIndex < 0)
				return false;

			bool failedContinuation = IsC4FailedContinuation(setupDirection, High[0], Low[0], c3High, c3Low);
			if (!failedContinuation)
				return false;

			c1BarIndex = c2BarIndex;
			c2BarIndex = c3BarIndex;
			c3BarIndex = -1;
			failureBarIndex = -1;
			failureReason = string.Empty;

			c1Open = c2Open;
			c1High = c2High;
			c1Low = c2Low;
			c1Close = c2Close;
			c1Time = c2Time;

			c2Open = c3Open;
			c2High = c3High;
			c2Low = c3Low;
			c2Close = c3Close;
			c2Time = c3Time;

			c3Open = double.NaN;
			c3High = double.NaN;
			c3Low = double.NaN;
			c3Close = double.NaN;
			c3Time = Core.Globals.MinDate;

			setupId++;
			fractalStatus = TTFMSetupStatus.WaitingC3;
			setupPhase = "C4 failed continuation; C3 promoted to C2";
			return true;
		}

		private bool IsC4FailedContinuation(int direction, double candidateHigh, double candidateLow, double referenceHigh, double referenceLow)
		{
			return direction > 0
				? candidateLow < referenceLow && candidateHigh <= referenceHigh
				: candidateHigh > referenceHigh && candidateLow >= referenceLow;
		}

		private void UpdateCisdSeries()
		{
			if (!UseCisd || cisdBarsInProgress < 0)
				return;
			if (CurrentBars == null || CurrentBars.Length <= cisdBarsInProgress || CurrentBars[cisdBarsInProgress] < 1)
				return;
			if (cisdHtfBarsInProgress >= 0 && (CurrentBars.Length <= cisdHtfBarsInProgress || CurrentBars[cisdHtfBarsInProgress] < 1))
				return;

			StoreClosedCisdCandle();
			ConfirmPendingCisdOnClosedCtfBar();
			UpdateCisdControlFromPine();
		}

		private void SeedCisdFromPrimaryLookback(int maxBars)
		{
			if (!UseCisd)
				return;

			int max = Math.Min(CurrentBar, Math.Max(1, maxBars));
			for (int barsAgo = 0; barsAgo <= max; barsAgo++)
			{
				int candleDirection = GetCandleDirection(Open[barsAgo], High[barsAgo], Low[barsAgo], Close[barsAgo]);
				if (candleDirection == 0)
					continue;

				cisdPendingLevel = Open[barsAgo];
				cisdPendingTime = Time[barsAgo];
				cisdRunDirection = candleDirection;

				for (int runBarsAgo = barsAgo + 1; runBarsAgo <= max; runBarsAgo++)
				{
					if (GetCandleDirection(Open[runBarsAgo], High[runBarsAgo], Low[runBarsAgo], Close[runBarsAgo]) != candleDirection)
						break;

					if (candleDirection > 0 && Open[runBarsAgo] < cisdPendingLevel)
					{
						cisdPendingLevel = Open[runBarsAgo];
						cisdPendingTime = Time[runBarsAgo];
					}
					else if (candleDirection < 0 && Open[runBarsAgo] > cisdPendingLevel)
					{
						cisdPendingLevel = Open[runBarsAgo];
						cisdPendingTime = Time[runBarsAgo];
					}
				}
				return;
			}
		}

		private void UpdateCisdFromPrimaryBar()
		{
			if (!UseCisd || cisdBarsInProgress >= 0)
				return;

			ProcessCisdCandle(Open[0], High[0], Low[0], Close[0], Time[0]);
		}

		private void StoreClosedCisdCandle()
		{
			if (!IsFirstTickOfBar || CurrentBars[cisdBarsInProgress] < 1)
				return;

			DateTime closedTime = Times[cisdBarsInProgress][1];
			if (closedTime == cisdLastStoredClosedTime)
				return;

			CisdCandle candle = new CisdCandle
			{
				Open = Opens[cisdBarsInProgress][1],
				High = Highs[cisdBarsInProgress][1],
				Low = Lows[cisdBarsInProgress][1],
				Close = Closes[cisdBarsInProgress][1],
				Time = closedTime
			};
			candle.Direction = GetCandleDirection(candle.Open, candle.High, candle.Low, candle.Close);
			cisdHistory.Insert(0, candle);
			while (cisdHistory.Count > 100)
				cisdHistory.RemoveAt(cisdHistory.Count - 1);

			cisdLastStoredClosedTime = closedTime;
		}

		private void UpdateCisdControlFromPine()
		{
			double mo0 = Opens[cisdBarsInProgress][0];
			double mh0 = Highs[cisdBarsInProgress][0];
			double ml0 = Lows[cisdBarsInProgress][0];
			double mc0 = Closes[cisdBarsInProgress][0];
			DateTime mt0 = Times[cisdBarsInProgress][0];
			double mh1 = Highs[cisdBarsInProgress][1];
			double ml1 = Lows[cisdBarsInProgress][1];

			double htfHigh0 = Highs[cisdHtfBarsInProgress][0];
			double htfLow0 = Lows[cisdHtfBarsInProgress][0];

			int currentDirection = mc0 > mo0 ? 1 : (mc0 < mo0 ? -1 : 0);
			int previousDirection = GetCandleDirection(
				Opens[cisdBarsInProgress][1],
				Highs[cisdBarsInProgress][1],
				Lows[cisdBarsInProgress][1],
				Closes[cisdBarsInProgress][1]);

			if (SamePrice(mh0, htfHigh0) && SamePrice(Highs[cisdBarsInProgress][0], mh0) && mh0 >= mh1)
				UpdateBearishCisdControl(mo0, mh0, ml0, mc0, mt0, previousDirection, currentDirection);

			if (SamePrice(ml0, htfLow0) && SamePrice(Lows[cisdBarsInProgress][0], ml0) && ml0 <= ml1)
				UpdateBullishCisdControl(mo0, mh0, ml0, mc0, mt0, previousDirection, currentDirection);

			RefreshSelectedPendingCisd();
		}

		private void UpdateBearishCisdControl(double open, double high, double low, double close, DateTime time, int previousDirection, int currentDirection)
		{
			if (previousDirection <= 0 && currentDirection > 0)
			{
				SetPendingCisd(-1, open, time);
				return;
			}

			if (previousDirection <= 0 && currentDirection > 0 || cisdHistory.Count <= 15)
				return;

			for (int i = 1; i <= 15 && i < cisdHistory.Count; i++)
			{
				if (cisdHistory[i].High > high)
					break;

				if (cisdHistory[i].Direction <= 0 && cisdHistory[i - 1].Direction > 0)
				{
					int ybar = i - 1;
					double level = cisdHistory[ybar].Open;
					DateTime levelTime = cisdHistory[ybar].Time;

					for (int j = ybar; j >= 0; j--)
					{
						if (cisdHistory[j].Open < level && cisdHistory[j].Direction > 0)
						{
							level = cisdHistory[j].Open;
							levelTime = cisdHistory[j].Time;
						}
					}

					if (level > open && !(close < open))
					{
						level = open;
						levelTime = time;
					}
					if (level > open && close < open)
					{
						level = low;
						levelTime = time;
					}

					SetPendingCisd(-1, level, levelTime);
					break;
				}
			}
		}

		private void UpdateBullishCisdControl(double open, double high, double low, double close, DateTime time, int previousDirection, int currentDirection)
		{
			if (previousDirection >= 0 && currentDirection < 0)
			{
				SetPendingCisd(1, open, time);
				return;
			}

			if (previousDirection >= 0 && currentDirection < 0 || cisdHistory.Count <= 15)
				return;

			for (int i = 1; i <= 15 && i < cisdHistory.Count; i++)
			{
				if (cisdHistory[i].Low < low)
					break;

				if (cisdHistory[i].Direction >= 0 && cisdHistory[i - 1].Direction < 0)
				{
					int xbar = i - 1;
					double level = cisdHistory[xbar].Open;
					DateTime levelTime = cisdHistory[xbar].Time;

					for (int j = xbar; j >= 0; j--)
					{
						if (cisdHistory[j].Open > level && cisdHistory[j].Direction < 0)
						{
							level = cisdHistory[j].Open;
							levelTime = cisdHistory[j].Time;
						}
					}

					if (level < open && !(close > open))
					{
						level = open;
						levelTime = time;
					}
					if (level < open && close > open)
					{
						level = high;
						levelTime = time;
					}

					SetPendingCisd(1, level, levelTime);
					break;
				}
			}
		}

		private void SetPendingCisd(int direction, double level, DateTime time)
		{
			if (!IsValidPrice(level) || time == Core.Globals.MinDate)
				return;

			if (direction > 0)
			{
				cisdPendingBullLevel = level;
				cisdPendingBullTime = time;
			}
			else if (direction < 0)
			{
				cisdPendingBearLevel = level;
				cisdPendingBearTime = time;
			}
		}

		private void RefreshSelectedPendingCisd()
		{
			bool hasBull = IsValidPrice(cisdPendingBullLevel) && cisdPendingBullTime != Core.Globals.MinDate;
			bool hasBear = IsValidPrice(cisdPendingBearLevel) && cisdPendingBearTime != Core.Globals.MinDate;

			if (hasBull && (!hasBear || cisdPendingBullTime >= cisdPendingBearTime))
			{
				cisdPendingDirection = 1;
				cisdPendingLevel = cisdPendingBullLevel;
				cisdPendingTime = cisdPendingBullTime;
			}
			else if (hasBear)
			{
				cisdPendingDirection = -1;
				cisdPendingLevel = cisdPendingBearLevel;
				cisdPendingTime = cisdPendingBearTime;
			}
			else
			{
				cisdPendingDirection = 0;
				cisdPendingLevel = double.NaN;
				cisdPendingTime = Core.Globals.MinDate;
			}
		}

		private void ConfirmPendingCisdOnClosedCtfBar()
		{
			if (!IsFirstTickOfBar || CurrentBars[cisdBarsInProgress] < 1)
				return;

			double closedClose = Closes[cisdBarsInProgress][1];
			DateTime closedTime = Times[cisdBarsInProgress][1];

			if (IsValidPrice(cisdPendingBullLevel) && cisdPendingBullTime != Core.Globals.MinDate && closedTime > cisdPendingBullTime && closedClose > cisdPendingBullLevel)
			{
				ConfirmCisd(1, cisdPendingBullLevel, closedTime);
				cisdPendingBullLevel = double.NaN;
				cisdPendingBullTime = Core.Globals.MinDate;
			}

			if (IsValidPrice(cisdPendingBearLevel) && cisdPendingBearTime != Core.Globals.MinDate && closedTime > cisdPendingBearTime && closedClose < cisdPendingBearLevel)
			{
				ConfirmCisd(-1, cisdPendingBearLevel, closedTime);
				cisdPendingBearLevel = double.NaN;
				cisdPendingBearTime = Core.Globals.MinDate;
			}

			RefreshSelectedPendingCisd();
		}

		private void ProcessCisdCandle(double open, double high, double low, double close, DateTime barTime)
		{
			int candleDirection = GetCandleDirection(open, high, low, close);
			if (candleDirection == 0)
				return;

			if (cisdRunDirection == 0 || candleDirection == cisdRunDirection)
			{
				if (cisdRunDirection != candleDirection)
				{
					cisdRunDirection = candleDirection;
					cisdPendingLevel = open;
					cisdPendingTime = barTime;
					return;
				}

				if (cisdRunDirection > 0 && open < cisdPendingLevel)
				{
					cisdPendingLevel = open;
					cisdPendingTime = barTime;
				}
				else if (cisdRunDirection < 0 && open > cisdPendingLevel)
				{
					cisdPendingLevel = open;
					cisdPendingTime = barTime;
				}
				return;
			}

			if (cisdRunDirection < 0 && IsValidPrice(cisdPendingLevel) && barTime > cisdPendingTime && close > cisdPendingLevel)
				ConfirmCisd(1, cisdPendingLevel, barTime);
			else if (cisdRunDirection > 0 && IsValidPrice(cisdPendingLevel) && barTime > cisdPendingTime && close < cisdPendingLevel)
				ConfirmCisd(-1, cisdPendingLevel, barTime);

			cisdRunDirection = candleDirection;
			cisdPendingLevel = open;
			cisdPendingTime = barTime;
		}

		private int GetCandleDirection(double open, double close)
		{
			if (IsDojiCandle(open, close))
				return 0;

			if (close > open)
				return 1;
			if (close < open)
				return -1;
			return 0;
		}

		private int GetCandleDirection(double open, double high, double low, double close)
		{
			if (IsDojiCandle(open, high, low, close))
				return 0;

			if (close > open)
				return 1;
			if (close < open)
				return -1;
			return 0;
		}

		private bool IsDojiCandle(int barsAgo)
		{
			return IsDojiCandle(Open[barsAgo], High[barsAgo], Low[barsAgo], Close[barsAgo]);
		}

		private bool IsDojiCandle(double open, double high, double low, double close)
		{
			double body = Math.Abs(close - open);
			double range = Math.Max(TickSize, high - low);
			double tickThreshold = Math.Max(0, DojiBodyMaxTicks) * TickSize;
			double percentThreshold = range * Math.Max(0.0, DojiBodyMaxPercent) * 0.01;
			double proportionThreshold = range / Math.Max(1.0, DojiBodyProportionSize);
			bool openCloseEq = body <= Math.Max(Math.Max(tickThreshold, percentThreshold), proportionThreshold);
			if (!openCloseEq)
				return false;

			double upperWick = high - Math.Max(open, close);
			double lowerWick = Math.Min(open, close) - low;
			bool balancedLower = lowerWick > range / 2.5 && lowerWick < range / 1.65;
			bool balancedUpper = upperWick > range / 2.5 && upperWick < range / 1.65;
			bool dojiStar = balancedLower && balancedUpper;
			bool gravestone = lowerWick <= proportionThreshold && upperWick > body * Math.Max(1.0, DojiLongWickProportion);
			bool dragonfly = upperWick <= proportionThreshold && lowerWick > body * Math.Max(1.0, DojiLongWickProportion);

			return dojiStar || gravestone || dragonfly || body <= Math.Max(tickThreshold, percentThreshold);
		}

		private bool IsDojiCandle(double open, double close)
		{
			return Math.Abs(close - open) <= Math.Max(0, DojiBodyMaxTicks) * TickSize;
		}

		private void ConfirmCisd(int direction, double level, DateTime time)
		{
			cisdDirection = direction;
			cisdConfirmedLevel = level;
			cisdConfirmedTime = time;
			cisdConfirmedPrimaryBar = CurrentBars != null && CurrentBars.Length > 0 ? CurrentBars[0] : CurrentBar;
			cisdConfirmedSeriesBar = CurrentBars != null && CurrentBars.Length > cisdBarsInProgress ? CurrentBars[cisdBarsInProgress] : -1;
		}

		private void ResetCisdState()
		{
			cisdConfirmedPrimaryBar = -1;
			cisdConfirmedSeriesBar = -1;
			cisdDirection = 0;
			cisdRunDirection = 0;
			cisdPendingDirection = 0;
			cisdPendingLevel = double.NaN;
			cisdPendingBullLevel = double.NaN;
			cisdPendingBearLevel = double.NaN;
			cisdConfirmedLevel = double.NaN;
			cisdPendingTime = Core.Globals.MinDate;
			cisdPendingBullTime = Core.Globals.MinDate;
			cisdPendingBearTime = Core.Globals.MinDate;
			cisdConfirmedTime = Core.Globals.MinDate;
			cisdLastStoredClosedTime = Core.Globals.MinDate;
			cisdHistory.Clear();
			RemoveDrawObject("TTFM_CISD");
			RemoveDrawObject("TTFM_CISD_LABEL");
		}

		private void PopulateCandlesFromState(TTFM_Context context)
		{
			if (c3BarIndex == CurrentBar)
				LockCandle(0, out c3Open, out c3High, out c3Low, out c3Close, out c3Time);

			context.SetupId = setupId;
			context.SetupStatus = fractalStatus;
			context.SetupPhase = setupPhase;
			context.SetupAgeBars = c3BarIndex >= 0 ? CurrentBar - c3BarIndex : (c2BarIndex >= 0 ? CurrentBar - c2BarIndex : 0);
			context.FailureReason = failureReason;
			context.Bias = ExternalBias != 0 ? ExternalBias : setupDirection;
			context.SequenceDirection = setupDirection;

			context.C1High = c1High;
			context.C1Low = c1Low;
			context.C1Open = c1Open;
			context.C1Close = c1Close;
			context.C1Time = c1Time;

			context.C2High = c2High;
			context.C2Low = c2Low;
			context.C2Open = c2Open;
			context.C2Close = c2Close;
			context.C2Time = c2Time;

			context.C3High = c3High;
			context.C3Low = c3Low;
			context.C3Open = c3Open;
			context.C3Close = c3Close;
			context.C3Eq = IsValidPrice(c3High) && IsValidPrice(c3Low) ? (c3High + c3Low) * 0.5 : double.NaN;
			context.C3Time = c3Time;

			PopulatePostC3CandlesFromActiveState(context);

			if (c2BarIndex < 0)
				PopulateCandlesFromScannedSequence(context);
		}

		private void PopulateCandlesFromScannedSequence(TTFM_Context context)
		{
			int direction;
			int c2BarsAgo;
			int c3BarsAgo;
			if (!FindLatestActiveScannedSequence(out direction, out c2BarsAgo, out c3BarsAgo))
				return;

			int c1BarsAgo = c2BarsAgo + 1;
			if (c1BarsAgo > CurrentBar)
				return;

			context.SequenceDirection = direction;
			context.Bias = ExternalBias != 0 ? ExternalBias : direction;
			context.SetupStatus = TTFMSetupStatus.Valid;
			context.SetupPhase = direction > 0 ? "Bullish scanned C3 active" : "Bearish scanned C3 active";
			context.SetupAgeBars = c3BarsAgo;

			PopulateContextCandle(context, c1BarsAgo, 1);
			PopulateContextCandle(context, c2BarsAgo, 2);
			PopulateContextCandle(context, c3BarsAgo, 3);

			context.C3Eq = IsValidPrice(context.C3High) && IsValidPrice(context.C3Low) ? (context.C3High + context.C3Low) * 0.5 : double.NaN;
			PopulatePostC3CandlesFromScannedSequence(context, c3BarsAgo);
		}

		private void PopulatePostC3CandlesFromActiveState(TTFM_Context context)
		{
			ClearContextCandle(context, 4);
			ClearContextCandle(context, 5);
			ClearContextCandle(context, 6);

			if (c3BarIndex < 0)
				return;

			for (int candleNumber = 4; candleNumber <= 6; candleNumber++)
			{
				int barIndex = c3BarIndex + (candleNumber - 3);
				if (CurrentBar < barIndex)
					continue;

				PopulateContextCandle(context, CurrentBar - barIndex, candleNumber);
			}
		}

		private void PopulatePostC3CandlesFromScannedSequence(TTFM_Context context, int c3BarsAgo)
		{
			ClearContextCandle(context, 4);
			ClearContextCandle(context, 5);
			ClearContextCandle(context, 6);

			for (int candleNumber = 4; candleNumber <= 6; candleNumber++)
			{
				int barsAgo = c3BarsAgo - (candleNumber - 3);
				if (barsAgo < 0 || barsAgo > CurrentBar)
					continue;

				PopulateContextCandle(context, barsAgo, candleNumber);
			}
		}

		private void PopulateContextCandle(TTFM_Context context, int barsAgo, int candleNumber)
		{
			if (barsAgo < 0 || barsAgo > CurrentBar)
				return;

			if (candleNumber == 1)
			{
				context.C1High = High[barsAgo];
				context.C1Low = Low[barsAgo];
				context.C1Open = Open[barsAgo];
				context.C1Close = Close[barsAgo];
				context.C1Time = Time[barsAgo];
			}
			else if (candleNumber == 2)
			{
				context.C2High = High[barsAgo];
				context.C2Low = Low[barsAgo];
				context.C2Open = Open[barsAgo];
				context.C2Close = Close[barsAgo];
				context.C2Time = Time[barsAgo];
			}
			else if (candleNumber == 3)
			{
				context.C3High = High[barsAgo];
				context.C3Low = Low[barsAgo];
				context.C3Open = Open[barsAgo];
				context.C3Close = Close[barsAgo];
				context.C3Time = Time[barsAgo];
			}
			else if (candleNumber == 4)
			{
				context.C4High = High[barsAgo];
				context.C4Low = Low[barsAgo];
				context.C4Open = Open[barsAgo];
				context.C4Close = Close[barsAgo];
				context.C4Time = Time[barsAgo];
			}
			else if (candleNumber == 5)
			{
				context.C5High = High[barsAgo];
				context.C5Low = Low[barsAgo];
				context.C5Open = Open[barsAgo];
				context.C5Close = Close[barsAgo];
				context.C5Time = Time[barsAgo];
			}
			else if (candleNumber == 6)
			{
				context.C6High = High[barsAgo];
				context.C6Low = Low[barsAgo];
				context.C6Open = Open[barsAgo];
				context.C6Close = Close[barsAgo];
				context.C6Time = Time[barsAgo];
			}
		}

		private void ClearContextCandle(TTFM_Context context, int candleNumber)
		{
			if (candleNumber == 4)
			{
				context.C4High = double.NaN;
				context.C4Low = double.NaN;
				context.C4Open = double.NaN;
				context.C4Close = double.NaN;
				context.C4Time = Core.Globals.MinDate;
			}
			else if (candleNumber == 5)
			{
				context.C5High = double.NaN;
				context.C5Low = double.NaN;
				context.C5Open = double.NaN;
				context.C5Close = double.NaN;
				context.C5Time = Core.Globals.MinDate;
			}
			else if (candleNumber == 6)
			{
				context.C6High = double.NaN;
				context.C6Low = double.NaN;
				context.C6Open = double.NaN;
				context.C6Close = double.NaN;
				context.C6Time = Core.Globals.MinDate;
			}
		}

		private void DetermineBias(TTFM_Context context)
		{
			if (ExternalBias > 0)
			{
				context.Bias = 1;
				return;
			}
			if (ExternalBias < 0)
			{
				context.Bias = -1;
				return;
			}

			bool closeAboveC2 = context.C3Close > context.C2High;
			bool closeBelowC2 = context.C3Close < context.C2Low;
			if (closeAboveC2)
				context.Bias = 1;
			else if (closeBelowC2)
				context.Bias = -1;
			else if (context.C3Close > context.C3Eq)
				context.Bias = 1;
			else if (context.C3Close < context.C3Eq)
				context.Bias = -1;
			else
				context.Bias = 0;
		}

		private void LockCandle(int barsAgo, out double open, out double high, out double low, out double close, out DateTime time)
		{
			open = Open[barsAgo];
			high = High[barsAgo];
			low = Low[barsAgo];
			close = Close[barsAgo];
			time = Time[barsAgo];
		}

		private bool IsBullishC2At(int barsAgo)
		{
			if (barsAgo + 1 > CurrentBar)
				return false;

			bool previousBearish = Close[barsAgo + 1] < Open[barsAgo + 1];
			return previousBearish
				&& Low[barsAgo] < Low[barsAgo + 1]
				&& Close[barsAgo] > Low[barsAgo + 1];
		}

		private bool IsBearishC2At(int barsAgo)
		{
			if (barsAgo + 1 > CurrentBar)
				return false;

			bool previousBullish = Close[barsAgo + 1] > Open[barsAgo + 1];
			return previousBullish
				&& High[barsAgo] > High[barsAgo + 1]
				&& Close[barsAgo] < High[barsAgo + 1];
		}

		private bool IsBullExpansion(double c2LowValue, double c2HighValue, double currentLow, double currentClose, bool strictClose)
		{
			return currentLow > c2LowValue && (strictClose ? currentClose > c2HighValue : currentClose > (c2HighValue + c2LowValue) * 0.5);
		}

		private bool IsBearExpansion(double c2LowValue, double c2HighValue, double currentHigh, double currentClose, bool strictClose)
		{
			return currentHigh < c2HighValue && (strictClose ? currentClose < c2LowValue : currentClose < (c2HighValue + c2LowValue) * 0.5);
		}

		private bool IsProtectedSwingBrokenByClosedBar()
		{
			if (!IsFirstTickOfBar || c2BarIndex < 0 || CurrentBar <= c2BarIndex)
				return false;

			int closedBarsAgo = 1;
			int closedBarIndex = CurrentBar - closedBarsAgo;
			if (closedBarIndex <= c2BarIndex)
				return false;

			return setupDirection > 0
				? Low[closedBarsAgo] < c2Low
				: High[closedBarsAgo] > c2High;
		}

		private void FailSetup(string reason)
		{
			fractalStatus = TTFMSetupStatus.Failed;
			setupPhase = "Invalidated";
			failureReason = reason;
			failureBarIndex = CurrentBar;
		}

		private void ResetSetup()
		{
			setupDirection = 0;
			c1BarIndex = -1;
			c2BarIndex = -1;
			c3BarIndex = -1;
			failureBarIndex = -1;
			fractalStatus = TTFMSetupStatus.Forming;
			setupPhase = "Waiting C2";
			failureReason = string.Empty;
			c1Open = c1High = c1Low = c1Close = double.NaN;
			c2Open = c2High = c2Low = c2Close = double.NaN;
			c3Open = c3High = c3Low = c3Close = double.NaN;
			c1Time = c2Time = c3Time = Core.Globals.MinDate;
		}

		private void ComputeTSpot(TTFM_Context context)
		{
			int direction = context.SequenceDirection != 0 ? context.SequenceDirection : setupDirection;

			if (direction > 0 && IsValidPrice(context.C2Low))
			{
				context.ProtectedSwing = context.C2Low;
				context.InvalidationPrice = context.C2Low;
			}
			else if (direction < 0 && IsValidPrice(context.C2High))
			{
				context.ProtectedSwing = context.C2High;
				context.InvalidationPrice = context.C2High;
			}

			if (direction > 0 && IsValidPrice(context.C3Eq))
			{
				context.TSpotLower = context.C3Eq;
				context.TSpotUpper = context.C3High;
			}
			else if (direction < 0 && IsValidPrice(context.C3Eq))
			{
				context.TSpotLower = context.C3Low;
				context.TSpotUpper = context.C3Eq;
			}
		}

		private void ComputeSequenceFacts(TTFM_Context context)
		{
			int direction = context.SequenceDirection != 0 ? context.SequenceDirection : setupDirection;
			if (context.SequenceDirection == 0)
				context.SequenceDirection = setupDirection;
			context.CurrentCandleNumber = c3BarIndex >= 0
				? Math.Min(6, Math.Max(3, 3 + (CurrentBar - c3BarIndex)))
				: (c2BarIndex >= 0
					? 2
					: (context.C3Time != Core.Globals.MinDate
						? Math.Min(6, Math.Max(3, 3 + context.SetupAgeBars))
						: (context.C2Time != Core.Globals.MinDate ? 2 : 0)));

			if (direction == 0)
			{
				context.TSpotTouched = false;
				context.TSpotViolated = false;
				context.ProtectedSwingBroken = false;
				context.DistanceToTSpotTicks = double.NaN;
				context.DistanceToProtectedTicks = double.NaN;
				PopulateCisdFacts(context);
				return;
			}

			if (!IsValidPrice(context.TSpotLower) || !IsValidPrice(context.TSpotUpper))
			{
				context.TSpotTouched = false;
				context.TSpotViolated = false;
				context.ProtectedSwingBroken = IsValidPrice(context.ProtectedSwing)
					&& (direction > 0 ? Low[0] < context.ProtectedSwing : High[0] > context.ProtectedSwing);
				context.DistanceToTSpotTicks = double.NaN;
				context.DistanceToProtectedTicks = IsValidPrice(context.ProtectedSwing)
					? (direction > 0 ? context.CurrentPrice - context.ProtectedSwing : context.ProtectedSwing - context.CurrentPrice) / TickSize
					: double.NaN;
				PopulateCisdFacts(context);
				return;
			}

			context.TSpotTouched = direction > 0
				? IsBetween(context.C4Low, context.TSpotLower, context.TSpotUpper)
				: IsBetween(context.C4High, context.TSpotLower, context.TSpotUpper);
			context.TSpotViolated = direction > 0
				? context.C4Low < context.TSpotLower
				: context.C4High > context.TSpotUpper;
			context.ProtectedSwingBroken = direction > 0
				? context.C4Low < context.ProtectedSwing
				: context.C4High > context.ProtectedSwing;

			double tspotDistance = 0;
			if (context.CurrentPrice < context.TSpotLower)
				tspotDistance = context.TSpotLower - context.CurrentPrice;
			else if (context.CurrentPrice > context.TSpotUpper)
				tspotDistance = context.CurrentPrice - context.TSpotUpper;
			context.DistanceToTSpotTicks = tspotDistance / TickSize;

			double protectedDistance = direction > 0
				? context.CurrentPrice - context.ProtectedSwing
				: context.ProtectedSwing - context.CurrentPrice;
			context.DistanceToProtectedTicks = protectedDistance / TickSize;
			PopulateCisdFacts(context);
		}

		private void PopulateCisdFacts(TTFM_Context context)
		{
			context.CisdEnabled = UseCisd;
			context.CisdMinutes = UseCisd ? Math.Max(1, CisdMinutes) : 0;
			bool hasNewerPending = IsValidPrice(cisdPendingLevel) && cisdPendingTime != Core.Globals.MinDate && cisdPendingTime > cisdConfirmedTime;
			context.CisdConfirmed = cisdDirection != 0 && IsValidPrice(cisdConfirmedLevel) && !hasNewerPending;
			context.CisdDirection = context.CisdConfirmed ? cisdDirection : cisdPendingDirection;
			context.CisdLevel = context.CisdConfirmed ? cisdConfirmedLevel : cisdPendingLevel;
			context.CisdTime = context.CisdConfirmed ? cisdConfirmedTime : cisdPendingTime;
			context.CisdAgeBars = context.CisdConfirmed && cisdConfirmedPrimaryBar >= 0 ? Math.Max(0, CurrentBar - cisdConfirmedPrimaryBar) : 0;
			context.DistanceToCisdTicks = IsValidPrice(context.CisdLevel) ? Math.Abs(context.CurrentPrice - context.CisdLevel) / TickSize : double.NaN;
		}

		private void ScoreAndState(TTFM_Context context)
		{
			int direction = context.SequenceDirection != 0 ? context.SequenceDirection : setupDirection;
			if (direction == 0)
			{
				context.SetupStatus = TTFMSetupStatus.Forming;
				context.AllowedDirection = ICTContextDirection.None;
				context.Confidence = 20;
				context.SetupPhase = "Waiting C2";
				context.Location = "Neutral";
				return;
			}

			if (setupDirection != 0)
			{
				context.SetupStatus = fractalStatus;
				context.SetupPhase = setupPhase;
			}
			context.FailureReason = failureReason;
			context.Location = direction > 0 ? "Bullish TTFM" : "Bearish TTFM";

			bool hasC3 = IsValidPrice(context.C3Eq) && context.C3Time != Core.Globals.MinDate;
			bool displacement = hasC3 && Math.Abs(context.C3Close - context.C3Open) >= Math.Max(TickSize, MinDisplacementBodyTicks * TickSize);
			bool touchedTSpot = hasC3 && (direction > 0
				? IsBetween(context.C4Low, context.TSpotLower, context.TSpotUpper)
				: IsBetween(context.C4High, context.TSpotLower, context.TSpotUpper));
			bool respectedTSpot = hasC3 && (direction > 0 ? context.C4Low >= context.TSpotLower : context.C4High <= context.TSpotUpper);

			int confidence = hasC3 ? 45 : 25;
			if (hasC3) confidence += 20;
			if (displacement) confidence += 15;
			if (context.CisdConfirmed) confidence += 15;
			if (respectedTSpot) confidence += 10;
			if (touchedTSpot) confidence += 10;
			if (HasDirectionalPda(context)) confidence += 10;
			context.Confidence = Math.Min(100, confidence);

			if (ExternalBias != 0 && ExternalBias != direction)
				context.AllowedDirection = ICTContextDirection.None;
			else if (context.SetupStatus == TTFMSetupStatus.Valid)
				context.AllowedDirection = direction > 0 ? ICTContextDirection.LongOnly : ICTContextDirection.ShortOnly;
			else if (context.SetupStatus == TTFMSetupStatus.Paused)
				context.AllowedDirection = ICTContextDirection.Both;
			else
				context.AllowedDirection = ICTContextDirection.None;

			if (hasC3)
				context.Location = direction > 0 ? "Bullish T-Spot" : "Bearish T-Spot";
		}

		private bool HasDirectionalPda(TTFM_Context context)
		{
			int direction = context.SequenceDirection != 0 ? context.SequenceDirection : setupDirection;
			if (direction > 0)
				return IsValidPrice(context.NearestBullZoneTop) && IsValidPrice(context.NearestBullZoneBottom);
			if (direction < 0)
				return IsValidPrice(context.NearestBearZoneTop) && IsValidPrice(context.NearestBearZoneBottom);
			return false;
		}

		private void MergeHtfPdaProjectorZones(TTFM_Context context)
		{
			if (htfPdaProjector == null)
				return;

			context.RoiSource = "ICT_HTF_PDA_Projector";
			context.RoiHtfMinutes = Math.Max(5, HtfPdaProjectorMinutes);
			double roiBias = SafeRawSeriesValue(htfPdaProjector.ActiveBias, 0);
			context.RoiActiveBias = double.IsNaN(roiBias) || double.IsInfinity(roiBias) ? 0 : (int)roiBias;

			double bullTop = SafeSeriesValue(htfPdaProjector.NearestBullTop, 0);
			double bullBottom = SafeSeriesValue(htfPdaProjector.NearestBullBottom, 0);
			double bullCe = SafeSeriesValue(htfPdaProjector.NearestBullCE, 0);
			DateTime bullStart = SafeOaDateSeriesValue(htfPdaProjector.NearestBullStartTimeOa, 0);
			if (IsValidPrice(bullTop) && IsValidPrice(bullBottom))
			{
				context.NearestBullZoneTop = Math.Max(bullTop, bullBottom);
				context.NearestBullZoneBottom = Math.Min(bullTop, bullBottom);
				context.NearestBullZoneCE = bullCe;
				context.NearestBullZoneStartTime = bullStart;
			}

			double bearTop = SafeSeriesValue(htfPdaProjector.NearestBearTop, 0);
			double bearBottom = SafeSeriesValue(htfPdaProjector.NearestBearBottom, 0);
			double bearCe = SafeSeriesValue(htfPdaProjector.NearestBearCE, 0);
			DateTime bearStart = SafeOaDateSeriesValue(htfPdaProjector.NearestBearStartTimeOa, 0);
			if (IsValidPrice(bearTop) && IsValidPrice(bearBottom))
			{
				context.NearestBearZoneTop = Math.Max(bearTop, bearBottom);
				context.NearestBearZoneBottom = Math.Min(bearTop, bearBottom);
				context.NearestBearZoneCE = bearCe;
				context.NearestBearZoneStartTime = bearStart;
			}
		}

		private void FindNearestFvgZones(TTFM_Context context)
		{
			int maxBars = Math.Min(CurrentBar - 2, Math.Max(3, FvgLookbackBars));
			double bestBullDistance = double.MaxValue;
			double bestBearDistance = double.MaxValue;

			for (int barsAgo = 0; barsAgo <= maxBars; barsAgo++)
			{
				if (!IsValidPrice(context.NearestBullZoneTop) && Low[barsAgo] > High[barsAgo + 2])
				{
					double top = Low[barsAgo];
					double bottom = High[barsAgo + 2];
					double distance = Math.Abs(Close[0] - ((top + bottom) * 0.5));
					if (distance < bestBullDistance)
					{
						bestBullDistance = distance;
						context.NearestBullZoneTop = Math.Max(top, bottom);
						context.NearestBullZoneBottom = Math.Min(top, bottom);
						context.NearestBullZoneCE = (context.NearestBullZoneTop + context.NearestBullZoneBottom) * 0.5;
						context.NearestBullZoneStartTime = Time[barsAgo + 2];
						if (string.IsNullOrWhiteSpace(context.RoiSource))
						{
							context.RoiSource = "Local_H1_FVG_Fallback";
							context.RoiHtfMinutes = BarsPeriod != null ? BarsPeriod.Value : 60;
							context.RoiActiveBias = 0;
						}
					}
				}

				if (!IsValidPrice(context.NearestBearZoneTop) && High[barsAgo] < Low[barsAgo + 2])
				{
					double top = Low[barsAgo + 2];
					double bottom = High[barsAgo];
					double distance = Math.Abs(Close[0] - ((top + bottom) * 0.5));
					if (distance < bestBearDistance)
					{
						bestBearDistance = distance;
						context.NearestBearZoneTop = Math.Max(top, bottom);
						context.NearestBearZoneBottom = Math.Min(top, bottom);
						context.NearestBearZoneCE = (context.NearestBearZoneTop + context.NearestBearZoneBottom) * 0.5;
						context.NearestBearZoneStartTime = Time[barsAgo + 2];
						if (string.IsNullOrWhiteSpace(context.RoiSource))
						{
							context.RoiSource = "Local_H1_FVG_Fallback";
							context.RoiHtfMinutes = BarsPeriod != null ? BarsPeriod.Value : 60;
							context.RoiActiveBias = 0;
						}
					}
				}
			}
		}

		private double SafeSeriesValue(Series<double> series, int barsAgo)
		{
			if (series == null || CurrentBar < barsAgo)
				return double.NaN;
			try
			{
				double value = series[barsAgo];
				return IsValidPrice(value) ? value : double.NaN;
			}
			catch
			{
				return double.NaN;
			}
		}

		private double SafeRawSeriesValue(Series<double> series, int barsAgo)
		{
			if (series == null || CurrentBar < barsAgo)
				return double.NaN;
			try
			{
				return series[barsAgo];
			}
			catch
			{
				return double.NaN;
			}
		}

		private DateTime SafeOaDateSeriesValue(Series<double> series, int barsAgo)
		{
			double value = SafeRawSeriesValue(series, barsAgo);
			if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
				return Core.Globals.MinDate;
			try
			{
				return DateTime.FromOADate(value);
			}
			catch
			{
				return Core.Globals.MinDate;
			}
		}

		private void BuildNarrative(TTFM_Context context)
		{
			string bias = context.Bias > 0 ? "Bullish" : (context.Bias < 0 ? "Bearish" : "Neutral");
			context.Narrative = context.Profile + " " + bias +
				", status " + context.SetupStatus +
				", phase " + context.SetupPhase +
				", confidence " + context.Confidence +
				", allowed " + context.AllowedDirection +
				", C" + context.CurrentCandleNumber +
				", CISD " + (context.CisdEnabled ? (context.CisdConfirmed ? "confirmed " : "pending ") + FormatPrice(context.CisdLevel) + " " + context.CisdMinutes + "m" : "off") +
				", touched " + context.TSpotTouched +
				", violated " + context.TSpotViolated +
				", C3 EQ " + FormatPrice(context.C3Eq) +
				", T-Spot " + FormatZone(context.TSpotUpper, context.TSpotLower) +
				", protected " + FormatPrice(context.ProtectedSwing) +
				", invalidation " + FormatPrice(context.InvalidationPrice) +
				", ROI " + context.RoiSource + "(" + context.RoiHtfMinutes + "m bias " + context.RoiActiveBias + ")";
		}

		private void DrawPanel(TTFM_Context context)
		{
			string text =
				"TTFM Context Engine\n" +
				"Key: " + context.Key + " | " + context.Profile + " | ID: " + context.SetupId + "\n" +
				"Bias: " + (context.Bias > 0 ? "Bullish" : (context.Bias < 0 ? "Bearish" : "Neutral")) +
				" | Status: " + context.SetupStatus + " | Age: " + context.SetupAgeBars + " | Conf: " + context.Confidence + "\n" +
				"Allow: " + context.AllowedDirection + " | Phase: " + context.SetupPhase + "\n" +
				"C#: " + context.CurrentCandleNumber + " | Touched: " + context.TSpotTouched + " | Violated: " + context.TSpotViolated + " | Broken: " + context.ProtectedSwingBroken + "\n" +
				"CISD: " + (context.CisdEnabled ? (context.CisdConfirmed ? "Confirmed " : "Pending ") + FormatPrice(context.CisdLevel) + " " + context.CisdMinutes + "m dist " + FormatTicks(context.DistanceToCisdTicks) : "Off") + "\n" +
				"Dist T/P ticks: " + FormatTicks(context.DistanceToTSpotTicks) + " / " + FormatTicks(context.DistanceToProtectedTicks) + "\n" +
				(context.SetupStatus == TTFMSetupStatus.Failed ? "Fail: " + context.FailureReason + "\n" : string.Empty) +
				"C1 H/L: " + FormatPrice(context.C1High) + " / " + FormatPrice(context.C1Low) + "\n" +
				"C2 H/L: " + FormatPrice(context.C2High) + " / " + FormatPrice(context.C2Low) + "\n" +
				"C3 H/EQ/L: " + FormatPrice(context.C3High) + " / " + FormatPrice(context.C3Eq) + " / " + FormatPrice(context.C3Low) + "\n" +
				"C4 H/L/C: " + FormatPrice(context.C4High) + " / " + FormatPrice(context.C4Low) + " / " + FormatPrice(context.C4Close) + "\n" +
				"C5 H/L/C: " + FormatPrice(context.C5High) + " / " + FormatPrice(context.C5Low) + " / " + FormatPrice(context.C5Close) + "\n" +
				"C6 H/L/C: " + FormatPrice(context.C6High) + " / " + FormatPrice(context.C6Low) + " / " + FormatPrice(context.C6Close) + "\n" +
				"T-Spot: " + FormatZone(context.TSpotUpper, context.TSpotLower) + "\n" +
				"Protected/Invalid: " + FormatPrice(context.ProtectedSwing) + " / " + FormatPrice(context.InvalidationPrice) + "\n" +
				"ROI: " + context.RoiSource + " " + context.RoiHtfMinutes + "m bias " + context.RoiActiveBias + "\n" +
				"Bull ROI: " + FormatZone(context.NearestBullZoneTop, context.NearestBullZoneBottom) + " CE " + FormatPrice(context.NearestBullZoneCE) + " @ " + FormatTime(context.NearestBullZoneStartTime) + "\n" +
				"Bear ROI: " + FormatZone(context.NearestBearZoneTop, context.NearestBearZoneBottom) + " CE " + FormatPrice(context.NearestBearZoneCE) + " @ " + FormatTime(context.NearestBearZoneStartTime);

			Draw.TextFixed(this, "TTFM_CONTEXT_PANEL", text, TextPosition.TopLeft, Brushes.WhiteSmoke, new SimpleFont("Consolas", 12), Brushes.DimGray, Brushes.Black, 80);
		}

		private void DrawCisd(TTFM_Context context)
		{
			if (!UseCisd)
			{
				RemoveCisdDrawObjects();
				return;
			}

			TimeSpan span = TimeSpan.FromMinutes(Math.Max(1, CisdMinutes) * Math.Max(1, CisdLineProjectionBars));
			DateTime endTime = Time[0].Add(span);
			bool drew = false;

			if (cisdDirection != 0 && IsValidPrice(cisdConfirmedLevel) && cisdConfirmedTime != Core.Globals.MinDate)
			{
				DrawSingleCisdLine(
					"TTFM_CISD_CONFIRMED",
					"TTFM_CISD_CONFIRMED_LABEL",
					cisdDirection,
					cisdConfirmedLevel,
					cisdConfirmedTime,
					endTime,
					true);
				drew = true;
			}
			else
			{
				RemoveDrawObject("TTFM_CISD_CONFIRMED");
				RemoveDrawObject("TTFM_CISD_CONFIRMED_LABEL");
			}

			if (IsValidPrice(cisdPendingBullLevel) && cisdPendingBullTime != Core.Globals.MinDate)
			{
				DrawSingleCisdLine(
					"TTFM_CISD_PENDING_BULL",
					"TTFM_CISD_PENDING_BULL_LABEL",
					1,
					cisdPendingBullLevel,
					cisdPendingBullTime,
					endTime,
					false);
				drew = true;
			}
			else
			{
				RemoveDrawObject("TTFM_CISD_PENDING_BULL");
				RemoveDrawObject("TTFM_CISD_PENDING_BULL_LABEL");
			}

			if (IsValidPrice(cisdPendingBearLevel) && cisdPendingBearTime != Core.Globals.MinDate)
			{
				DrawSingleCisdLine(
					"TTFM_CISD_PENDING_BEAR",
					"TTFM_CISD_PENDING_BEAR_LABEL",
					-1,
					cisdPendingBearLevel,
					cisdPendingBearTime,
					endTime,
					false);
				drew = true;
			}
			else
			{
				RemoveDrawObject("TTFM_CISD_PENDING_BEAR");
				RemoveDrawObject("TTFM_CISD_PENDING_BEAR_LABEL");
			}

			RemoveDrawObject("TTFM_CISD");
			RemoveDrawObject("TTFM_CISD_LABEL");

			if (!drew)
				RemoveCisdDrawObjects();
		}

		private void DrawSingleCisdLine(string lineTag, string labelTag, int direction, double level, DateTime startTime, DateTime endTime, bool confirmed)
		{
			Brush confirmedBrush = direction > 0
				? (BullCisdBrush ?? Brushes.Blue)
				: (BearCisdBrush ?? Brushes.Red);
			Brush lineBrush = confirmed ? confirmedBrush : (PendingCisdLineBrush ?? Brushes.Gray);
			Brush textBrush = confirmed ? confirmedBrush : (PendingCisdTextBrush ?? Brushes.Black);
			string text = (confirmed ? "CISD" : "pending CISD") + (direction > 0 ? "+" : "-") + " " + Math.Max(1, CisdMinutes) + "m";
			DashStyleHelper style = confirmed ? DashStyleHelper.Solid : DashStyleHelper.Dash;
			int width = confirmed ? Math.Max(1, ConfirmedCisdLineWidth) : Math.Max(1, PendingCisdLineWidth);

			Draw.Line(this, lineTag, false, startTime, level, endTime, level, lineBrush, style, width);
			Draw.Text(this, labelTag, false, text, endTime, level, 0, textBrush, new SimpleFont("Arial", 10), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void RemoveCisdDrawObjects()
		{
			RemoveDrawObject("TTFM_CISD");
			RemoveDrawObject("TTFM_CISD_LABEL");
			RemoveDrawObject("TTFM_CISD_CONFIRMED");
			RemoveDrawObject("TTFM_CISD_CONFIRMED_LABEL");
			RemoveDrawObject("TTFM_CISD_PENDING_BULL");
			RemoveDrawObject("TTFM_CISD_PENDING_BULL_LABEL");
			RemoveDrawObject("TTFM_CISD_PENDING_BEAR");
			RemoveDrawObject("TTFM_CISD_PENDING_BEAR_LABEL");
		}

		private void DrawTSpot(TTFM_Context context)
		{
			if (context.SetupStatus == TTFMSetupStatus.Failed || context.SetupStatus == TTFMSetupStatus.Completed || setupDirection == 0 || c3BarIndex < 0)
			{
				RemoveDrawObject("TTFM_TSPOT");
				return;
			}

			if (!IsValidPrice(context.TSpotUpper) || !IsValidPrice(context.TSpotLower))
			{
				RemoveDrawObject("TTFM_TSPOT");
				return;
			}

			int c4BarIndex = c3BarIndex + 1;
			if (c4BarIndex > CurrentBar)
			{
				RemoveDrawObject("TTFM_TSPOT");
				return;
			}

			int c4BarsAgo = CurrentBar - c4BarIndex;
			Brush brush = setupDirection > 0 ? BullTSpotBrush : BearTSpotBrush;
			DrawTSpotAtBarsAgo(c4BarsAgo, context.TSpotUpper, context.TSpotLower, brush, setupDirection);
		}

		private bool DrawLatestScannedTSpot()
		{
			int direction;
			int c2BarsAgo;
			int c3BarsAgo;
			if (!FindLatestScannedSequence(out direction, out c2BarsAgo, out c3BarsAgo))
				return false;

			int c4BarsAgo = c3BarsAgo - 1;
			if (c4BarsAgo < 0)
				return false;

			double c3Eq = (High[c3BarsAgo] + Low[c3BarsAgo]) * 0.5;
			if (direction > 0)
				DrawTSpotAtBarsAgo(c4BarsAgo, High[c3BarsAgo], c3Eq, BullTSpotBrush, direction);
			else
				DrawTSpotAtBarsAgo(c4BarsAgo, c3Eq, Low[c3BarsAgo], BearTSpotBrush, direction);

			return true;
		}

		private void DrawTSpotAtBarsAgo(int c4BarsAgo, double upper, double lower, Brush brush, int direction)
		{
			TimeSpan barSpan = c4BarsAgo + 1 <= CurrentBar ? Time[c4BarsAgo] - Time[c4BarsAgo + 1] : TimeSpan.FromMinutes(Math.Max(1, BarsPeriod.Value));
			if (barSpan <= TimeSpan.Zero)
				barSpan = TimeSpan.FromMinutes(Math.Max(1, BarsPeriod.Value));

			DateTime c4LeftEdge = Time[c4BarsAgo].AddTicks(-barSpan.Ticks / 2);
			DateTime c4RightEdge = Time[c4BarsAgo].AddTicks(barSpan.Ticks / 2);
			DateTime startTime;
			DateTime endTime;
			if (direction > 0)
			{
				startTime = c4RightEdge.AddTicks(-barSpan.Ticks * Math.Max(0, TSpotStartOffsetBars));
				endTime = startTime.AddTicks(-barSpan.Ticks * Math.Max(1, TSpotProjectionBars));
			}
			else
			{
				startTime = c4LeftEdge.AddTicks(barSpan.Ticks * Math.Max(0, TSpotStartOffsetBars));
				endTime = startTime.AddTicks(barSpan.Ticks * Math.Max(1, TSpotProjectionBars));
			}
			Draw.Rectangle(this, "TTFM_TSPOT", false, startTime, Math.Max(upper, lower), endTime, Math.Min(upper, lower), brush, brush, TSpotOpacity);
		}

		private void DrawFractalLabels(TTFM_Context context)
		{
			RemoveFractalLabels();

			if (context.SetupStatus == TTFMSetupStatus.Failed
				|| context.SetupStatus == TTFMSetupStatus.Completed
				|| setupDirection == 0
				|| c2BarIndex < 0)
			{
				DrawLatestActiveScannedCandleLabels();
				return;
			}

			DrawRecentC2C3Labels();
		}

		private void DrawRecentC2C3Labels()
		{
			double offset = Math.Max(TickSize, CLabelTickOffset * TickSize);

			int c2BarsAgo = CurrentBar - c2BarIndex;
			if (c2BarsAgo >= 0 && c2BarsAgo <= CurrentBar)
			{
				double y = setupDirection > 0 ? Low[c2BarsAgo] - offset : High[c2BarsAgo] + offset;
				DrawFractalText("TTFM_C2_ACTIVE", "C2", c2BarsAgo, y);
			}

			if (c3BarIndex < 0)
				return;

			int c3BarsAgo = CurrentBar - c3BarIndex;
			if (c3BarsAgo >= 0 && c3BarsAgo <= CurrentBar)
			{
				double y = setupDirection > 0 ? Low[c3BarsAgo] - offset : High[c3BarsAgo] + offset;
				DrawFractalText("TTFM_C3_ACTIVE", "C3", c3BarsAgo, y);
			}

			int maxNumber = Math.Min(6, 3 + Math.Max(0, CurrentBar - c3BarIndex));
			for (int number = 4; number <= maxNumber; number++)
			{
				int barIndex = c3BarIndex + (number - 3);
				if (barIndex > CurrentBar)
					break;

				int barsAgo = CurrentBar - barIndex;
				double y = setupDirection > 0 ? Low[barsAgo] - offset : High[barsAgo] + offset;
				DrawFractalText("TTFM_C" + number + "_ACTIVE", "C" + number, barsAgo, y);
			}
		}

		private void DrawFractalText(string tag, string text, int barsAgo, double y)
		{
			drawnFractalTags.Add(tag);
			Draw.Text(this, tag, text, barsAgo, y, Brushes.Black);
		}

		private void DrawLatestActiveScannedCandleLabels()
		{
			int direction;
			int c2BarsAgo;
			int c3BarsAgo;
			if (!FindLatestActiveScannedSequence(out direction, out c2BarsAgo, out c3BarsAgo))
					return;

			double offset = Math.Max(TickSize, CLabelTickOffset * TickSize);
			DrawFractalText("TTFM_C2_SCAN_ACTIVE", "C2", c2BarsAgo, direction > 0 ? Low[c2BarsAgo] - offset : High[c2BarsAgo] + offset);
			DrawFractalText("TTFM_C3_SCAN_ACTIVE", "C3", c3BarsAgo, direction > 0 ? Low[c3BarsAgo] - offset : High[c3BarsAgo] + offset);

			int maxNumber = Math.Min(6, 3 + Math.Max(0, c3BarsAgo));
			for (int number = 4; number <= maxNumber; number++)
			{
				int barsAgo = c3BarsAgo - (number - 3);
				if (barsAgo < 0 || barsAgo > CurrentBar)
					break;

				DrawFractalText("TTFM_C" + number + "_SCAN_ACTIVE", "C" + number, barsAgo, direction > 0 ? Low[barsAgo] - offset : High[barsAgo] + offset);
			}
		}

		private bool FindLatestActiveScannedSequence(out int direction, out int c2BarsAgo, out int c3BarsAgo)
		{
			direction = 0;
			c2BarsAgo = -1;
			c3BarsAgo = -1;

			int scanDirection;
			int scanC2BarsAgo;
			int scanC3BarsAgo;
			if (!FindLatestScannedSequence(out scanDirection, out scanC2BarsAgo, out scanC3BarsAgo))
				return false;

			int ageFromC3 = scanC3BarsAgo;
			if (ageFromC3 < 0 || ageFromC3 > 3)
				return false;

			if (IsScannedSequenceBroken(scanDirection, scanC2BarsAgo, scanC3BarsAgo))
				return false;

			direction = scanDirection;
			c2BarsAgo = scanC2BarsAgo;
			c3BarsAgo = scanC3BarsAgo;
			return true;
		}

		private bool IsScannedSequenceBroken(int direction, int c2BarsAgo, int c3BarsAgo)
		{
			if (direction == 0 || c2BarsAgo < 0 || c3BarsAgo < 0)
				return true;

			if (c3BarsAgo > 0 && IsC4FailedContinuation(direction, High[c3BarsAgo - 1], Low[c3BarsAgo - 1], High[c3BarsAgo], Low[c3BarsAgo]))
				return true;

			double c2Protected = direction > 0 ? Low[c2BarsAgo] : High[c2BarsAgo];
			for (int barsAgo = c3BarsAgo - 1; barsAgo >= 0; barsAgo--)
			{
				if (direction > 0)
				{
					if (Low[barsAgo] < c2Protected)
						return true;
				}
				else
				{
					if (High[barsAgo] > c2Protected)
						return true;
				}
			}

			return false;
		}

		private bool FindLatestScannedSequence(out int direction, out int c2BarsAgo, out int c3BarsAgo)
		{
			direction = 0;
			c2BarsAgo = -1;
			c3BarsAgo = -1;

			int activeDirection = 0;
			int activeC2BarsAgo = -1;
			int latestDirection = 0;
			int latestC2BarsAgo = -1;
			int latestC3BarsAgo = -1;
			int maxBarsAgo = Math.Min(CurrentBar - 2, Math.Max(3, CLabelLookbackBars));

			for (int barsAgo = maxBarsAgo; barsAgo >= 0; barsAgo--)
			{
				bool bullC2 = IsBullishC2At(barsAgo);
				bool bearC2 = IsBearishC2At(barsAgo);

				if (bullC2 && !bearC2)
				{
					activeDirection = 1;
					activeC2BarsAgo = barsAgo;
					continue;
				}
				if (bearC2 && !bullC2)
				{
					activeDirection = -1;
					activeC2BarsAgo = barsAgo;
					continue;
				}

				if (activeDirection == 0 || activeC2BarsAgo < 0)
					continue;

				int ageFromC2 = activeC2BarsAgo - barsAgo;
				if (ageFromC2 < 1)
					continue;

				bool bullC3 = activeDirection > 0 && IsBullExpansion(Low[activeC2BarsAgo], High[activeC2BarsAgo], Low[barsAgo], Close[barsAgo], RequireStrongC3Close);
				bool bearC3 = activeDirection < 0 && IsBearExpansion(Low[activeC2BarsAgo], High[activeC2BarsAgo], High[barsAgo], Close[barsAgo], RequireStrongC3Close);
				if (bullC3 || bearC3)
				{
					latestDirection = activeDirection;
					latestC2BarsAgo = activeC2BarsAgo;
					latestC3BarsAgo = barsAgo;
					activeDirection = 0;
					activeC2BarsAgo = -1;
				}
			}

			if (latestDirection == 0 || latestC2BarsAgo < 0 || latestC3BarsAgo < 0)
				return false;

			direction = latestDirection;
			c2BarsAgo = latestC2BarsAgo;
			c3BarsAgo = latestC3BarsAgo;
			return true;
		}

		private void RemoveFractalLabels()
		{
			foreach (string tag in drawnFractalTags)
				RemoveDrawObject(tag);
			drawnFractalTags.Clear();

			RemoveDrawObject("TTFM_C2");
			RemoveDrawObject("TTFM_C3");
			RemoveDrawObject("TTFM_C4");
			RemoveDrawObject("TTFM_C2_ACTIVE");
			RemoveDrawObject("TTFM_C3_ACTIVE");
			RemoveDrawObject("TTFM_C4_ACTIVE");
			RemoveDrawObject("TTFM_C5_ACTIVE");
			RemoveDrawObject("TTFM_C6_ACTIVE");
			RemoveDrawObject("TTFM_C2_SCAN_ACTIVE");
			RemoveDrawObject("TTFM_C3_SCAN_ACTIVE");
			RemoveDrawObject("TTFM_C4_SCAN_ACTIVE");
			RemoveDrawObject("TTFM_C5_SCAN_ACTIVE");
			RemoveDrawObject("TTFM_C6_SCAN_ACTIVE");

			if (!legacyFractalTagsScrubbed)
			{
				int max = Math.Max(CurrentBar + 5, CLabelLookbackBars + 50);
				for (int i = 0; i <= max; i++)
				{
					RemoveDrawObject("TTFM_C2_SCAN_" + i);
					RemoveDrawObject("TTFM_C3_SCAN_" + i);
					RemoveDrawObject("TTFM_C4_SCAN_" + i);
					RemoveDrawObject("TTFM_C5_SCAN_" + i);
					RemoveDrawObject("TTFM_C6_SCAN_" + i);
				}
				legacyFractalTagsScrubbed = true;
			}
		}

		private void WriteContextSnapshot(TTFM_Context context)
		{
			try
			{
				string path = ResolveSnapshotFilePath();
				string dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				File.WriteAllText(path, BuildSnapshotText(context));
			}
			catch (Exception ex)
			{
				if (DebugPrint)
					Print("TTFM_Context snapshot write failed: " + ex.Message);
			}
		}

		private string BuildSnapshotText(TTFM_Context context)
		{
			return
				"key=" + context.Key + Environment.NewLine +
				"instrument=" + context.Instrument + Environment.NewLine +
				"profile=" + context.Profile + Environment.NewLine +
				"context_tf_minutes=" + context.ContextTfMinutes + Environment.NewLine +
				"published_at=" + context.PublishedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"bar_time=" + context.BarTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"price=" + RawPrice(context.CurrentPrice) + Environment.NewLine +
				"bias=" + context.Bias + Environment.NewLine +
				"confidence=" + context.Confidence + Environment.NewLine +
				"allowed_direction=" + context.AllowedDirection + Environment.NewLine +
				"setup_status=" + context.SetupStatus + Environment.NewLine +
				"setup_phase=" + context.SetupPhase + Environment.NewLine +
				"setup_id=" + context.SetupId + Environment.NewLine +
				"setup_age_bars=" + context.SetupAgeBars + Environment.NewLine +
				"failure_reason=" + context.FailureReason + Environment.NewLine +
				"child_context_key=" + context.ChildContextKey + Environment.NewLine +
				"child_tf_minutes=" + context.ChildTfMinutes + Environment.NewLine +
				"child_bar_time=" + context.ChildBarTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"child_parent_candle_number=" + context.ChildParentCandleNumber + Environment.NewLine +
				"child_parent_candle_start=" + context.ChildParentCandleStartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"child_parent_candle_end=" + context.ChildParentCandleEndTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"child_parent_candle_high=" + RawPrice(context.ChildParentCandleHigh) + Environment.NewLine +
				"child_parent_candle_low=" + RawPrice(context.ChildParentCandleLow) + Environment.NewLine +
				"child_parent_candle_open=" + RawPrice(context.ChildParentCandleOpen) + Environment.NewLine +
				"child_parent_candle_close=" + RawPrice(context.ChildParentCandleClose) + Environment.NewLine +
				"is_inside_parent_candle_window=" + context.IsInsideParentCandleWindow + Environment.NewLine +
				"ai_interface_version=" + context.AiInterfaceVersion + Environment.NewLine +
				"ai_setup_gate=" + context.AiSetupGate + Environment.NewLine +
				"ai_setup_gate_reason=" + context.AiSetupGateReason + Environment.NewLine +
				"sequence_direction=" + context.SequenceDirection + Environment.NewLine +
				"current_candle_number=" + context.CurrentCandleNumber + Environment.NewLine +
				"tspot_touched=" + context.TSpotTouched + Environment.NewLine +
				"tspot_violated=" + context.TSpotViolated + Environment.NewLine +
				"protected_swing_broken=" + context.ProtectedSwingBroken + Environment.NewLine +
				"cisd_enabled=" + context.CisdEnabled + Environment.NewLine +
				"cisd_confirmed=" + context.CisdConfirmed + Environment.NewLine +
				"cisd_minutes=" + context.CisdMinutes + Environment.NewLine +
				"cisd_direction=" + context.CisdDirection + Environment.NewLine +
				"cisd_level=" + RawPrice(context.CisdLevel) + Environment.NewLine +
				"cisd_time=" + context.CisdTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"cisd_age_bars=" + context.CisdAgeBars + Environment.NewLine +
				"distance_to_cisd_ticks=" + RawDouble(context.DistanceToCisdTicks) + Environment.NewLine +
				"distance_to_tspot_ticks=" + RawDouble(context.DistanceToTSpotTicks) + Environment.NewLine +
				"distance_to_protected_ticks=" + RawDouble(context.DistanceToProtectedTicks) + Environment.NewLine +
				"location=" + context.Location + Environment.NewLine +
				"c1_high=" + RawPrice(context.C1High) + Environment.NewLine +
				"c1_low=" + RawPrice(context.C1Low) + Environment.NewLine +
				"c1_open=" + RawPrice(context.C1Open) + Environment.NewLine +
				"c1_close=" + RawPrice(context.C1Close) + Environment.NewLine +
				"c1_time=" + context.C1Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"c2_high=" + RawPrice(context.C2High) + Environment.NewLine +
				"c2_low=" + RawPrice(context.C2Low) + Environment.NewLine +
				"c2_open=" + RawPrice(context.C2Open) + Environment.NewLine +
				"c2_close=" + RawPrice(context.C2Close) + Environment.NewLine +
				"c2_time=" + context.C2Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"c3_high=" + RawPrice(context.C3High) + Environment.NewLine +
				"c3_low=" + RawPrice(context.C3Low) + Environment.NewLine +
				"c3_open=" + RawPrice(context.C3Open) + Environment.NewLine +
				"c3_close=" + RawPrice(context.C3Close) + Environment.NewLine +
				"c3_eq=" + RawPrice(context.C3Eq) + Environment.NewLine +
				"c3_time=" + context.C3Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"c4_high=" + RawPrice(context.C4High) + Environment.NewLine +
				"c4_low=" + RawPrice(context.C4Low) + Environment.NewLine +
				"c4_open=" + RawPrice(context.C4Open) + Environment.NewLine +
				"c4_close=" + RawPrice(context.C4Close) + Environment.NewLine +
				"c4_time=" + context.C4Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"c5_high=" + RawPrice(context.C5High) + Environment.NewLine +
				"c5_low=" + RawPrice(context.C5Low) + Environment.NewLine +
				"c5_open=" + RawPrice(context.C5Open) + Environment.NewLine +
				"c5_close=" + RawPrice(context.C5Close) + Environment.NewLine +
				"c5_time=" + context.C5Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"c6_high=" + RawPrice(context.C6High) + Environment.NewLine +
				"c6_low=" + RawPrice(context.C6Low) + Environment.NewLine +
				"c6_open=" + RawPrice(context.C6Open) + Environment.NewLine +
				"c6_close=" + RawPrice(context.C6Close) + Environment.NewLine +
				"c6_time=" + context.C6Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"t_spot_upper=" + RawPrice(context.TSpotUpper) + Environment.NewLine +
				"t_spot_lower=" + RawPrice(context.TSpotLower) + Environment.NewLine +
				"protected_swing=" + RawPrice(context.ProtectedSwing) + Environment.NewLine +
				"invalidation_price=" + RawPrice(context.InvalidationPrice) + Environment.NewLine +
				"roi_source=" + context.RoiSource + Environment.NewLine +
				"roi_htf_minutes=" + context.RoiHtfMinutes + Environment.NewLine +
				"roi_active_bias=" + context.RoiActiveBias + Environment.NewLine +
				"nearest_bull_zone_top=" + RawPrice(context.NearestBullZoneTop) + Environment.NewLine +
				"nearest_bull_zone_bottom=" + RawPrice(context.NearestBullZoneBottom) + Environment.NewLine +
				"nearest_bull_zone_ce=" + RawPrice(context.NearestBullZoneCE) + Environment.NewLine +
				"nearest_bull_zone_start=" + context.NearestBullZoneStartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"nearest_bear_zone_top=" + RawPrice(context.NearestBearZoneTop) + Environment.NewLine +
				"nearest_bear_zone_bottom=" + RawPrice(context.NearestBearZoneBottom) + Environment.NewLine +
				"nearest_bear_zone_ce=" + RawPrice(context.NearestBearZoneCE) + Environment.NewLine +
				"nearest_bear_zone_start=" + context.NearestBearZoneStartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
				"narrative=" + context.Narrative + Environment.NewLine;
		}

		private string ResolveContextKey()
		{
			if (!string.IsNullOrWhiteSpace(ContextKey))
				return ContextKey.Trim();
			return Instrument != null ? Instrument.MasterInstrument.Name + "_TTFM_MAIN" : string.Empty;
		}

		private string ResolveSnapshotFilePath()
		{
			if (!string.IsNullOrWhiteSpace(ContextSnapshotFile))
				return ContextSnapshotFile.Trim();

			string dir = Core.Globals.UserDataDir;
			if (string.IsNullOrWhiteSpace(dir))
				dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

			return Path.Combine(dir, "ttfm_context_snapshot.txt");
		}

		private bool IsBetween(double price, double a, double b)
		{
			if (!IsValidPrice(a) || !IsValidPrice(b))
				return false;
			return price >= Math.Min(a, b) && price <= Math.Max(a, b);
		}

		private bool SamePrice(double a, double b)
		{
			return IsValidPrice(a) && IsValidPrice(b) && Math.Abs(a - b) <= Math.Max(TickSize * 0.5, 1e-10);
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

		private string RawPrice(double price)
		{
			if (!IsValidPrice(price))
				return "NaN";
			return price.ToString("0.########", CultureInfo.InvariantCulture);
		}

		private string RawDouble(double value)
		{
			if (double.IsNaN(value) || double.IsInfinity(value))
				return "NaN";
			return value.ToString("0.########", CultureInfo.InvariantCulture);
		}

		private string FormatTicks(double ticks)
		{
			if (double.IsNaN(ticks) || double.IsInfinity(ticks))
				return "-";
			return ticks.ToString("0.#", CultureInfo.InvariantCulture);
		}

		private class CisdCandle
		{
			public double Open;
			public double High;
			public double Low;
			public double Close;
			public DateTime Time;
			public int Direction;
		}

		[NinjaScriptProperty]
		[Display(Name = "Context Key", GroupName = "01. Publish", Order = 0)]
		public string ContextKey { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Profile", GroupName = "01. Publish", Order = 1)]
		public string Profile { get; set; }

		[NinjaScriptProperty]
		[Range(-1, 1)]
		[Display(Name = "External Bias (-1/0/1)", GroupName = "02. TTFM", Order = 0)]
		public int ExternalBias { get; set; }

		[NinjaScriptProperty]
		[Range(0, 200)]
		[Display(Name = "Min Displacement Body Ticks", GroupName = "02. TTFM", Order = 1)]
		public int MinDisplacementBodyTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "Doji Body Max Ticks", GroupName = "02. TTFM", Order = 2)]
		public int DojiBodyMaxTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 50.0)]
		[Display(Name = "Doji Body Max Percent", GroupName = "02. TTFM", Order = 3)]
		public double DojiBodyMaxPercent { get; set; }

		[NinjaScriptProperty]
		[Range(1.0, 50.0)]
		[Display(Name = "Doji Body Proportion Size", GroupName = "02. TTFM", Order = 4)]
		public double DojiBodyProportionSize { get; set; }

		[NinjaScriptProperty]
		[Range(1.0, 50.0)]
		[Display(Name = "Doji Long Wick Proportion", GroupName = "02. TTFM", Order = 5)]
		public double DojiLongWickProportion { get; set; }

		[NinjaScriptProperty]
		[Range(10, 500)]
		[Display(Name = "FVG Lookback Bars", GroupName = "02. TTFM", Order = 6)]
		public int FvgLookbackBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "C2 Sweep Lookback Bars", GroupName = "02. TTFM", Order = 7)]
		public int C2SweepLookbackBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require Strong C3 Close", GroupName = "02. TTFM", Order = 8)]
		public bool RequireStrongC3Close { get; set; }

		[NinjaScriptProperty]
		[Range(1, 12)]
		[Display(Name = "Max Bars To Wait C3", GroupName = "02. TTFM", Order = 9)]
		public int MaxBarsToWaitC3 { get; set; }

		[NinjaScriptProperty]
		[Range(1, 48)]
		[Display(Name = "Max C4 Tracking Bars", GroupName = "02. TTFM", Order = 10)]
		public int MaxC4TrackingBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use CISD", GroupName = "03. CISD", Order = 0)]
		public bool UseCisd { get; set; }

		[NinjaScriptProperty]
		[Range(1, 240)]
		[Display(Name = "CISD Minutes", GroupName = "03. CISD", Order = 1)]
		public int CisdMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show CISD Line", GroupName = "03. CISD", Order = 2)]
		public bool ShowCisdLine { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "CISD Line Projection Bars", GroupName = "03. CISD", Order = 3)]
		public int CisdLineProjectionBars { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Pending CISD Line Color", GroupName = "03. CISD", Order = 4)]
		public Brush PendingCisdLineBrush { get; set; }
		[Browsable(false)]
		public string PendingCisdLineBrushSerializable { get { return Serialize.BrushToString(PendingCisdLineBrush); } set { PendingCisdLineBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Pending CISD Text Color", GroupName = "03. CISD", Order = 5)]
		public Brush PendingCisdTextBrush { get; set; }
		[Browsable(false)]
		public string PendingCisdTextBrushSerializable { get { return Serialize.BrushToString(PendingCisdTextBrush); } set { PendingCisdTextBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bull CISD Color", GroupName = "03. CISD", Order = 6)]
		public Brush BullCisdBrush { get; set; }
		[Browsable(false)]
		public string BullCisdBrushSerializable { get { return Serialize.BrushToString(BullCisdBrush); } set { BullCisdBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bear CISD Color", GroupName = "03. CISD", Order = 7)]
		public Brush BearCisdBrush { get; set; }
		[Browsable(false)]
		public string BearCisdBrushSerializable { get { return Serialize.BrushToString(BearCisdBrush); } set { BearCisdBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Pending CISD Line Width", GroupName = "03. CISD", Order = 8)]
		public int PendingCisdLineWidth { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Confirmed CISD Line Width", GroupName = "03. CISD", Order = 9)]
		public int ConfirmedCisdLineWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use HTF PDA Projector", GroupName = "03. PDA / Imbalance", Order = 0)]
		public bool UseHtfPdaProjector { get; set; }

		[NinjaScriptProperty]
		[Range(5, 240)]
		[Display(Name = "HTF PDA Projector Minutes", GroupName = "03. PDA / Imbalance", Order = 1)]
		public int HtfPdaProjectorMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show HTF PDA Projector Visuals", GroupName = "03. PDA / Imbalance", Order = 2)]
		public bool ShowHtfPdaProjectorVisuals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Panel", GroupName = "04. Visual", Order = 0)]
		public bool ShowPanel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw T-Spot Zone", GroupName = "04. Visual", Order = 1)]
		public bool DrawTSpotZone { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "T-Spot Opacity", GroupName = "04. Visual", Order = 2)]
		public int TSpotOpacity { get; set; }

		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "T-Spot Start Offset Bars", GroupName = "04. Visual", Order = 3)]
		public int TSpotStartOffsetBars { get; set; }

		[NinjaScriptProperty]
		[Range(2, 100)]
		[Display(Name = "T-Spot Projection Bars", GroupName = "04. Visual", Order = 4)]
		public int TSpotProjectionBars { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bull T-Spot Color", GroupName = "04. Visual", Order = 5)]
		public Brush BullTSpotBrush { get; set; }
		[Browsable(false)]
		public string BullTSpotBrushSerializable { get { return Serialize.BrushToString(BullTSpotBrush); } set { BullTSpotBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bear T-Spot Color", GroupName = "04. Visual", Order = 6)]
		public Brush BearTSpotBrush { get; set; }
		[Browsable(false)]
		public string BearTSpotBrushSerializable { get { return Serialize.BrushToString(BearTSpotBrush); } set { BearTSpotBrush = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[Display(Name = "Show C2/C3/C4 Labels", GroupName = "04. Visual", Order = 7)]
		public bool ShowCLabels { get; set; }

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "C Label Lookback Bars", GroupName = "04. Visual", Order = 8)]
		public int CLabelLookbackBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "C Label Tick Offset", GroupName = "04. Visual", Order = 9)]
		public int CLabelTickOffset { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Debug Print", GroupName = "04. Visual", Order = 10)]
		public bool DebugPrint { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Export Context Snapshot", GroupName = "05. Codex Bridge", Order = 0)]
		public bool ExportContextSnapshot { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Context Snapshot File", GroupName = "05. Codex Bridge", Order = 1)]
		public string ContextSnapshotFile { get; set; }
	}
}
