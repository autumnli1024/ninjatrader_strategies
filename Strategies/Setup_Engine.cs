#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class Setup_Engine : Strategy
	{
		private ICT_Setup_Engine setupEngine;
		private ICT_Arbitrator arbitrator;
		private ICT_Risk_Manager riskManager;
		private ICTEventLatch eventLatch;
		private ICTPanelState panel = new ICTPanelState();
		private TimeCyclesLiquiditySweep tc240;
		private TimeCyclesLiquiditySweep tc90;
		private TimeCyclesLiquiditySweep tc60;
		private TimeCyclesLiquiditySweep tc15;
		private ICT_SMT_MTF smt;
		private ICT_HTF_PDA_Projector htfPdaProjector;
		private ICT_Structure_Suite structureH1;
		private ICT_Structure_Suite structureM15;
		private ICT_Structure_Suite structureM5;
		private ICT_PDA_Suite pda15;
		private ICT_PDA_Suite pdaExec;
		private ICT_HTF_Suite_Ultimate htfSuite;

		private DateTime tradeDate = Core.Globals.MinDate;
		private double dayStartCumProfit;
		private int tradesToday;

		private double priorDayHigh = double.NaN;
		private double priorDayLow = double.NaN;
		private double workingDayHigh = double.NaN;
		private double workingDayLow = double.NaN;
		private double sessionRangeHigh = double.NaN;
		private double sessionRangeLow = double.NaN;
		private DateTime sessionRangeStart = Core.Globals.MinDate;
		private string sessionRangeName = string.Empty;

		private double recentSwingHigh = double.NaN;
		private double recentSwingLow = double.NaN;
		private double previousSwingHigh = double.NaN;
		private double previousSwingLow = double.NaN;
		private readonly double[] swingHighDriveValues = new double[] { double.NaN, double.NaN, double.NaN };
		private readonly double[] swingLowDriveValues = new double[] { double.NaN, double.NaN, double.NaN };
		private readonly int[] swingHighDriveBars = new int[] { -1, -1, -1 };
		private readonly int[] swingLowDriveBars = new int[] { -1, -1, -1 };

		private double bullFvgTop = double.NaN;
		private double bullFvgBottom = double.NaN;
		private double bearFvgTop = double.NaN;
		private double bearFvgBottom = double.NaN;

		private double bullObTop = double.NaN;
		private double bullObBottom = double.NaN;
		private double bearObTop = double.NaN;
		private double bearObBottom = double.NaN;

		private int lastEntryBar = -1;
		private string lastDebugStatus = string.Empty;
		private string lastDebugReason = string.Empty;
		private bool entryOrderPending;
		private bool activeTradeCounted;
		private bool stopMovedToBreakEven;
		private bool activeEntryThreePush;
		private bool activeSplitEntries;
		private bool targetsSubmitted;
		private bool partialTargetFilled;
		private bool pendingStopMove;
		private int activeInitialQuantity;
		private int activePartialQuantity;
		private int activeRunnerQuantity;
		private Order activeEntryOrder;
		private Order activeStopOrder;
		private List<Order> activeStopOrders = new List<Order>();
		private List<Order> activeTargetOrders = new List<Order>();
		private int entrySubmitBar = -1;
		private string activeEntryName = string.Empty;
		private string activePartialEntryName = string.Empty;
		private string activeRunnerEntryName = string.Empty;
		private ICTTradeDirection activeDirection = ICTTradeDirection.None;
		private double activeEntryPrice = double.NaN;
		private double activeStopPrice = double.NaN;
		private double activeTargetPrice = double.NaN;
		private double activePartialTargetPrice = double.NaN;
		private double activeRunnerTargetPrice = double.NaN;
		private double pendingStopMovePrice = double.NaN;
		private string pendingStopMoveReason = string.Empty;
		private string lastOrderMessage = string.Empty;
		private Grid controlPanelGrid;
		private TextBlock controlPanelStatus;
		private bool setupEngineRebuildRequested;
		private bool controlPanelUpdatePending;
		private bool controlPanelDragging;
		private Point controlPanelDragStart;
		private Thickness controlPanelDragMargin;
		private ICTDealingRangeMode lastDrBoxMode = ICTDealingRangeMode.LiveCycle;
		private int lastDrBoxActiveCount = -1;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "Setup_Engine";
				Description = "Modular setup engine strategy: ICT2022, TurtleSoup, IFVG-CISD, breaker, unicorn, OB-CISD.";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 2;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsInstantiatedOnEachOptimizationIteration = false;
				BarsRequiredToTrade = 80;

				EnableTrading = true;
				ManualExitOverride = false;
				ShowPanel = true;
				ShowDetailedPanel = true;
				ShowControlPanel = true;
				ShowDealingRangeLines = true;
				UseThreePushBonus = true;
				ThreePushBonusScore = 8;
				ThreePushQuantity = 2;
				ThreePushLookbackBars = 80;
				DealingRangeLineLookbackBars = 120;
				DealingRangeMaxHistory = 3;
				DealingRangeCycleMinutes = 90;
				SetupDealingRangeMode = ICTDealingRangeMode.CompletedCycle;
				VisualDealingRangeMode = ICTDealingRangeMode.LiveCycle;
				VisualOteFibDirection = ICTOteFibDirection.Auto;
				DebugPrint = false;
				TradeLong = true;
				TradeShort = true;
				UseICT2022Setup = true;
				UseIFVGCISDSetup = true;
				UseBreakerSetup = true;
				UseUnicornSetup = true;
				UseOTESetup = true;
				UseOBCISDSetup = true;
				UseTurtleSoupSetup = true;

				MinimumScore = 70;
				MinimumRR = 1.2;
				FixedQuantity = 1;
				UseFixedQuantity = true;
				AccountRiskDollars = 100;
				MaxDailyLossDollars = 500;
				MaxTradesPerDay = 0;
				MaxStopPoints = 80;
				UseBreakEven = true;
				BreakEvenAtR = 1.0;
				BreakEvenPlusTicks = 1;
				UsePartialAt1R = true;
				PartialAtR = 1.0;
				RunnerTargetR = 2.0;
				PartialQuantity = 1;
				ManualAdjustTicks = 4;
				ProtectiveStopBufferTicks = 2;
				UseLimitEntries = true;
				AllowMarketIfEntryTouched = false;
				EntryTimeoutBars = 6;
				UseAtrTrailingStop = false;
				TrailAfterR = 1.5;
				TrailAtrMultiple = 1.0;

				SwingLength = 3;
				CisdLookback = 5;
				AtrPeriod = 14;
				MinBarsBetweenEntries = 3;
				SweepTtlBars = 20;
				MssTtlBars = 12;
				CisdTtlBars = 8;
				PdaTtlBars = 10;
				SmtTtlBars = 80;
				UseTimeCycleInputs = true;
				TimeCycleStartHour = 18;
				TimeCycleStartMinute = 0;
				UseSmtInputs = true;
				SmtAssetA = "MES 06-26";
				SmtAssetB = "MYM 06-26";
				ShowSmtVisuals = true;
				ShowSmtLabels = true;
				SmtTF5 = true;
				SmtTF15 = true;
				SmtTF30 = true;
				SmtTF60 = true;
				SmtTF90 = true;
				SmtTF240 = true;
				UseStructureInputs = true;
				ShowStructureVisuals = false;
				UsePdaInputs = true;
				UseHtfPdaProjectorInputs = true;
				UseM1ExecutionPda = true;
				UseHtfSuiteTargets = true;
				UseKillZones = true;
				RequireKillZoneForEntry = true;
				AsianStart = 2000;
				AsianEnd = 0;
				LondonStart = 200;
				LondonEnd = 500;
				NewYorkAMStart = 830;
				NewYorkAMEnd = 1100;
				NewYorkPMStart = 1330;
				NewYorkPMEnd = 1600;
			}
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Minute, 240);
				AddDataSeries(BarsPeriodType.Minute, 90);
				AddDataSeries(BarsPeriodType.Minute, 60);
				AddDataSeries(BarsPeriodType.Minute, 30);
				AddDataSeries(BarsPeriodType.Minute, 15);
				AddDataSeries(BarsPeriodType.Minute, 5);
				AddDataSeries(BarsPeriodType.Minute, 1);

				if (UseSmtInputs)
				{
					AddSmtDataSeries(SmtAssetA);
					AddSmtDataSeries(SmtAssetB);
				}
			}
			else if (State == State.DataLoaded)
			{
				RebuildSetupEngine();
				arbitrator = new ICT_Arbitrator { MinimumScore = MinimumScore, MinimumRR = MinimumRR };
				riskManager = new ICT_Risk_Manager
				{
					FixedQuantity = FixedQuantity,
					UseFixedQuantity = UseFixedQuantity,
					AccountRiskDollars = AccountRiskDollars,
					MaxDailyLossDollars = MaxDailyLossDollars,
					MaxTradesPerDay = MaxTradesPerDay,
					MaxStopPoints = MaxStopPoints
				};
				eventLatch = new ICTEventLatch
				{
					SweepTtlBars = SweepTtlBars,
					MssTtlBars = MssTtlBars,
					CisdTtlBars = CisdTtlBars,
					PdaTtlBars = PdaTtlBars,
					SmtTtlBars = SmtTtlBars
				};

				if (UseTimeCycleInputs)
				{
					tc240 = TimeCyclesLiquiditySweep(TimeCycleStartHour, TimeCycleStartMinute, 240, false, Brushes.RoyalBlue, "Dash", 1, false, Brushes.OrangeRed, Brushes.Crimson, Brushes.MediumSeaGreen, Brushes.Black, Brushes.Black, 2, "X", true, 1, Brushes.Crimson, false, false, true, Brushes.MediumPurple, "Dot", false, Brushes.SteelBlue, 8, false, DealingRangeMaxHistory);
					tc90 = TimeCyclesLiquiditySweep(TimeCycleStartHour, TimeCycleStartMinute, 90, false, Brushes.RoyalBlue, "Dash", 1, false, Brushes.OrangeRed, Brushes.Crimson, Brushes.MediumSeaGreen, Brushes.Black, Brushes.Black, 2, "X", true, 1, Brushes.Crimson, false, false, true, Brushes.MediumPurple, "Dot", false, Brushes.SteelBlue, 8, false, DealingRangeMaxHistory);
					tc60 = TimeCyclesLiquiditySweep(TimeCycleStartHour, TimeCycleStartMinute, 60, false, Brushes.RoyalBlue, "Dash", 1, false, Brushes.OrangeRed, Brushes.Crimson, Brushes.MediumSeaGreen, Brushes.Black, Brushes.Black, 2, "X", true, 1, Brushes.Crimson, false, false, true, Brushes.MediumPurple, "Dot", false, Brushes.SteelBlue, 8, false, DealingRangeMaxHistory);
					tc15 = TimeCyclesLiquiditySweep(TimeCycleStartHour, TimeCycleStartMinute, 15, false, Brushes.RoyalBlue, "Dash", 1, false, Brushes.OrangeRed, Brushes.Crimson, Brushes.MediumSeaGreen, Brushes.Black, Brushes.Black, 2, "X", true, 1, Brushes.Crimson, false, false, true, Brushes.MediumPurple, "Dot", false, Brushes.SteelBlue, 8, false, DealingRangeMaxHistory);
				}

				if (UseSmtInputs)
				{
					smt = ICT_SMT_MTF(true, SmtAssetA, true, SmtAssetB, 3, false, SmtTF240, SmtTF90, SmtTF60, SmtTF30, SmtTF15, false, true, 0.5, 10, 240, "Dashed", 80, ShowSmtVisuals, ShowSmtLabels, Brushes.LimeGreen, Brushes.Crimson, Brushes.Gray, 4, 3, 3, 2, 2, new SimpleFont("Arial", 10));
					ApplySmtVisualConfig();
					if (ShowSmtVisuals)
						AddChartIndicator(smt);
				}

				if (UsePdaInputs)
				{
					pda15 = CreatePda(Closes[5]);
					pdaExec = CreatePda(UseM1ExecutionPda ? Closes[7] : Closes[6]);
				}

				if (UseStructureInputs)
				{
					structureH1 = CreateStructure(Closes[3], false);
					structureM15 = CreateStructure(Closes[5], false);
					structureM5 = CreateStructure(Closes[6], ShowStructureVisuals);
					if (ShowStructureVisuals)
						AddChartIndicator(structureM5);
				}

				if (UseHtfPdaProjectorInputs)
					htfPdaProjector = ICT_HTF_PDA_Projector(60, 12, 2, false, PDAFillType.CLOSE_THROUGH, true, 24, true, true, true, true, 22, 8, 2, new SimpleFont("Arial", 10), Brushes.MediumAquamarine, Brushes.LightCoral, Brushes.Black, Brushes.Black);

				if (UseHtfSuiteTargets)
					htfSuite = ICT_HTF_Suite_Ultimate(Input);
			}
			else if (State == State.Historical)
			{
				AddControlPanel();
			}
			else if (State == State.Terminated)
			{
				RemoveControlPanel();
			}
		}

		private ICT_PDA_Suite CreatePda(ISeries<double> input)
		{
			return ICT_PDA_Suite(input,
				false, PDAPeriodTypes.Minute, 1,
				true, true, true, true,
				50, 30,
				true, 1.1, 10, 2,
				true, PDAFillType.CLOSE_THROUGH, false, true,
				true, 0.50,
				true, 50, 0, 0,
				false, new TimeSpan(3, 0, 0), 60,
				false, new TimeSpan(10, 0, 0), 60,
				false, new TimeSpan(14, 0, 0), 60,
				Brushes.LimeGreen, Brushes.LimeGreen, Brushes.Green,
				Brushes.Crimson, Brushes.Crimson, Brushes.DarkRed,
				Brushes.DeepSkyBlue, Brushes.DodgerBlue,
				Brushes.Goldenrod, Brushes.MediumPurple,
				20, 15, 16, 18, 20,
				false, TextPosition.TopRight, new SimpleFont("Verdana", 12),
				Brushes.WhiteSmoke, Brushes.DimGray, Brushes.DarkSlateGray, 50);
		}

		private ICT_Structure_Suite CreateStructure(ISeries<double> input, bool showVisuals)
		{
			return ICT_Structure_Suite(input,
				SwingLength,
				CisdLookback,
				false,
				0.5,
				showVisuals,
				showVisuals,
				Brushes.LimeGreen,
				Brushes.Crimson,
				Brushes.Goldenrod,
				2,
				new SimpleFont("Arial", 10));
		}

		private void AddSmtDataSeries(string instrumentName)
		{
			if (string.IsNullOrWhiteSpace(instrumentName))
				return;

			AddDataSeries(instrumentName, BarsPeriodType.Minute, 240);
			AddDataSeries(instrumentName, BarsPeriodType.Minute, 90);
			AddDataSeries(instrumentName, BarsPeriodType.Minute, 60);
			AddDataSeries(instrumentName, BarsPeriodType.Minute, 30);
			AddDataSeries(instrumentName, BarsPeriodType.Minute, 15);
			AddDataSeries(instrumentName, BarsPeriodType.Minute, 5);
		}

		private void RebuildSetupEngine()
		{
			setupEngine = new ICT_Setup_Engine(UseICT2022Setup, UseIFVGCISDSetup, UseBreakerSetup, UseUnicornSetup, UseOTESetup, UseOBCISDSetup, UseTurtleSoupSetup);
			setupEngineRebuildRequested = false;
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0)
				return;
			if (!HasEnoughBars())
				return;
			if (setupEngineRebuildRequested)
				RebuildSetupEngine();

			UpdateDailyState();
			UpdateSessionRangeState();
			UpdateMarketPrimitives();
			ManagePendingEntry();
			ManageOpenPosition();
			TryApplyPendingStopMove();
			ApplySmtVisualConfig();

			ICTMarketContext context = BuildContext();
			List<ICTSetupSignal> signals = setupEngine.Evaluate(context);
			ApplyThreePushBonus(signals, context);
			string blockReason;
			arbitrator.RequireKillZone = RequireKillZoneForEntry;
			ICTSetupSignal best = arbitrator.SelectBest(signals, context, out blockReason);

			panel.BestSignal = best;
			panel.Context = context;
			panel.BlockReason = blockReason;
			panel.TradesToday = tradesToday;
			panel.DailyPnL = GetDailyPnL();
			UpdateScores(signals);
			DebugState(context, best);

			if (EnableTrading && best != null && best.IsValid && Position.MarketPosition == MarketPosition.Flat && !entryOrderPending && CurrentBar - lastEntryBar >= MinBarsBetweenEntries)
				TryEnter(best);

			DrawDealingRange();
			UpdatePanel();
		}

		private bool HasEnoughBars()
		{
			if (CurrentBar < BarsRequiredToTrade)
				return false;
			int requiredCoreSeries = Math.Min(CurrentBars.Length, 8);
			for (int i = 0; i < requiredCoreSeries; i++)
				if (CurrentBars[i] < Math.Max(BarsRequiredToTrade, 30))
					return false;
			return true;
		}

		private void UpdateDailyState()
		{
			DateTime d = Time[0].Date;
			if (tradeDate != d)
			{
				if (!double.IsNaN(workingDayHigh))
				{
					priorDayHigh = workingDayHigh;
					priorDayLow = workingDayLow;
				}
				workingDayHigh = High[0];
				workingDayLow = Low[0];
				tradeDate = d;
				tradesToday = 0;
				dayStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
			}
			else
			{
				workingDayHigh = double.IsNaN(workingDayHigh) ? High[0] : Math.Max(workingDayHigh, High[0]);
				workingDayLow = double.IsNaN(workingDayLow) ? Low[0] : Math.Min(workingDayLow, Low[0]);
			}
		}

		private void UpdateSessionRangeState()
		{
			string kzName = "All";
			bool inKz = !UseKillZones || IsInKillZone(ToTime(Time[0]), out kzName);
			if (!inKz)
			{
				sessionRangeName = string.Empty;
				sessionRangeStart = Core.Globals.MinDate;
				return;
			}

			if (sessionRangeName != kzName)
			{
				sessionRangeName = kzName;
				sessionRangeStart = Time[0];
				sessionRangeHigh = High[0];
				sessionRangeLow = Low[0];
				return;
			}

			sessionRangeHigh = double.IsNaN(sessionRangeHigh) ? High[0] : Math.Max(sessionRangeHigh, High[0]);
			sessionRangeLow = double.IsNaN(sessionRangeLow) ? Low[0] : Math.Min(sessionRangeLow, Low[0]);
		}

		private void UpdateMarketPrimitives()
		{
			UpdateSwings();
			UpdateFvgAndIfvgSeeds();
			UpdateOrderBlockSeeds();
		}

		private void UpdateSwings()
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
				previousSwingHigh = recentSwingHigh;
				recentSwingHigh = ph;
				PushDriveSwing(swingHighDriveValues, swingHighDriveBars, ph, CurrentBar - SwingLength);
			}
			if (isLow)
			{
				previousSwingLow = recentSwingLow;
				recentSwingLow = pl;
				PushDriveSwing(swingLowDriveValues, swingLowDriveBars, pl, CurrentBar - SwingLength);
			}
		}

		private void PushDriveSwing(double[] values, int[] bars, double value, int bar)
		{
			if (bars[0] == bar)
				return;
			values[2] = values[1];
			values[1] = values[0];
			values[0] = value;
			bars[2] = bars[1];
			bars[1] = bars[0];
			bars[0] = bar;
		}

		private void UpdateFvgAndIfvgSeeds()
		{
			if (Low[0] > High[2])
			{
				bullFvgTop = Low[0];
				bullFvgBottom = High[2];
			}
			if (High[0] < Low[2])
			{
				bearFvgTop = Low[2];
				bearFvgBottom = High[0];
			}
		}

		private void UpdateOrderBlockSeeds()
		{
			if (!double.IsNaN(recentSwingHigh) && Close[0] > recentSwingHigh)
			{
				int idx = FindLastBearishCandle(10);
				if (idx > 0)
				{
					bullObTop = High[idx];
					bullObBottom = Low[idx];
				}
			}
			if (!double.IsNaN(recentSwingLow) && Close[0] < recentSwingLow)
			{
				int idx = FindLastBullishCandle(10);
				if (idx > 0)
				{
					bearObTop = High[idx];
					bearObBottom = Low[idx];
				}
			}
		}

		private ICTMarketContext BuildContext()
		{
			ICTFactSnapshot snapshot = BuildSnapshot();
			eventLatch.Update(snapshot);
			return eventLatch.ToContext(snapshot);
		}

		private ICTFactSnapshot BuildSnapshot()
		{
			double atr = GetAtr(AtrPeriod);
			double drHigh = GetDealingRangeHigh(SetupDealingRangeMode);
			double drLow = GetDealingRangeLow(SetupDealingRangeMode);
			double mid = (drHigh + drLow) * 0.5;
			bool bullMss = false;
			bool bearMss = false;
			bool bullCisd = false;
			bool bearCisd = false;
			bool bullIfvg = !double.IsNaN(bearFvgTop) && Close[0] > bearFvgTop;
			bool bearIfvg = !double.IsNaN(bullFvgBottom) && Close[0] < bullFvgBottom;
			bool bullBreaker = !double.IsNaN(bearObTop) && Close[0] > bearObTop;
			bool bearBreaker = !double.IsNaN(bullObBottom) && Close[0] < bullObBottom;
			bool bullOb = !double.IsNaN(bullObTop) && Low[0] <= bullObTop && Close[0] >= bullObBottom;
			bool bearOb = !double.IsNaN(bearObBottom) && High[0] >= bearObBottom && Close[0] <= bearObTop;
			bool bullFvg = Low[0] > High[2];
			bool bearFvg = High[0] < Low[2];
			double pureBullFvgTop = bullFvg ? Math.Max(Low[0], High[2]) : double.NaN;
			double pureBullFvgBottom = bullFvg ? Math.Min(Low[0], High[2]) : double.NaN;
			double pureBearFvgTop = bearFvg ? Math.Max(Low[2], High[0]) : double.NaN;
			double pureBearFvgBottom = bearFvg ? Math.Min(Low[2], High[0]) : double.NaN;
			bool sweptHigh = IsBuySideSweep();
			bool sweptLow = IsSellSideSweep();
			bool threePushHigh = IsThreePushHigh(sweptHigh);
			bool threePushLow = IsThreePushLow(sweptLow);
			double bullPdaTop = GetBullPdaTop(bullIfvg, bullFvg);
			double bullPdaBottom = GetBullPdaBottom(bullIfvg, bullFvg);
			double bearPdaTop = GetBearPdaTop(bearIfvg, bearFvg);
			double bearPdaBottom = GetBearPdaBottom(bearIfvg, bearFvg);

			if (UseTimeCycleInputs && tc15 != null)
			{
				sweptHigh = sweptHigh || SafeSeriesValue(tc15.PchSweepSignal, 0) == 1;
				sweptLow = sweptLow || SafeSeriesValue(tc15.PclSweepSignal, 0) == 1;
			}
			if (UseStructureInputs)
				MergeStructureFacts(ref bullMss, ref bearMss, ref bullCisd, ref bearCisd);

			if (UsePdaInputs)
				MergePdaFacts(ref bullFvg, ref bearFvg, ref bullIfvg, ref bearIfvg, ref pureBullFvgTop, ref pureBullFvgBottom, ref pureBearFvgTop, ref pureBearFvgBottom, ref bullPdaTop, ref bullPdaBottom, ref bearPdaTop, ref bearPdaBottom);
			if (UseHtfPdaProjectorInputs)
				MergeHtfPdaProjectorFacts(ref bullPdaTop, ref bullPdaBottom, ref bearPdaTop, ref bearPdaBottom);

			bool bullSmt = UseSmtInputs && smt != null && SafeSeriesValue(smt.BullSmtSignal, 0) > 0;
			bool bearSmt = UseSmtInputs && smt != null && SafeSeriesValue(smt.BearSmtSignal, 0) < 0;
			int smtTimeframe = 0;
			if (bullSmt)
				smtTimeframe = (int)SafeSeriesValue(smt.BullSmtTimeframe, 0);
			else if (bearSmt)
				smtTimeframe = (int)SafeSeriesValue(smt.BearSmtTimeframe, 0);

			string killZoneName = "Off";
			bool inKillZone = !UseKillZones || IsInKillZone(ToTime(Time[0]), out killZoneName);
			int higherBias = GetHigherBias();
			if (UseHtfPdaProjectorInputs && htfPdaProjector != null && higherBias == 0)
			{
				double projectedBias = SafeSeriesValue(htfPdaProjector.ActiveBias, 0);
				if (projectedBias > 0) higherBias = 1;
				else if (projectedBias < 0) higherBias = -1;
			}
			List<ICTKeyLevelFact> htfKeyLevels = ReadHtfSuiteFacts();

			return new ICTFactSnapshot
			{
				CurrentBar = CurrentBar,
				Time = Time[0],
				Open = Open[0],
				High = High[0],
				Low = Low[0],
				Close = Close[0],
				Atr = atr,
				TickSize = TickSize,
				PointValue = Instrument.MasterInstrument.PointValue,
				InKillZone = inKillZone,
				KillZoneName = killZoneName,
				HigherBias = higherBias,
				DealingRangeHigh = drHigh,
				DealingRangeLow = drLow,
				DealingRangeMid = mid,
				InPremium = Close[0] >= mid,
				InDiscount = Close[0] <= mid,
				RecentSwingHigh = double.IsNaN(recentSwingHigh) ? High[0] : recentSwingHigh,
				RecentSwingLow = double.IsNaN(recentSwingLow) ? Low[0] : recentSwingLow,
				PriorDayHigh = priorDayHigh,
				PriorDayLow = priorDayLow,
				SellsideSweepEvent = sweptLow,
				BuysideSweepEvent = sweptHigh,
				BullMssEvent = bullMss,
				BearMssEvent = bearMss,
				BullCisdEvent = bullCisd,
				BearCisdEvent = bearCisd,
				BullFvgEvent = bullFvg,
				BearFvgEvent = bearFvg,
				BullIfvgEvent = bullIfvg,
				BearIfvgEvent = bearIfvg,
				BullBreakerEvent = bullBreaker,
				BearBreakerEvent = bearBreaker,
				BullObEvent = bullOb,
				BearObEvent = bearOb,
				BullUnicornEvent = bullIfvg && bullBreaker,
				BearUnicornEvent = bearIfvg && bearBreaker,
				BullSmtEvent = bullSmt,
				BearSmtEvent = bearSmt,
				SmtTimeframe = smtTimeframe,
				ThreePushHighEvent = threePushHigh,
				ThreePushLowEvent = threePushLow,
				BullFvgTop = pureBullFvgTop,
				BullFvgBottom = pureBullFvgBottom,
				BearFvgTop = pureBearFvgTop,
				BearFvgBottom = pureBearFvgBottom,
				BullPdaTop = bullPdaTop,
				BullPdaBottom = bullPdaBottom,
				BearPdaTop = bearPdaTop,
				BearPdaBottom = bearPdaBottom,
				HtfKeyLevels = htfKeyLevels
			};
		}

		private void MergeStructureFacts(ref bool bullMss, ref bool bearMss, ref bool bullCisd, ref bool bearCisd)
		{
			MergeStructureSource(structureH1, ref bullMss, ref bearMss, ref bullCisd, ref bearCisd);
			MergeStructureSource(structureM15, ref bullMss, ref bearMss, ref bullCisd, ref bearCisd);
			MergeStructureSource(structureM5, ref bullMss, ref bearMss, ref bullCisd, ref bearCisd);
		}

		private void MergeStructureSource(IICTStructureFactSource source, ref bool bullMss, ref bool bearMss, ref bool bullCisd, ref bool bearCisd)
		{
			if (source == null)
				return;
			bullMss = bullMss || SafeSeriesValue(source.BullBosSignal, 0) > 0 || SafeSeriesValue(source.BullChochSignal, 0) > 0;
			bearMss = bearMss || SafeSeriesValue(source.BearBosSignal, 0) < 0 || SafeSeriesValue(source.BearChochSignal, 0) < 0;
			bullCisd = bullCisd || SafeSeriesValue(source.BullCisdSignal, 0) > 0;
			bearCisd = bearCisd || SafeSeriesValue(source.BearCisdSignal, 0) < 0;
		}

		private List<ICTKeyLevelFact> ReadHtfSuiteFacts()
		{
			if (!UseHtfSuiteTargets || htfSuite == null)
				return new List<ICTKeyLevelFact>();

			List<ICTKeyLevelFact> facts = htfSuite.GetKeyLevelFacts();
			return facts == null ? new List<ICTKeyLevelFact>() : facts;
		}

		private void MergePdaFacts(ref bool bullFvg, ref bool bearFvg, ref bool bullIfvg, ref bool bearIfvg, ref double pureBullFvgTop, ref double pureBullFvgBottom, ref double pureBearFvgTop, ref double pureBearFvgBottom, ref double bullPdaTop, ref double bullPdaBottom, ref double bearPdaTop, ref double bearPdaBottom)
		{
			ICT_PDA_Suite source = pdaExec ?? pda15;
			ICT_PDA_Suite context = pda15 ?? pdaExec;

			if (source != null)
			{
				bullFvg = bullFvg || SafeSeriesValue(source.BullFvgSignal, 0) > 0;
				bearFvg = bearFvg || SafeSeriesValue(source.BearFvgSignal, 0) < 0;
				bullIfvg = bullIfvg || SafeSeriesValue(source.BullIfvgSignal, 0) > 0;
				bearIfvg = bearIfvg || SafeSeriesValue(source.BearIfvgSignal, 0) < 0;
			}

			if (context == null)
				return;

			double bt = FirstValid(
				SafeSeriesValue(context.NearestBullIfvgTop, 0),
				SafeSeriesValue(context.NearestBullBprTop, 0),
				SafeSeriesValue(context.NearestUnicornTop, 0),
				SafeSeriesValue(context.NearestBullFvgTop, 0));
			double bb = FirstValid(
				SafeSeriesValue(context.NearestBullIfvgBottom, 0),
				SafeSeriesValue(context.NearestBullBprBottom, 0),
				SafeSeriesValue(context.NearestUnicornBottom, 0),
				SafeSeriesValue(context.NearestBullFvgBottom, 0));
			double st = FirstValid(
				SafeSeriesValue(context.NearestBearIfvgTop, 0),
				SafeSeriesValue(context.NearestBearBprTop, 0),
				SafeSeriesValue(context.NearestUnicornTop, 0),
				SafeSeriesValue(context.NearestBearFvgTop, 0));
			double sb = FirstValid(
				SafeSeriesValue(context.NearestBearIfvgBottom, 0),
				SafeSeriesValue(context.NearestBearBprBottom, 0),
				SafeSeriesValue(context.NearestUnicornBottom, 0),
				SafeSeriesValue(context.NearestBearFvgBottom, 0));

			double fbt = SafeSeriesValue(context.NearestBullFvgTop, 0);
			double fbb = SafeSeriesValue(context.NearestBullFvgBottom, 0);
			double fst = SafeSeriesValue(context.NearestBearFvgTop, 0);
			double fsb = SafeSeriesValue(context.NearestBearFvgBottom, 0);
			if (bullFvg && IsValidPrice(fbt) && IsValidPrice(fbb))
			{
				pureBullFvgTop = Math.Max(fbt, fbb);
				pureBullFvgBottom = Math.Min(fbt, fbb);
			}
			if (bearFvg && IsValidPrice(fst) && IsValidPrice(fsb))
			{
				pureBearFvgTop = Math.Max(fst, fsb);
				pureBearFvgBottom = Math.Min(fst, fsb);
			}

			if (IsValidPrice(bt) && IsValidPrice(bb))
			{
				bullPdaTop = Math.Max(bt, bb);
				bullPdaBottom = Math.Min(bt, bb);
			}
			if (IsValidPrice(st) && IsValidPrice(sb))
			{
				bearPdaTop = Math.Max(st, sb);
				bearPdaBottom = Math.Min(st, sb);
			}
		}

		private void MergeHtfPdaProjectorFacts(ref double bullPdaTop, ref double bullPdaBottom, ref double bearPdaTop, ref double bearPdaBottom)
		{
			if (htfPdaProjector == null)
				return;

			double bt = SafeSeriesValue(htfPdaProjector.NearestBullTop, 0);
			double bb = SafeSeriesValue(htfPdaProjector.NearestBullBottom, 0);
			double st = SafeSeriesValue(htfPdaProjector.NearestBearTop, 0);
			double sb = SafeSeriesValue(htfPdaProjector.NearestBearBottom, 0);

			if (IsValidZone(bt, bb))
			{
				bullPdaTop = Math.Max(bt, bb);
				bullPdaBottom = Math.Min(bt, bb);
			}
			if (IsValidZone(st, sb))
			{
				bearPdaTop = Math.Max(st, sb);
				bearPdaBottom = Math.Min(st, sb);
			}
		}

		private double FirstValid(params double[] values)
		{
			foreach (double value in values)
				if (IsValidPrice(value))
					return value;
			return double.NaN;
		}

		private int GetHigherBias()
		{
			double close240 = Closes[1][0];
			double close60 = Closes[3][0];
			double close15 = Closes[5][0];
			double sma240 = GetSma(1, 20);
			double sma60 = GetSma(3, 20);
			double sma15 = GetSma(5, 20);
			if (close240 > sma240 && close60 > sma60 && close15 > sma15)
				return 1;
			if (close240 < sma240 && close60 < sma60 && close15 < sma15)
				return -1;
			return 0;
		}

		private double GetSma(int barsInProgress, int period)
		{
			double sum = 0;
			for (int i = 0; i < period; i++)
				sum += Closes[barsInProgress][i];
			return sum / period;
		}

		private double GetAtr(int period)
		{
			double sum = 0;
			for (int i = 0; i < period; i++)
			{
				double prevClose = Close[i + 1];
				double tr = Math.Max(High[i] - Low[i], Math.Max(Math.Abs(High[i] - prevClose), Math.Abs(Low[i] - prevClose)));
				sum += tr;
			}
			return sum / period;
		}

		private double GetPremiumDiscountMid()
		{
			double hi = double.IsNaN(recentSwingHigh) ? MAX(High, 40)[0] : recentSwingHigh;
			double lo = double.IsNaN(recentSwingLow) ? MIN(Low, 40)[0] : recentSwingLow;
			return (hi + lo) * 0.5;
		}

		private double GetDealingRangeHigh(ICTDealingRangeMode mode)
		{
			double value = double.NaN;
			if (mode == ICTDealingRangeMode.LiveCycle)
				value = GetCycleHigh(true);
			else if (mode == ICTDealingRangeMode.CompletedCycle)
				value = GetCycleHigh(false);
			else if (mode == ICTDealingRangeMode.SessionRange)
				value = sessionRangeHigh;
			else if (mode == ICTDealingRangeMode.SwingRange)
				value = double.IsNaN(recentSwingHigh) ? MAX(High, 40)[0] : recentSwingHigh;

			if (IsValidPrice(value))
				return value;

			if (!double.IsNaN(priorDayHigh) && !double.IsNaN(priorDayLow))
				return Math.Max(priorDayHigh, priorDayLow);
			return double.IsNaN(recentSwingHigh) ? MAX(High, 40)[0] : recentSwingHigh;
		}

		private double GetDealingRangeLow(ICTDealingRangeMode mode)
		{
			double value = double.NaN;
			if (mode == ICTDealingRangeMode.LiveCycle)
				value = GetCycleLow(true);
			else if (mode == ICTDealingRangeMode.CompletedCycle)
				value = GetCycleLow(false);
			else if (mode == ICTDealingRangeMode.SessionRange)
				value = sessionRangeLow;
			else if (mode == ICTDealingRangeMode.SwingRange)
				value = double.IsNaN(recentSwingLow) ? MIN(Low, 40)[0] : recentSwingLow;

			if (IsValidPrice(value))
				return value;

			if (!double.IsNaN(priorDayHigh) && !double.IsNaN(priorDayLow))
				return Math.Min(priorDayHigh, priorDayLow);
			return double.IsNaN(recentSwingLow) ? MIN(Low, 40)[0] : recentSwingLow;
		}

		private double GetCycleHigh(bool live)
		{
			TimeCyclesLiquiditySweep selected = GetSelectedDealingRangeCycle();
			if (UseTimeCycleInputs && selected != null)
			{
				double selectedHigh = SafeSeriesValue(live ? selected.CurrentCycleHigh : selected.LastCycleHigh, 0);
				if (IsValidPrice(selectedHigh))
					return selectedHigh;
			}

			if (UseTimeCycleInputs && tc90 != null)
			{
				double h90 = SafeSeriesValue(live ? tc90.CurrentCycleHigh : tc90.LastCycleHigh, 0);
				if (IsValidPrice(h90))
					return h90;
			}
			if (UseTimeCycleInputs && tc60 != null)
			{
				double h60 = SafeSeriesValue(live ? tc60.CurrentCycleHigh : tc60.LastCycleHigh, 0);
				if (IsValidPrice(h60))
					return h60;
			}
			if (UseTimeCycleInputs && tc240 != null)
			{
				double h240 = SafeSeriesValue(live ? tc240.CurrentCycleHigh : tc240.LastCycleHigh, 0);
				if (IsValidPrice(h240))
					return h240;
			}
			return double.NaN;
		}

		private double GetCycleLow(bool live)
		{
			TimeCyclesLiquiditySweep selected = GetSelectedDealingRangeCycle();
			if (UseTimeCycleInputs && selected != null)
			{
				double selectedLow = SafeSeriesValue(live ? selected.CurrentCycleLow : selected.LastCycleLow, 0);
				if (IsValidPrice(selectedLow))
					return selectedLow;
			}

			if (UseTimeCycleInputs && tc90 != null)
			{
				double l90 = SafeSeriesValue(live ? tc90.CurrentCycleLow : tc90.LastCycleLow, 0);
				if (IsValidPrice(l90))
					return l90;
			}
			if (UseTimeCycleInputs && tc60 != null)
			{
				double l60 = SafeSeriesValue(live ? tc60.CurrentCycleLow : tc60.LastCycleLow, 0);
				if (IsValidPrice(l60))
					return l60;
			}
			if (UseTimeCycleInputs && tc240 != null)
			{
				double l240 = SafeSeriesValue(live ? tc240.CurrentCycleLow : tc240.LastCycleLow, 0);
				if (IsValidPrice(l240))
					return l240;
			}
			return double.NaN;
		}

		private TimeCyclesLiquiditySweep GetSelectedDealingRangeCycle()
		{
			if (DealingRangeCycleMinutes == 240)
				return tc240;
			if (DealingRangeCycleMinutes == 90)
				return tc90;
			if (DealingRangeCycleMinutes == 60)
				return tc60;
			if (DealingRangeCycleMinutes == 15)
				return tc15;
			return tc90 ?? tc60 ?? tc240 ?? tc15;
		}

		private double SafeSeriesValue(Series<double> series, int barsAgo)
		{
			if (series == null || series.Count <= barsAgo)
				return double.NaN;
			return series[barsAgo];
		}

		private bool IsValidPrice(double price)
		{
			return price > 0 && !double.IsNaN(price) && !double.IsInfinity(price);
		}

		private bool IsValidZone(double top, double bottom)
		{
			return IsValidPrice(top) && IsValidPrice(bottom) && Math.Abs(top - bottom) >= TickSize;
		}

		private double GetBullPdaTop(bool bullIfvg, bool bullFvg)
		{
			if (bullIfvg && !double.IsNaN(bearFvgTop)) return bearFvgTop;
			if (bullFvg && !double.IsNaN(bullFvgTop)) return bullFvgTop;
			if (!double.IsNaN(bullFvgTop)) return bullFvgTop;
			return double.NaN;
		}

		private double GetBullPdaBottom(bool bullIfvg, bool bullFvg)
		{
			if (bullIfvg && !double.IsNaN(bearFvgBottom)) return bearFvgBottom;
			if (bullFvg && !double.IsNaN(bullFvgBottom)) return bullFvgBottom;
			if (!double.IsNaN(bullFvgBottom)) return bullFvgBottom;
			return double.NaN;
		}

		private double GetBearPdaTop(bool bearIfvg, bool bearFvg)
		{
			if (bearIfvg && !double.IsNaN(bullFvgTop)) return bullFvgTop;
			if (bearFvg && !double.IsNaN(bearFvgTop)) return bearFvgTop;
			if (!double.IsNaN(bearFvgTop)) return bearFvgTop;
			return double.NaN;
		}

		private double GetBearPdaBottom(bool bearIfvg, bool bearFvg)
		{
			if (bearIfvg && !double.IsNaN(bullFvgBottom)) return bullFvgBottom;
			if (bearFvg && !double.IsNaN(bearFvgBottom)) return bearFvgBottom;
			if (!double.IsNaN(bearFvgBottom)) return bearFvgBottom;
			return double.NaN;
		}

		private bool IsBuySideSweep()
		{
			bool pdhSweep = !double.IsNaN(priorDayHigh) && High[0] > priorDayHigh && Close[0] < priorDayHigh;
			bool swingSweep = !double.IsNaN(recentSwingHigh) && High[0] > recentSwingHigh && Close[0] < recentSwingHigh;
			return pdhSweep || swingSweep;
		}

		private bool IsSellSideSweep()
		{
			bool pdlSweep = !double.IsNaN(priorDayLow) && Low[0] < priorDayLow && Close[0] > priorDayLow;
			bool swingSweep = !double.IsNaN(recentSwingLow) && Low[0] < recentSwingLow && Close[0] > recentSwingLow;
			return pdlSweep || swingSweep;
		}

		private bool IsThreePushHigh(bool sweptHigh)
		{
			if (HasConfirmedThreePush(swingHighDriveValues, swingHighDriveBars, true))
				return true;
			if (!sweptHigh || swingHighDriveBars[0] < 0 || swingHighDriveBars[1] < 0)
				return false;
			if (CurrentBar - swingHighDriveBars[1] > ThreePushLookbackBars)
				return false;
			return swingHighDriveValues[0] > swingHighDriveValues[1] && High[0] > swingHighDriveValues[0];
		}

		private bool IsThreePushLow(bool sweptLow)
		{
			if (HasConfirmedThreePush(swingLowDriveValues, swingLowDriveBars, false))
				return true;
			if (!sweptLow || swingLowDriveBars[0] < 0 || swingLowDriveBars[1] < 0)
				return false;
			if (CurrentBar - swingLowDriveBars[1] > ThreePushLookbackBars)
				return false;
			return swingLowDriveValues[0] < swingLowDriveValues[1] && Low[0] < swingLowDriveValues[0];
		}

		private bool HasConfirmedThreePush(double[] values, int[] bars, bool highs)
		{
			if (bars[0] < 0 || bars[1] < 0 || bars[2] < 0)
				return false;
			if (CurrentBar - bars[2] > ThreePushLookbackBars)
				return false;
			if (highs)
				return values[0] > values[1] && values[1] > values[2];
			return values[0] < values[1] && values[1] < values[2];
		}

		private int FindLastBearishCandle(int lookback)
		{
			for (int i = 1; i <= lookback; i++)
				if (Close[i] < Open[i])
					return i;
			return -1;
		}

		private int FindLastBullishCandle(int lookback)
		{
			for (int i = 1; i <= lookback; i++)
				if (Close[i] > Open[i])
					return i;
			return -1;
		}

		private bool IsInKillZone(int hhmmss, out string name)
		{
			int hhmm = hhmmss / 100;
			if (IsInWindow(hhmm, AsianStart, AsianEnd)) { name = "Asia"; return true; }
			if (IsInWindow(hhmm, LondonStart, LondonEnd)) { name = "London"; return true; }
			if (IsInWindow(hhmm, NewYorkAMStart, NewYorkAMEnd)) { name = "NY AM"; return true; }
			if (IsInWindow(hhmm, NewYorkPMStart, NewYorkPMEnd)) { name = "NY PM"; return true; }
			name = "Off";
			return false;
		}

		private bool IsInWindow(int hhmm, int start, int end)
		{
			if (start == end)
				return false;
			if (start < end)
				return hhmm >= start && hhmm <= end;
			return hhmm >= start || hhmm <= end;
		}

		private void ApplySmtVisualConfig()
		{
			if (smt == null)
				return;
			if (!ShowSmtVisuals)
				smt.ClearVisuals();
			smt.ShowLines = ShowSmtVisuals;
			smt.ShowLabels = ShowSmtVisuals && ShowSmtLabels;
			smt.TF5 = SmtTF5;
			smt.TF15 = SmtTF15;
			smt.TF30 = SmtTF30;
			smt.TF60 = SmtTF60;
			smt.TF90 = SmtTF90;
			smt.TF240 = SmtTF240;
		}

		private void ClearSmtVisuals()
		{
			if (smt != null)
				smt.ClearVisuals();
		}

		private void TryEnter(ICTSetupSignal signal)
		{
			string reason;
			if (signal != null && signal.Direction == ICTTradeDirection.Long && !TradeLong)
			{
				panel.Status = "BLOCKED";
				panel.BlockReason = "Long disabled.";
				Debug("BLOCKED Long disabled | " + DescribeSignal(signal));
				return;
			}
			if (signal != null && signal.Direction == ICTTradeDirection.Short && !TradeShort)
			{
				panel.Status = "BLOCKED";
				panel.BlockReason = "Short disabled.";
				Debug("BLOCKED Short disabled | " + DescribeSignal(signal));
				return;
			}

			double dailyPnL = GetDailyPnL();
			if (!riskManager.CanTrade(signal, tradesToday, dailyPnL, out reason))
			{
				panel.Status = "BLOCKED";
				panel.BlockReason = reason;
				Debug("BLOCKED " + reason + " | " + DescribeSignal(signal));
				return;
			}

			bool threePushMatched = IsThreePushMatched(signal);
			int qty = riskManager.GetQuantity(signal, Instrument.MasterInstrument.PointValue);
			if (threePushMatched && UseThreePushBonus)
				qty = Math.Max(qty, ThreePushQuantity);
			string entryName = signal.Kind.ToString();
			double safeStop = GetSafeStopPrice(signal.Direction, signal.Stop);
			if (!IsValidPrice(safeStop))
			{
				panel.Status = "BLOCKED";
				panel.BlockReason = "Invalid protective stop.";
				Debug("BLOCKED Invalid protective stop | " + DescribeSignal(signal));
				return;
			}

			activeEntryName = entryName;
			activeDirection = signal.Direction;
			activeEntryPrice = signal.Entry;
			activeStopPrice = safeStop;
			activeTargetPrice = signal.Target;
			activeEntryThreePush = threePushMatched;
			activeInitialQuantity = qty;
			activeSplitEntries = UsePartialAt1R && qty >= 2;
			activePartialEntryName = activeSplitEntries ? entryName + "_PT1" : string.Empty;
			activeRunnerEntryName = activeSplitEntries ? entryName + "_RUN" : string.Empty;
			activePartialQuantity = activeSplitEntries ? Math.Max(1, Math.Min(PartialQuantity, qty - 1)) : 0;
			activeRunnerQuantity = activeSplitEntries ? Math.Max(1, qty - activePartialQuantity) : 0;
			activePartialTargetPrice = double.NaN;
			activeRunnerTargetPrice = double.NaN;
			targetsSubmitted = false;
			partialTargetFilled = false;
			activeTradeCounted = false;
			stopMovedToBreakEven = false;
			entryOrderPending = true;

			ConfigureStopsAndTargets(entryName, qty, signal, safeStop);

			bool useLimit = UseLimitEntries && signal.PreferLimitEntry;
			if (useLimit)
			{
				if (!IsLimitEntryMarketSideSafe(signal.Direction, signal.Entry))
				{
					if (!AllowMarketIfEntryTouched)
					{
						entryOrderPending = false;
						panel.Status = "BLOCKED";
						panel.BlockReason = "Waiting for valid retrace-side limit price.";
						lastOrderMessage = "No limit: " + FormatPrice(signal.Entry) + " not on retrace side";
						Debug("BLOCKED " + lastOrderMessage + " | " + DescribeSignal(signal));
						ResetActiveTrade();
						return;
					}

					useLimit = false;
				}
			}

			if (useLimit)
			{
				if (activeSplitEntries)
					SubmitSplitEntries(signal.Direction, true, signal.Entry, qty);
				else if (signal.Direction == ICTTradeDirection.Long)
					EnterLongLimit(qty, signal.Entry, entryName);
				else if (signal.Direction == ICTTradeDirection.Short)
					EnterShortLimit(qty, signal.Entry, entryName);
				lastOrderMessage = "Limit " + entryName + " " + FormatPrice(signal.Entry);
			}
			else
			{
				if (activeSplitEntries)
					SubmitSplitEntries(signal.Direction, false, signal.Entry, qty);
				else if (signal.Direction == ICTTradeDirection.Long)
					EnterLong(qty, entryName);
				else if (signal.Direction == ICTTradeDirection.Short)
					EnterShort(qty, entryName);
				lastOrderMessage = "Market " + entryName;
			}

			lastEntryBar = CurrentBar;
			entrySubmitBar = CurrentBar;
			panel.Status = "ARMED";
			Debug("ENTER " + signal.Direction + " " + entryName + " qty=" + qty + " | " + DescribeSignal(signal));
		}

		private void ConfigureStopsAndTargets(string entryName, int quantity, ICTSetupSignal signal, double safeStop)
		{
			if (UsePartialAt1R && quantity >= 2)
			{
				int partialQty = activePartialQuantity > 0 ? activePartialQuantity : Math.Max(1, Math.Min(PartialQuantity, quantity - 1));
				int runnerQty = activeRunnerQuantity > 0 ? activeRunnerQuantity : Math.Max(1, quantity - partialQty);
				double risk = Math.Abs(signal.Entry - safeStop);
				double partialTarget = GetFirstScaleTarget(signal, safeStop);

				partialTarget = Instrument.MasterInstrument.RoundToTickSize(partialTarget);
				double runnerTarget = GetRunnerTarget(signal, safeStop, partialTarget);
				activePartialTargetPrice = partialTarget;
				activeRunnerTargetPrice = runnerTarget;

				SetStopLoss(activePartialEntryName, CalculationMode.Price, safeStop, false);
				SetProfitTarget(activePartialEntryName, CalculationMode.Price, partialTarget);
				SetStopLoss(activeRunnerEntryName, CalculationMode.Price, safeStop, false);
				SetProfitTarget(activeRunnerEntryName, CalculationMode.Price, runnerTarget);
				targetsSubmitted = true;
				lastOrderMessage = "Managed PT1 " + partialQty + " @ " + FormatPrice(partialTarget) + " / PT2 " + runnerQty + " @ " + FormatPrice(runnerTarget);
				return;
			}

			SetStopLoss(entryName, CalculationMode.Price, safeStop, false);
			SetProfitTarget(entryName, CalculationMode.Price, signal.Target);
			targetsSubmitted = true;
		}

		private double GetRunnerTarget(ICTSetupSignal signal, double safeStop, double partialTarget)
		{
			double risk = Math.Abs(signal.Entry - safeStop);
			double runnerR = Math.Max(RunnerTargetR, PartialAtR + 0.25);
			double fallback = signal.Direction == ICTTradeDirection.Long
				? signal.Entry + risk * runnerR
				: signal.Entry - risk * runnerR;
			double target = GetDirectionalTarget(signal, partialTarget, fallback);

			if (signal.Direction == ICTTradeDirection.Long && target <= partialTarget + TickSize)
				target = fallback;
			else if (signal.Direction == ICTTradeDirection.Short && target >= partialTarget - TickSize)
				target = fallback;

			return Instrument.MasterInstrument.RoundToTickSize(target);
		}

		private double GetFirstScaleTarget(ICTSetupSignal signal, double safeStop)
		{
			double risk = Math.Abs(signal.Entry - safeStop);
			double oneR = signal.Direction == ICTTradeDirection.Long
				? signal.Entry + risk * PartialAtR
				: signal.Entry - risk * PartialAtR;
			double eq = panel.Context != null ? panel.Context.PremiumDiscountMid : double.NaN;

			if (IsEntryInsideDealingRange(signal) && signal.Direction == ICTTradeDirection.Long && IsValidPrice(eq) && eq > signal.Entry + TickSize)
				return eq;
			if (IsEntryInsideDealingRange(signal) && signal.Direction == ICTTradeDirection.Short && IsValidPrice(eq) && eq < signal.Entry - TickSize)
				return eq;
			double dol = GetExternalLiquidityTarget(signal);
			if (IsTargetAhead(signal, dol, signal.Entry))
				return dol;
			return oneR;
		}

		private double GetDirectionalTarget(ICTSetupSignal signal, double partialTarget, double fallback)
		{
			double target = double.NaN;
			if (IsEntryInsideDealingRange(signal))
				target = GetLrlrTarget(signal);
			else
				target = GetExternalLiquidityTarget(signal);

			if (!IsTargetAhead(signal, target, partialTarget))
				target = GetReverseOteTarget(signal);
			if (!IsTargetAhead(signal, target, partialTarget) && IsTargetAhead(signal, signal.Target, partialTarget))
				target = signal.Target;
			if (!IsTargetAhead(signal, target, partialTarget))
				target = fallback;
			return target;
		}

		private bool IsEntryInsideDealingRange(ICTSetupSignal signal)
		{
			ICTMarketContext c = panel.Context;
			if (c == null || !IsValidPrice(c.DealingRangeHigh) || !IsValidPrice(c.DealingRangeLow) || c.DealingRangeHigh <= c.DealingRangeLow)
				return false;
			double price = IsValidPrice(signal.Entry) ? signal.Entry : c.Close;
			return price <= c.DealingRangeHigh + TickSize && price >= c.DealingRangeLow - TickSize;
		}

		private bool IsTargetAhead(ICTSetupSignal signal, double target, double minTarget)
		{
			if (!IsValidPrice(target))
				return false;
			if (signal.Direction == ICTTradeDirection.Long)
				return target > Math.Max(signal.Entry, minTarget) + TickSize;
			if (signal.Direction == ICTTradeDirection.Short)
				return target < Math.Min(signal.Entry, minTarget) - TickSize;
			return false;
		}

		private double GetLrlrTarget(ICTSetupSignal signal)
		{
			ICTMarketContext c = panel.Context;
			if (c == null)
				return double.NaN;

			if (signal.Direction == ICTTradeDirection.Long)
			{
				if (IsValidPrice(c.DealingRangeHigh) && c.DealingRangeHigh > signal.Entry + TickSize)
					return c.DealingRangeHigh;
				if (IsValidPrice(c.PriorDayHigh) && c.PriorDayHigh > signal.Entry + TickSize)
					return c.PriorDayHigh;
				if (IsValidPrice(c.RecentSwingHigh) && c.RecentSwingHigh > signal.Entry + TickSize)
					return c.RecentSwingHigh;
			}
			else if (signal.Direction == ICTTradeDirection.Short)
			{
				if (IsValidPrice(c.DealingRangeLow) && c.DealingRangeLow < signal.Entry - TickSize)
					return c.DealingRangeLow;
				if (IsValidPrice(c.PriorDayLow) && c.PriorDayLow < signal.Entry - TickSize)
					return c.PriorDayLow;
				if (IsValidPrice(c.RecentSwingLow) && c.RecentSwingLow < signal.Entry - TickSize)
					return c.RecentSwingLow;
			}

			return double.NaN;
		}

		private double GetExternalLiquidityTarget(ICTSetupSignal signal)
		{
			ICTMarketContext c = panel.Context;
			if (c == null)
				return double.NaN;

			double best = double.NaN;
			if (signal.Direction == ICTTradeDirection.Long)
			{
				AddTargetCandidate(signal, ref best, GetHtfSuiteTarget(signal));
				AddTargetCandidate(signal, ref best, GetEqualHighTarget(signal.Entry));
				AddTargetCandidate(signal, ref best, c.RecentSwingHigh);
				AddTargetCandidate(signal, ref best, previousSwingHigh);
				AddTargetCandidate(signal, ref best, GetForwardImbalanceTarget(signal));
				AddTargetCandidate(signal, ref best, c.PriorDayHigh);
			}
			else if (signal.Direction == ICTTradeDirection.Short)
			{
				AddTargetCandidate(signal, ref best, GetHtfSuiteTarget(signal));
				AddTargetCandidate(signal, ref best, GetEqualLowTarget(signal.Entry));
				AddTargetCandidate(signal, ref best, c.RecentSwingLow);
				AddTargetCandidate(signal, ref best, previousSwingLow);
				AddTargetCandidate(signal, ref best, GetForwardImbalanceTarget(signal));
				AddTargetCandidate(signal, ref best, c.PriorDayLow);
			}

			return best;
		}

		private double GetHtfSuiteTarget(ICTSetupSignal signal)
		{
			if (!UseHtfSuiteTargets || htfSuite == null || !IsValidPrice(signal.Entry))
				return double.NaN;
			if (signal.Direction == ICTTradeDirection.Long)
				return htfSuite.GetNearestKeyLevelAbove(signal.Entry);
			if (signal.Direction == ICTTradeDirection.Short)
				return htfSuite.GetNearestKeyLevelBelow(signal.Entry);
			return double.NaN;
		}

		private void AddTargetCandidate(ICTSetupSignal signal, ref double best, double candidate)
		{
			if (!IsValidPrice(candidate))
				return;
			if (signal.Direction == ICTTradeDirection.Long)
			{
				if (candidate <= signal.Entry + TickSize)
					return;
				if (!IsValidPrice(best) || candidate < best)
					best = candidate;
			}
			else if (signal.Direction == ICTTradeDirection.Short)
			{
				if (candidate >= signal.Entry - TickSize)
					return;
				if (!IsValidPrice(best) || candidate > best)
					best = candidate;
			}
		}

		private double GetEqualHighTarget(double entry)
		{
			double tolerance = Math.Max(TickSize * 4, GetAtr(AtrPeriod) * 0.08);
			double best = double.NaN;
			for (int i = 0; i < swingHighDriveValues.Length; i++)
			{
				double a = swingHighDriveValues[i];
				if (!IsValidPrice(a) || a <= entry + TickSize)
					continue;
				for (int j = i + 1; j < swingHighDriveValues.Length; j++)
				{
					double b = swingHighDriveValues[j];
					if (IsValidPrice(b) && Math.Abs(a - b) <= tolerance)
					{
						double eqh = Math.Max(a, b);
						if (!IsValidPrice(best) || eqh < best)
							best = eqh;
					}
				}
			}
			return best;
		}

		private double GetEqualLowTarget(double entry)
		{
			double tolerance = Math.Max(TickSize * 4, GetAtr(AtrPeriod) * 0.08);
			double best = double.NaN;
			for (int i = 0; i < swingLowDriveValues.Length; i++)
			{
				double a = swingLowDriveValues[i];
				if (!IsValidPrice(a) || a >= entry - TickSize)
					continue;
				for (int j = i + 1; j < swingLowDriveValues.Length; j++)
				{
					double b = swingLowDriveValues[j];
					if (IsValidPrice(b) && Math.Abs(a - b) <= tolerance)
					{
						double eql = Math.Min(a, b);
						if (!IsValidPrice(best) || eql > best)
							best = eql;
					}
				}
			}
			return best;
		}

		private double GetForwardImbalanceTarget(ICTSetupSignal signal)
		{
			ICTMarketContext c = panel.Context;
			if (c == null)
				return double.NaN;

			double best = double.NaN;
			if (signal.Direction == ICTTradeDirection.Long)
			{
				AddTargetCandidate(signal, ref best, c.BearPdaBottom);
				AddTargetCandidate(signal, ref best, c.BearPdaMid);
				AddTargetCandidate(signal, ref best, c.BearPdaTop);
				AddTargetCandidate(signal, ref best, bearFvgBottom);
				AddTargetCandidate(signal, ref best, bearFvgTop);
			}
			else if (signal.Direction == ICTTradeDirection.Short)
			{
				AddTargetCandidate(signal, ref best, c.BullPdaTop);
				AddTargetCandidate(signal, ref best, c.BullPdaMid);
				AddTargetCandidate(signal, ref best, c.BullPdaBottom);
				AddTargetCandidate(signal, ref best, bullFvgTop);
				AddTargetCandidate(signal, ref best, bullFvgBottom);
			}
			return best;
		}

		private double GetReverseOteTarget(ICTSetupSignal signal)
		{
			ICTMarketContext c = panel.Context;
			if (c == null || !IsValidPrice(c.DealingRangeHigh) || !IsValidPrice(c.DealingRangeLow) || c.DealingRangeHigh <= c.DealingRangeLow)
				return double.NaN;

			double range = c.DealingRangeHigh - c.DealingRangeLow;
			double target = double.NaN;
			if (signal.Direction == ICTTradeDirection.Long)
				target = c.DealingRangeLow + range * 0.705;
			else if (signal.Direction == ICTTradeDirection.Short)
				target = c.DealingRangeHigh - range * 0.705;
			return Instrument.MasterInstrument.RoundToTickSize(target);
		}

		private void SubmitSplitEntries(ICTTradeDirection direction, bool useLimit, double entryPrice, int quantity)
		{
			int partialQty = activePartialQuantity > 0 ? activePartialQuantity : Math.Max(1, Math.Min(PartialQuantity, quantity - 1));
			int runnerQty = activeRunnerQuantity > 0 ? activeRunnerQuantity : Math.Max(1, quantity - partialQty);

			if (direction == ICTTradeDirection.Long)
			{
				if (useLimit)
				{
					EnterLongLimit(partialQty, entryPrice, activePartialEntryName);
					EnterLongLimit(runnerQty, entryPrice, activeRunnerEntryName);
				}
				else
				{
					EnterLong(partialQty, activePartialEntryName);
					EnterLong(runnerQty, activeRunnerEntryName);
				}
			}
			else if (direction == ICTTradeDirection.Short)
			{
				if (useLimit)
				{
					EnterShortLimit(partialQty, entryPrice, activePartialEntryName);
					EnterShortLimit(runnerQty, entryPrice, activeRunnerEntryName);
				}
				else
				{
					EnterShort(partialQty, activePartialEntryName);
					EnterShort(runnerQty, activeRunnerEntryName);
				}
			}
		}

		private bool IsLimitEntryMarketSideSafe(ICTTradeDirection direction, double entryPrice)
		{
			if (!IsValidPrice(entryPrice))
				return false;

			double buffer = TickSize;
			if (direction == ICTTradeDirection.Long)
			{
				double ceiling = GetSellStopCeiling();
				if (!IsValidPrice(ceiling))
					ceiling = Close[0];
				return entryPrice <= ceiling - buffer;
			}

			if (direction == ICTTradeDirection.Short)
			{
				double floor = GetBuyStopFloor();
				if (!IsValidPrice(floor))
					floor = Close[0];
				return entryPrice >= floor + buffer;
			}

			return false;
		}

		private void ManagePendingEntry()
		{
			if (!entryOrderPending || Position.MarketPosition != MarketPosition.Flat)
				return;
			if (EntryTimeoutBars <= 0 || entrySubmitBar < 0 || CurrentBar - entrySubmitBar < EntryTimeoutBars)
				return;

			if (activeEntryOrder != null)
			{
				CancelOrder(activeEntryOrder);
				lastOrderMessage = "Cancel stale limit " + activeEntryName;
				Debug(lastOrderMessage);
			}
			else
			{
				entryOrderPending = false;
				lastOrderMessage = "Pending expired " + activeEntryName;
				ResetActiveTrade();
			}
		}

		private void ManageOpenPosition()
		{
			if (Position.MarketPosition == MarketPosition.Flat)
			{
				if (!entryOrderPending)
					ResetActiveTrade();
				return;
			}

			if (string.IsNullOrEmpty(activeEntryName) || !IsValidPrice(activeStopPrice))
				return;

			if (IsValidPrice(Position.AveragePrice))
				activeEntryPrice = Position.AveragePrice;

			double risk = Math.Abs(activeEntryPrice - activeStopPrice);
			if (risk <= TickSize)
				return;

			SubmitTargetsIfNeeded(risk);

			if (UseBreakEven && !stopMovedToBreakEven)
			{
				bool singleContractBe = activeInitialQuantity <= 1 || !activeSplitEntries;
				if (singleContractBe)
				{
					if (Position.MarketPosition == MarketPosition.Long && Close[0] >= activeEntryPrice + risk * BreakEvenAtR)
						MoveStopToBreakEven("BE");
					else if (Position.MarketPosition == MarketPosition.Short && Close[0] <= activeEntryPrice - risk * BreakEvenAtR)
						MoveStopToBreakEven("BE");
				}
			}

			if (ManualExitOverride || !UseAtrTrailingStop)
				return;

			double moveR = Position.MarketPosition == MarketPosition.Long
				? (Close[0] - activeEntryPrice) / risk
				: (activeEntryPrice - Close[0]) / risk;
			if (moveR < TrailAfterR)
				return;

			double atr = GetAtr(AtrPeriod);
			if (Position.MarketPosition == MarketPosition.Long)
				MoveStop(Close[0] - atr * TrailAtrMultiple, "TRAIL");
			else if (Position.MarketPosition == MarketPosition.Short)
				MoveStop(Close[0] + atr * TrailAtrMultiple, "TRAIL");
		}

		private void SubmitTargetsIfNeeded(double risk)
		{
			if (targetsSubmitted || string.IsNullOrEmpty(activeEntryName) || Position.MarketPosition == MarketPosition.Flat)
				return;

			int quantity = Math.Max(1, Position.Quantity);
			if (UsePartialAt1R && activeInitialQuantity >= 2 && quantity < activeInitialQuantity)
				return;

			if (UsePartialAt1R && quantity >= 2)
			{
				int partialQty = Math.Max(1, Math.Min(PartialQuantity, quantity - 1));
				int runnerQty = Math.Max(1, quantity - partialQty);
				double partialTarget = Position.MarketPosition == MarketPosition.Long
					? activeEntryPrice + risk * PartialAtR
					: activeEntryPrice - risk * PartialAtR;

				if (Position.MarketPosition == MarketPosition.Long)
				{
					ExitLongLimit(partialQty, Instrument.MasterInstrument.RoundToTickSize(partialTarget), "PT1_" + activeEntryName, activeEntryName);
					ExitLongLimit(runnerQty, activeTargetPrice, "PT2_" + activeEntryName, activeEntryName);
				}
				else if (Position.MarketPosition == MarketPosition.Short)
				{
					ExitShortLimit(partialQty, Instrument.MasterInstrument.RoundToTickSize(partialTarget), "PT1_" + activeEntryName, activeEntryName);
					ExitShortLimit(runnerQty, activeTargetPrice, "PT2_" + activeEntryName, activeEntryName);
				}

				targetsSubmitted = true;
				lastOrderMessage = "Targets PT1 " + FormatPrice(partialTarget) + " / PT2 " + FormatPrice(activeTargetPrice);
				Debug(lastOrderMessage);
				return;
			}

			if (Position.MarketPosition == MarketPosition.Long)
				ExitLongLimit(quantity, activeTargetPrice, "PT_" + activeEntryName, activeEntryName);
			else if (Position.MarketPosition == MarketPosition.Short)
				ExitShortLimit(quantity, activeTargetPrice, "PT_" + activeEntryName, activeEntryName);

			targetsSubmitted = true;
			lastOrderMessage = "Target " + FormatPrice(activeTargetPrice);
			Debug(lastOrderMessage);
		}

		private void MoveStopToBreakEven(string reason)
		{
			if (Position.MarketPosition == MarketPosition.Long)
				MoveStop(activeEntryPrice + BreakEvenPlusTicks * TickSize, reason);
			else if (Position.MarketPosition == MarketPosition.Short)
				MoveStop(activeEntryPrice - BreakEvenPlusTicks * TickSize, reason);
		}

		private void HandleProfitTargetExecution(Execution execution, double price, int quantity)
		{
			if (!IsProfitTargetExecution(execution, price))
				return;

			HandleProfitTargetFill(price, quantity, "Target BE");
		}

		private bool IsProfitTargetExecution(Execution execution, double price)
		{
			if (!activeSplitEntries || activeInitialQuantity <= 1 || stopMovedToBreakEven || execution == null || execution.Order == null)
				return false;
			if (!IsValidPrice(price) || !IsValidPrice(activeEntryPrice))
				return false;

			string orderName = execution.Order.Name ?? string.Empty;
			if (orderName.IndexOf("Profit target", StringComparison.OrdinalIgnoreCase) < 0
				&& orderName.IndexOf("PT", StringComparison.OrdinalIgnoreCase) < 0)
				return false;

			if (activeDirection == ICTTradeDirection.Long)
				return price > activeEntryPrice;
			if (activeDirection == ICTTradeDirection.Short)
				return price < activeEntryPrice;
			return false;
		}

		private void RequestStopMoveToBreakEven(string reason)
		{
			if (Position.MarketPosition == MarketPosition.Long)
				RequestStopMove(activeEntryPrice + BreakEvenPlusTicks * TickSize, reason);
			else if (Position.MarketPosition == MarketPosition.Short)
				RequestStopMove(activeEntryPrice - BreakEvenPlusTicks * TickSize, reason);
		}

		private void RequestStopMove(double price, string reason)
		{
			if (!IsValidPrice(price))
				return;

			pendingStopMove = true;
			pendingStopMovePrice = price;
			pendingStopMoveReason = reason;
			MoveStop(price, reason);
		}

		private void HandleProfitTargetFill(double price, int quantity, string reason)
		{
			if (partialTargetFilled && pendingStopMove && IsValidPrice(pendingStopMovePrice))
				return;

			partialTargetFilled = true;
			lastOrderMessage = "Target filled " + quantity + " @ " + FormatPrice(price);
			Debug(lastOrderMessage);
			RequestStopMoveToBreakEven(reason);
		}

		private void TryApplyPendingStopMove()
		{
			if (!pendingStopMove || Position.MarketPosition == MarketPosition.Flat || !IsValidPrice(pendingStopMovePrice))
				return;
			MoveStop(pendingStopMovePrice, pendingStopMoveReason);
		}

		private void MoveStop(double newStop, string reason)
		{
			if (ManualExitOverride && !IsBreakEvenStopReason(reason))
				return;
			if (!IsValidPrice(newStop) || string.IsNullOrEmpty(activeEntryName))
				return;

			ICTTradeDirection direction = Position.MarketPosition == MarketPosition.Long
				? ICTTradeDirection.Long
				: (Position.MarketPosition == MarketPosition.Short ? ICTTradeDirection.Short : ICTTradeDirection.None);
			newStop = GetSafeStopPrice(direction, newStop);
			if (!IsValidPrice(newStop))
				return;

			if (Position.MarketPosition == MarketPosition.Long)
			{
				if (newStop <= activeStopPrice)
					return;
			}
			else if (Position.MarketPosition == MarketPosition.Short)
			{
				if (newStop >= activeStopPrice)
					return;
			}
			else
				return;

			if (TryChangeActiveStopOrder(newStop, reason))
			{
				if (IsBreakEvenStopReason(reason))
					ApplyManagedStopLoss(newStop);
				if (reason != "BE" && reason != "PT1 BE" && reason != "Target BE")
					activeStopPrice = newStop;
				lastOrderMessage = reason + " stop requested " + FormatPrice(newStop);
				Debug(lastOrderMessage);
				return;
			}

			ApplyManagedStopLoss(newStop);
			if (reason != "BE" && reason != "PT1 BE" && reason != "Target BE")
				activeStopPrice = newStop;
			lastOrderMessage = reason + " stop " + FormatPrice(newStop);
			Debug(lastOrderMessage);
		}

		private void ApplyManagedStopLoss(double newStop)
		{
			if (activeSplitEntries)
			{
				if (!string.IsNullOrEmpty(activePartialEntryName))
					SetStopLoss(activePartialEntryName, CalculationMode.Price, newStop, false);
				if (!string.IsNullOrEmpty(activeRunnerEntryName))
					SetStopLoss(activeRunnerEntryName, CalculationMode.Price, newStop, false);
			}
			else if (!string.IsNullOrEmpty(activeRunnerEntryName))
				SetStopLoss(activeRunnerEntryName, CalculationMode.Price, newStop, false);
			else
				SetStopLoss(activeEntryName, CalculationMode.Price, newStop, false);
		}

		private bool IsBreakEvenStopReason(string reason)
		{
			return reason == "BE" || reason == "PT1 BE" || reason == "Target BE";
		}

		private bool TryChangeActiveStopOrder(double newStop, string reason)
		{
			bool submitted = false;
			int remaining = Math.Max(1, Position.Quantity);
			for (int i = activeStopOrders.Count - 1; i >= 0; i--)
			{
				Order stop = activeStopOrders[i];
				if (!IsWorkingStopOrder(stop))
				{
					activeStopOrders.RemoveAt(i);
					continue;
				}

				int stopQty = Math.Max(1, Math.Min(stop.Quantity - stop.Filled, remaining));
				try
				{
					ChangeOrder(stop, stopQty, 0, newStop);
					submitted = true;
				}
				catch (Exception ex)
				{
					Debug("Stop change failed " + ex.Message);
				}
			}

			if (submitted)
				return true;

			if (activeStopOrder == null || !IsWorkingStopOrder(activeStopOrder))
				return false;

			try
			{
				ChangeOrder(activeStopOrder, remaining, 0, newStop);
				return true;
			}
			catch (Exception ex)
			{
				Debug("Stop change failed " + ex.Message);
				return false;
			}
		}

		private bool IsWorkingStopOrder(Order order)
		{
			return order != null
				&& order.Name == "Stop loss"
				&& (order.OrderState == OrderState.Accepted
					|| order.OrderState == OrderState.Working
					|| order.OrderState == OrderState.ChangePending
					|| order.OrderState == OrderState.ChangeSubmitted);
		}

		private double GetSafeStopPrice(ICTTradeDirection direction, double desiredStop)
		{
			if (!IsValidPrice(desiredStop) || direction == ICTTradeDirection.None)
				return double.NaN;

			double buffer = Math.Max(1, ProtectiveStopBufferTicks) * TickSize;
			if (direction == ICTTradeDirection.Long)
			{
				double ceiling = GetSellStopCeiling();
				if (!IsValidPrice(ceiling))
					ceiling = Close[0];
				return Math.Min(desiredStop, ceiling - buffer);
			}

			double floor = GetBuyStopFloor();
			if (!IsValidPrice(floor))
				floor = Close[0];
			return Math.Max(desiredStop, floor + buffer);
		}

		private double GetSellStopCeiling()
		{
			double bid = GetCurrentBid();
			if (IsValidPrice(bid))
				return Math.Min(Close[0], bid);
			return Close[0];
		}

		private double GetBuyStopFloor()
		{
			double ask = GetCurrentAsk();
			if (IsValidPrice(ask))
				return Math.Max(Close[0], ask);
			return Close[0];
		}

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
		{
			if (order == null)
				return;

			TrackActiveStopOrder(order, stopPrice, quantity, orderState);
			TrackActiveTargetOrder(order, limitPrice, quantity, orderState);
			HandleProfitTargetOrderUpdate(order, limitPrice, quantity, filled, averageFillPrice, orderState);

			if (string.IsNullOrEmpty(activeEntryName) || !IsActiveEntryOrderName(order.Name))
				return;

			if (orderState == OrderState.Submitted || orderState == OrderState.Accepted || orderState == OrderState.Working)
				activeEntryOrder = order;

			if (orderState == OrderState.Filled || filled >= quantity)
			{
				entryOrderPending = false;
				activeEntryOrder = null;
				activeEntryPrice = averageFillPrice > 0 ? averageFillPrice : activeEntryPrice;
				if (!activeTradeCounted)
				{
					tradesToday++;
					activeTradeCounted = true;
				}
				lastOrderMessage = "Filled " + order.Name + " " + FormatPrice(activeEntryPrice);
				Debug(lastOrderMessage);
				TrySubmitTargetsFromCurrentPosition();
			}
			else if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
			{
				entryOrderPending = false;
				activeEntryOrder = null;
				lastOrderMessage = orderState + " " + order.Name + " " + nativeError;
				panel.Status = "BLOCKED";
				panel.BlockReason = lastOrderMessage;
				Debug(lastOrderMessage);
				ResetActiveTrade();
			}
			else if (error != ErrorCode.NoError)
			{
				lastOrderMessage = error + " " + nativeError;
				panel.BlockReason = lastOrderMessage;
				Debug(lastOrderMessage);
			}
		}

		private void TrackActiveStopOrder(Order order, double stopPrice, int quantity, OrderState orderState)
		{
			if (order == null || string.IsNullOrEmpty(activeEntryName))
				return;
			if (order.Name != "Stop loss")
				return;
			if (orderState == OrderState.Cancelled || orderState == OrderState.Filled || orderState == OrderState.Rejected)
			{
				if (activeStopOrder == order)
					activeStopOrder = null;
				activeStopOrders.Remove(order);
				return;
			}
			if (orderState != OrderState.Submitted
				&& orderState != OrderState.Accepted
				&& orderState != OrderState.Working
				&& orderState != OrderState.ChangePending
				&& orderState != OrderState.ChangeSubmitted)
				return;
			activeStopOrder = order;
			if (!activeStopOrders.Contains(order))
				activeStopOrders.Add(order);
			if (IsValidPrice(stopPrice))
			{
				activeStopPrice = stopPrice;
				if (pendingStopMove && IsValidPrice(pendingStopMovePrice) && Math.Abs(stopPrice - pendingStopMovePrice) <= TickSize * 0.5)
				{
					stopMovedToBreakEven = true;
					pendingStopMove = false;
					pendingStopMovePrice = double.NaN;
					pendingStopMoveReason = string.Empty;
					lastOrderMessage = "BE confirmed " + FormatPrice(stopPrice);
					Debug(lastOrderMessage);
				}
			}
			TryApplyPendingStopMove();
		}

		private void TrackActiveTargetOrder(Order order, double limitPrice, int quantity, OrderState orderState)
		{
			if (order == null || string.IsNullOrEmpty(activeEntryName))
				return;
			if (order.Name != "Profit target")
				return;
			if (orderState == OrderState.Cancelled || orderState == OrderState.Filled || orderState == OrderState.Rejected)
			{
				activeTargetOrders.Remove(order);
				return;
			}
			if (orderState != OrderState.Submitted
				&& orderState != OrderState.Accepted
				&& orderState != OrderState.Working
				&& orderState != OrderState.ChangePending
				&& orderState != OrderState.ChangeSubmitted)
				return;

			if (!activeTargetOrders.Contains(order))
				activeTargetOrders.Add(order);
		}

		private void HandleProfitTargetOrderUpdate(Order order, double limitPrice, int quantity, int filled, double averageFillPrice, OrderState orderState)
		{
			if (order == null || !activeSplitEntries || activeInitialQuantity <= 1 || stopMovedToBreakEven)
				return;
			if (order.Name != "Profit target")
				return;
			if (orderState != OrderState.Filled && filled < quantity)
				return;

			double fillPrice = averageFillPrice > 0 ? averageFillPrice : limitPrice;
			if (!IsValidPrice(fillPrice) || !IsValidPrice(activeEntryPrice))
				return;
			if (activeDirection == ICTTradeDirection.Long && fillPrice <= activeEntryPrice)
				return;
			if (activeDirection == ICTTradeDirection.Short && fillPrice >= activeEntryPrice)
				return;

			HandleProfitTargetFill(fillPrice, Math.Max(1, filled), "Target BE");
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution == null || execution.Order == null || string.IsNullOrEmpty(activeEntryName))
				return;

			HandleProfitTargetExecution(execution, price, quantity);

			if (!IsActiveEntryOrderName(execution.Order.Name))
				return;

			entryOrderPending = false;
			activeEntryOrder = null;
			if (price > 0)
				activeEntryPrice = price;
			lastOrderMessage = "Exec " + execution.Order.Name + " " + quantity + " @ " + FormatPrice(price);
			Debug(lastOrderMessage);
			TrySubmitTargetsFromCurrentPosition();
		}

		private void TrySubmitTargetsFromCurrentPosition()
		{
			if (targetsSubmitted || Position.MarketPosition == MarketPosition.Flat || !IsValidPrice(activeEntryPrice) || !IsValidPrice(activeStopPrice))
				return;
			double risk = Math.Abs(activeEntryPrice - activeStopPrice);
			if (risk > TickSize)
				SubmitTargetsIfNeeded(risk);
		}

		private bool IsActiveEntryOrderName(string orderName)
		{
			return orderName == activeEntryName
				|| orderName == activePartialEntryName
				|| orderName == activeRunnerEntryName;
		}

		private void ResetActiveTrade()
		{
			activeEntryName = string.Empty;
			activePartialEntryName = string.Empty;
			activeRunnerEntryName = string.Empty;
			activeDirection = ICTTradeDirection.None;
			activeEntryPrice = double.NaN;
			activeStopPrice = double.NaN;
			activeTargetPrice = double.NaN;
			activeEntryThreePush = false;
			activeSplitEntries = false;
			activeInitialQuantity = 0;
			activePartialQuantity = 0;
			activeRunnerQuantity = 0;
			activePartialTargetPrice = double.NaN;
			activeRunnerTargetPrice = double.NaN;
			pendingStopMove = false;
			pendingStopMovePrice = double.NaN;
			pendingStopMoveReason = string.Empty;
			targetsSubmitted = false;
			partialTargetFilled = false;
			activeTradeCounted = false;
			stopMovedToBreakEven = false;
			activeEntryOrder = null;
			activeStopOrder = null;
			activeStopOrders.Clear();
			activeTargetOrders.Clear();
			entryOrderPending = false;
			entrySubmitBar = -1;
		}

		private double GetDailyPnL()
		{
			return SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - dayStartCumProfit;
		}

		private void ApplyThreePushBonus(List<ICTSetupSignal> signals, ICTMarketContext context)
		{
			if (!UseThreePushBonus || ThreePushBonusScore <= 0 || signals == null)
				return;

			foreach (ICTSetupSignal signal in signals)
			{
				if (signal == null || !signal.IsValid || !IsThreePushMatched(signal, context))
					continue;

				signal.Score = Math.Min(100, signal.Score + ThreePushBonusScore);
				if (string.IsNullOrEmpty(signal.Reason))
					signal.Reason = "Three-push bonus.";
				else if (signal.Reason.IndexOf("three-push", StringComparison.OrdinalIgnoreCase) < 0)
					signal.Reason += " | three-push bonus +" + ThreePushBonusScore;
			}
		}

		private void ToggleManualExitOverride()
		{
			ManualExitOverride = !ManualExitOverride;
			if (ManualExitOverride)
			{
				lastOrderMessage = "Manual exit adjust ON; BE still active";
			}
			else
			{
				lastOrderMessage = "Manual exit adjust OFF";
			}
			Debug(lastOrderMessage);
		}

		private void ManualMoveStops(int ticks)
		{
			if (ticks == 0 || Position.MarketPosition == MarketPosition.Flat)
				return;
			double delta = ticks * TickSize;
			bool moved = false;
			for (int i = activeStopOrders.Count - 1; i >= 0; i--)
			{
				Order stop = activeStopOrders[i];
				if (!IsWorkingStopOrder(stop))
				{
					activeStopOrders.RemoveAt(i);
					continue;
				}

				double newStop = stop.StopPrice + delta;
				ChangeOrder(stop, Math.Max(1, stop.Quantity - stop.Filled), 0, Instrument.MasterInstrument.RoundToTickSize(newStop));
				moved = true;
			}
			if (moved)
				lastOrderMessage = "Manual SL " + (ticks > 0 ? "+" : "") + ticks + " ticks";
		}

		private void ManualMoveTargets(int ticks)
		{
			if (ticks == 0 || Position.MarketPosition == MarketPosition.Flat)
				return;
			double delta = ticks * TickSize;
			bool moved = false;
			for (int i = activeTargetOrders.Count - 1; i >= 0; i--)
			{
				Order target = activeTargetOrders[i];
				if (!IsWorkingTargetOrder(target))
				{
					activeTargetOrders.RemoveAt(i);
					continue;
				}

				double newTarget = target.LimitPrice + delta;
				ChangeOrder(target, Math.Max(1, target.Quantity - target.Filled), Instrument.MasterInstrument.RoundToTickSize(newTarget), 0);
				moved = true;
			}
			if (moved)
				lastOrderMessage = "Manual TP " + (ticks > 0 ? "+" : "") + ticks + " ticks";
		}

		private bool IsWorkingTargetOrder(Order order)
		{
			return order != null
				&& order.Name == "Profit target"
				&& (order.OrderState == OrderState.Accepted
					|| order.OrderState == OrderState.Working
					|| order.OrderState == OrderState.ChangePending
					|| order.OrderState == OrderState.ChangeSubmitted);
		}

		private bool IsThreePushMatched(ICTSetupSignal signal)
		{
			return IsThreePushMatched(signal, panel.Context);
		}

		private bool IsThreePushMatched(ICTSetupSignal signal, ICTMarketContext context)
		{
			if (signal == null || context == null)
				return false;
			if (signal.Direction == ICTTradeDirection.Long)
				return context.ThreePushLow;
			if (signal.Direction == ICTTradeDirection.Short)
				return context.ThreePushHigh;
			return false;
		}

		private void UpdateScores(List<ICTSetupSignal> signals)
		{
			panel.LongScore = 0;
			panel.ShortScore = 0;
			foreach (ICTSetupSignal signal in signals)
			{
				if (signal == null) continue;
				if (signal.Direction == ICTTradeDirection.Long)
					panel.LongScore = Math.Max(panel.LongScore, signal.Score);
				else if (signal.Direction == ICTTradeDirection.Short)
					panel.ShortScore = Math.Max(panel.ShortScore, signal.Score);
			}
			if (Position.MarketPosition != MarketPosition.Flat)
				panel.Status = "IN TRADE";
			else if (panel.BestSignal != null && panel.BestSignal.IsValid)
				panel.Status = "ARMED";
			else if (string.IsNullOrEmpty(panel.BlockReason) || panel.BlockReason == "No valid setup.")
				panel.Status = "WARMUP";
			else
				panel.Status = "BLOCKED";
		}

		private void UpdatePanel()
		{
			if (!ShowPanel)
			{
				RemoveDrawObject("ICT2022_PANEL");
				UpdateControlPanelStatus();
				return;
			}

			ICTSetupSignal s = panel.BestSignal ?? new ICTSetupSignal();
			ICTMarketContext c = panel.Context ?? new ICTMarketContext();
			string text =
				"Setup_Engine | " + panel.Status + "\n" +
				"L/S score: " + panel.LongScore.ToString("0") + " / " + panel.ShortScore.ToString("0") + "\n" +
				"Exit Mgmt: " + (ManualExitOverride ? "MANUAL" : "AUTO") + "\n" +
				"Setup: " + s.Kind + " " + s.Direction + " " + s.Score + "\n" +
				"Entry/SL/TP: " + FormatPrice(s.Entry) + " / " + FormatPrice(s.Stop) + " / " + FormatPrice(s.Target) + "\n" +
				"Model/Zone: " + s.EntryModel + " | " + FormatPrice(s.ZoneTop) + " / " + FormatPrice(s.ZoneBottom) + "\n" +
				"Live: " + Position.MarketPosition + " " + FormatPrice(activeEntryPrice) + " / " + FormatPrice(activeStopPrice) + " / " + FormatPrice(activeTargetPrice) + "\n" +
				"Trades/PnL: " + panel.TradesToday + " / " + panel.DailyPnL.ToString("0.00") + "\n" +
				"Order: " + lastOrderMessage + "\n" +
				"Block: " + panel.BlockReason;

			if (ShowDetailedPanel)
			{
				text += "\n" +
					"DR H/M/L: " + FormatPrice(c.DealingRangeHigh) + " / " + FormatPrice(c.PremiumDiscountMid) + " / " + FormatPrice(c.DealingRangeLow) + "\n" +
					"DR Mode S/V/C: " + SetupDealingRangeMode + " / " + VisualDealingRangeMode + " / " + DealingRangeCycleMinutes + "m\n" +
					"Zone: " + (c.InDiscount ? "Discount" : (c.InPremium ? "Premium" : "-")) + " | KZ: " + c.KillZoneName + "\n" +
					"Sweep age L/H: " + AgeText(c.SellsideSweepAge) + " / " + AgeText(c.BuysideSweepAge) + "\n" +
					"MSS age B/S: " + AgeText(c.BullMssAge) + " / " + AgeText(c.BearMssAge) + " | CISD: " + AgeText(c.BullCisdAge) + " / " + AgeText(c.BearCisdAge) + "\n" +
					"FVG age B/S: " + AgeText(c.BullFvgAge) + " / " + AgeText(c.BearFvgAge) + " | tapped: " + (c.BullFvgTapped ? "Y" : "N") + " / " + (c.BearFvgTapped ? "Y" : "N") + "\n" +
					"ThreePush L/H: " + (c.ThreePushLow ? "Y" : "N") + " / " + (c.ThreePushHigh ? "Y" : "N") + " age " + AgeText(c.ThreePushLowAge) + " / " + AgeText(c.ThreePushHighAge) + "\n" +
					"Bull PDA T/M/B: " + FormatPrice(c.BullPdaTop) + " / " + FormatPrice(c.BullPdaMid) + " / " + FormatPrice(c.BullPdaBottom) + "\n" +
					"Bear PDA T/M/B: " + FormatPrice(c.BearPdaTop) + " / " + FormatPrice(c.BearPdaMid) + " / " + FormatPrice(c.BearPdaBottom) + "\n" +
					"HTF facts: " + FormatHtfFactsSummary(c) + "\n" +
					"SMT: " + (c.BullSmt ? "Bull" : (c.BearSmt ? "Bear" : "-")) + " " + c.SmtTimeframe + " age " + AgeText(c.BullSmt ? c.BullSmtAge : c.BearSmtAge) + "\n" +
					"Reason: " + s.Reason;
			}

			Draw.TextFixed(this, "ICT2022_PANEL", text, TextPosition.TopLeft, Brushes.WhiteSmoke, new SimpleFont("Consolas", 12), Brushes.DimGray, Brushes.Black, 80);
			UpdateControlPanelStatus();
		}

		private string FormatHtfFactsSummary(ICTMarketContext context)
		{
			if (context == null || context.HtfKeyLevels == null || context.HtfKeyLevels.Count == 0)
				return "0";

			ICTKeyLevelFact above = null;
			ICTKeyLevelFact below = null;
			for (int i = 0; i < context.HtfKeyLevels.Count; i++)
			{
				ICTKeyLevelFact fact = context.HtfKeyLevels[i];
				if (fact == null || !fact.IsActive || fact.IsFilled || !IsValidPrice(fact.Price))
					continue;
				if (fact.Price > context.Close + TickSize && (above == null || fact.Price < above.Price))
					above = fact;
				if (fact.Price < context.Close - TickSize && (below == null || fact.Price > below.Price))
					below = fact;
			}

			return context.HtfKeyLevels.Count.ToString()
				+ " | A " + FormatKeyLevelFact(above)
				+ " | B " + FormatKeyLevelFact(below);
		}

		private string FormatKeyLevelFact(ICTKeyLevelFact fact)
		{
			if (fact == null)
				return "-";
			return fact.Source + " " + fact.Kind + " " + FormatPrice(fact.Price);
		}

		private void AddControlPanel()
		{
			if (!ShowControlPanel || ChartControl == null)
				return;

			ChartControl.Dispatcher.InvokeAsync(() =>
			{
				if (controlPanelGrid != null || State == State.Terminated)
					return;
				controlPanelGrid = BuildControlPanel();
				UserControlCollection.Add(controlPanelGrid);
			});
		}

		private void RemoveControlPanel()
		{
			if (ChartControl == null)
				return;

			ChartControl.Dispatcher.InvokeAsync(() =>
			{
				if (controlPanelGrid != null && UserControlCollection.Contains(controlPanelGrid))
					UserControlCollection.Remove(controlPanelGrid);
				controlPanelGrid = null;
				controlPanelStatus = null;
			});
		}

		private Grid BuildControlPanel()
		{
			Grid root = new Grid
			{
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(12, 310, 0, 0),
				Background = new SolidColorBrush(Color.FromArgb(215, 20, 20, 20)),
				Width = 260
			};
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

			TextBlock title = new TextBlock
			{
				Text = "Setup Engine Control",
				Foreground = Brushes.WhiteSmoke,
				FontWeight = FontWeights.Bold,
				Margin = new Thickness(8, 6, 8, 4),
				Cursor = Cursors.SizeAll
			};
			title.MouseLeftButtonDown += ControlPanelDragStart;
			title.MouseMove += ControlPanelDragMove;
			title.MouseLeftButtonUp += ControlPanelDragEnd;
			root.Children.Add(title);

			StackPanel body = new StackPanel { Margin = new Thickness(8, 0, 8, 8) };
			Grid.SetRow(body, 1);
			root.Children.Add(body);

			controlPanelStatus = new TextBlock { Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 0, 0, 6), FontSize = 11 };
			body.Children.Add(controlPanelStatus);

			StackPanel row1 = Row();
			row1.Children.Add(MakeButton("Trading", () => EnableTrading = !EnableTrading));
			row1.Children.Add(MakeButton("Manual", ToggleManualExitOverride));
			row1.Children.Add(MakeButton("Panel", () => ShowPanel = !ShowPanel));
			row1.Children.Add(MakeButton("Detail", () => ShowDetailedPanel = !ShowDetailedPanel));
			row1.Children.Add(MakeButton("DR Lines", () => ShowDealingRangeLines = !ShowDealingRangeLines));
			body.Children.Add(row1);

			StackPanel manualRow = Row();
			manualRow.Children.Add(MakeButton("SL +", () => ManualMoveStops(ManualAdjustTicks)));
			manualRow.Children.Add(MakeButton("SL -", () => ManualMoveStops(-ManualAdjustTicks)));
			manualRow.Children.Add(MakeButton("TP +", () => ManualMoveTargets(ManualAdjustTicks)));
			manualRow.Children.Add(MakeButton("TP -", () => ManualMoveTargets(-ManualAdjustTicks)));
			body.Children.Add(manualRow);

			StackPanel row2 = Row();
			row2.Children.Add(MakeCheck("3P Bonus", () => UseThreePushBonus, value => UseThreePushBonus = value));
			row2.Children.Add(MakeCheck("KZ Only", () => RequireKillZoneForEntry, value => RequireKillZoneForEntry = value));
			body.Children.Add(row2);

			StackPanel smtRow1 = Row();
			smtRow1.Children.Add(MakeCheck("SMT", () => ShowSmtVisuals, value => { ShowSmtVisuals = value; ClearSmtVisuals(); ApplySmtVisualConfig(); }));
			smtRow1.Children.Add(MakeCheck("Lbl", () => ShowSmtLabels, value => { ShowSmtLabels = value; ClearSmtVisuals(); ApplySmtVisualConfig(); }));
			body.Children.Add(smtRow1);

			StackPanel smtRow2 = Row();
			smtRow2.Children.Add(LabelInline("SMT TF"));
			smtRow2.Children.Add(MakeCheck("5", () => SmtTF5, value => { SmtTF5 = value; ClearSmtVisuals(); ApplySmtVisualConfig(); }));
			smtRow2.Children.Add(MakeCheck("15", () => SmtTF15, value => { SmtTF15 = value; ClearSmtVisuals(); ApplySmtVisualConfig(); }));
			smtRow2.Children.Add(MakeCheck("30", () => SmtTF30, value => { SmtTF30 = value; ClearSmtVisuals(); ApplySmtVisualConfig(); }));
			smtRow2.Children.Add(MakeCheck("60", () => SmtTF60, value => { SmtTF60 = value; ClearSmtVisuals(); ApplySmtVisualConfig(); }));
			smtRow2.Children.Add(MakeCheck("90", () => SmtTF90, value => { SmtTF90 = value; ClearSmtVisuals(); ApplySmtVisualConfig(); }));
			smtRow2.Children.Add(MakeCheck("240", () => SmtTF240, value => { SmtTF240 = value; ClearSmtVisuals(); ApplySmtVisualConfig(); }));
			body.Children.Add(smtRow2);

			body.Children.Add(Label("Setup DR"));
			body.Children.Add(ModeCombo(SetupDealingRangeMode, value => SetupDealingRangeMode = value));
			body.Children.Add(Label("Visual DR"));
			body.Children.Add(ModeCombo(VisualDealingRangeMode, value => VisualDealingRangeMode = value));

			StackPanel cycleRow = Row();
			cycleRow.Children.Add(LabelInline("Cycle"));
			cycleRow.Children.Add(MakeButton("15", () => DealingRangeCycleMinutes = 15));
			cycleRow.Children.Add(MakeButton("60", () => DealingRangeCycleMinutes = 60));
			cycleRow.Children.Add(MakeButton("90", () => DealingRangeCycleMinutes = 90));
			cycleRow.Children.Add(MakeButton("240", () => DealingRangeCycleMinutes = 240));
			body.Children.Add(cycleRow);

			StackPanel fibRow = Row();
			fibRow.Children.Add(LabelInline("Fib"));
			fibRow.Children.Add(MakeButton("Auto", () => SetVisualOteFibDirection(ICTOteFibDirection.Auto)));
			fibRow.Children.Add(MakeButton("Long", () => SetVisualOteFibDirection(ICTOteFibDirection.Long)));
			fibRow.Children.Add(MakeButton("Short", () => SetVisualOteFibDirection(ICTOteFibDirection.Short)));
			fibRow.Children.Add(MakeButton("Both", () => SetVisualOteFibDirection(ICTOteFibDirection.Both)));
			body.Children.Add(fibRow);

			StackPanel setups1 = Row();
			setups1.Children.Add(MakeCheck("2022", () => UseICT2022Setup, value => { UseICT2022Setup = value; setupEngineRebuildRequested = true; }));
			setups1.Children.Add(MakeCheck("IFVG", () => UseIFVGCISDSetup, value => { UseIFVGCISDSetup = value; setupEngineRebuildRequested = true; }));
			setups1.Children.Add(MakeCheck("BRK", () => UseBreakerSetup, value => { UseBreakerSetup = value; setupEngineRebuildRequested = true; }));
			body.Children.Add(setups1);

			StackPanel setups2 = Row();
			setups2.Children.Add(MakeCheck("UNI", () => UseUnicornSetup, value => { UseUnicornSetup = value; setupEngineRebuildRequested = true; }));
			setups2.Children.Add(MakeCheck("OTE", () => UseOTESetup, value => { UseOTESetup = value; setupEngineRebuildRequested = true; }));
			setups2.Children.Add(MakeCheck("OB", () => UseOBCISDSetup, value => { UseOBCISDSetup = value; setupEngineRebuildRequested = true; }));
			setups2.Children.Add(MakeCheck("TS", () => UseTurtleSoupSetup, value => { UseTurtleSoupSetup = value; setupEngineRebuildRequested = true; }));
			body.Children.Add(setups2);

			UpdateControlPanelStatus();
			return root;
		}

		private StackPanel Row()
		{
			return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
		}

		private TextBlock Label(string text)
		{
			return new TextBlock { Text = text, Foreground = Brushes.LightGray, Margin = new Thickness(0, 4, 0, 1), FontSize = 11 };
		}

		private TextBlock LabelInline(string text)
		{
			return new TextBlock { Text = text, Foreground = Brushes.LightGray, Margin = new Thickness(0, 5, 6, 0), FontSize = 11 };
		}

		private Button MakeButton(string text, Action action)
		{
			Button button = new Button { Content = text, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(6, 2, 6, 2), FontSize = 11, MinWidth = 46 };
			button.Click += (sender, args) => { action(); UpdateControlPanelStatus(); };
			return button;
		}

		private CheckBox MakeCheck(string text, Func<bool> getter, Action<bool> setter)
		{
			CheckBox check = new CheckBox { Content = text, IsChecked = getter(), Foreground = Brushes.WhiteSmoke, Margin = new Thickness(0, 0, 8, 0), FontSize = 11 };
			check.Checked += (sender, args) => { setter(true); UpdateControlPanelStatus(); };
			check.Unchecked += (sender, args) => { setter(false); UpdateControlPanelStatus(); };
			return check;
		}

		private ComboBox ModeCombo(ICTDealingRangeMode selected, Action<ICTDealingRangeMode> setter)
		{
			ComboBox combo = new ComboBox { Margin = new Thickness(0, 0, 0, 2), FontSize = 11, MinWidth = 170 };
			foreach (ICTDealingRangeMode mode in Enum.GetValues(typeof(ICTDealingRangeMode)))
				combo.Items.Add(mode);
			combo.SelectedItem = selected;
			combo.SelectionChanged += (sender, args) =>
			{
				if (combo.SelectedItem is ICTDealingRangeMode)
				{
					setter((ICTDealingRangeMode)combo.SelectedItem);
					UpdateControlPanelStatus();
				}
			};
			return combo;
		}

		private void UpdateControlPanelStatus()
		{
			if (controlPanelStatus == null || ChartControl == null)
				return;
			if (controlPanelUpdatePending)
				return;

			controlPanelUpdatePending = true;
			ChartControl.Dispatcher.InvokeAsync(() =>
			{
				controlPanelUpdatePending = false;
				if (controlPanelStatus == null)
					return;
				controlPanelStatus.Text =
					(Position.MarketPosition + " | " + panel.Status + " | " + panel.LongScore.ToString("0") + "/" + panel.ShortScore.ToString("0")) + "\n" +
					(EnableTrading ? "Trading ON" : "Trading OFF") + " | Exit " + (ManualExitOverride ? "MANUAL" : "AUTO") + " | 3P " + (UseThreePushBonus ? "ON" : "OFF") + "\n" +
					"Entry " + (RequireKillZoneForEntry ? "KZ ONLY" : "ALL TIME") + " | KZ " + panel.Context.KillZoneName + "\n" +
					"SMT " + (ShowSmtVisuals ? "ON" : "OFF") + " " + SmtTfText() + "\n" +
					"DR " + SetupDealingRangeMode + "/" + VisualDealingRangeMode + " " + DealingRangeCycleMinutes + "m | Fib " + VisualOteFibDirection;
				controlPanelStatus.Foreground = EnableTrading ? Brushes.LightGreen : Brushes.OrangeRed;
			});
		}

		private void SetVisualOteFibDirection(ICTOteFibDirection direction)
		{
			VisualOteFibDirection = direction;
			DrawDealingRange();
		}

		private string SmtTfText()
		{
			string text = "";
			if (SmtTF5) text += "5/";
			if (SmtTF15) text += "15/";
			if (SmtTF30) text += "30/";
			if (SmtTF60) text += "60/";
			if (SmtTF90) text += "90/";
			if (SmtTF240) text += "240/";
			return text.Length > 0 ? text.TrimEnd('/') : "-";
		}

		private void ControlPanelDragStart(object sender, MouseButtonEventArgs e)
		{
			if (controlPanelGrid == null)
				return;
			controlPanelDragging = true;
			controlPanelDragStart = e.GetPosition(ChartPanel);
			controlPanelDragMargin = controlPanelGrid.Margin;
			((UIElement)sender).CaptureMouse();
		}

		private void ControlPanelDragMove(object sender, MouseEventArgs e)
		{
			if (!controlPanelDragging || controlPanelGrid == null || ChartPanel == null)
				return;
			Point point = e.GetPosition(ChartPanel);
			double left = Math.Max(0, controlPanelDragMargin.Left + point.X - controlPanelDragStart.X);
			double top = Math.Max(0, controlPanelDragMargin.Top + point.Y - controlPanelDragStart.Y);
			controlPanelGrid.Margin = new Thickness(left, top, 0, 0);
		}

		private void ControlPanelDragEnd(object sender, MouseButtonEventArgs e)
		{
			controlPanelDragging = false;
			((UIElement)sender).ReleaseMouseCapture();
		}

		private void DrawDealingRange()
		{
			double drHigh = GetDealingRangeHigh(VisualDealingRangeMode);
			double drLow = GetDealingRangeLow(VisualDealingRangeMode);
			double drMid = (drHigh + drLow) * 0.5;
			if (!ShowDealingRangeLines || !IsValidPrice(drHigh) || !IsValidPrice(drMid) || !IsValidPrice(drLow))
			{
				RemoveDealingRangeDrawObjects();
				return;
			}

			int startBarsAgo = Math.Min(CurrentBar, Math.Max(10, DealingRangeLineLookbackBars));
			string mode = VisualDealingRangeMode.ToString();
			if (VisualDealingRangeMode == ICTDealingRangeMode.LiveCycle || VisualDealingRangeMode == ICTDealingRangeMode.CompletedCycle)
				mode += DealingRangeCycleMinutes.ToString();

			DrawDealingRangeBox(drHigh, drLow, startBarsAgo);
			DrawDrLine("SE_DR_HIGH", "DR High " + mode, drHigh, startBarsAgo, Brushes.Black, DashStyleHelper.Solid, 4);
			DrawDrLine("SE_DR_EQ", "DR EQ " + mode, drMid, startBarsAgo, Brushes.Black, DashStyleHelper.Dash, 4);
			DrawDrLine("SE_DR_LOW", "DR Low " + mode, drLow, startBarsAgo, Brushes.Black, DashStyleHelper.Solid, 4);
			DrawOteFibLines(drHigh, drLow, mode, startBarsAgo);
		}

		private void DrawDealingRangeBox(double drHigh, double drLow, int fallbackStartBarsAgo)
		{
			if (VisualDealingRangeMode == ICTDealingRangeMode.LiveCycle || VisualDealingRangeMode == ICTDealingRangeMode.CompletedCycle)
			{
				DrawCycleDealingRangeBoxes();
				return;
			}

			PrepareSingleDealingRangeBoxTag();
			DateTime boxStart;
			DateTime boxEnd;
			if (TryGetDealingRangeBoxTimes(VisualDealingRangeMode, out boxStart, out boxEnd))
			{
				Draw.Rectangle(this, "SE_DR_BOX", false,
					boxStart, drHigh,
					boxEnd, drLow,
					Brushes.Black, Brushes.Gray, 12);
				return;
			}

			Draw.Rectangle(this, "SE_DR_BOX", false,
				fallbackStartBarsAgo, drHigh,
				0, drLow,
				Brushes.Black, Brushes.Gray, 12);
		}

		private void DrawCycleDealingRangeBoxes()
		{
			int interval = Math.Max(1, DealingRangeCycleMinutes);
			int history = VisualDealingRangeMode == ICTDealingRangeMode.LiveCycle ? 1 : Math.Max(1, DealingRangeMaxHistory);
			DateTime currentCycleStart = GetCycleStart(Time[0], interval);
			int firstOffset = VisualDealingRangeMode == ICTDealingRangeMode.CompletedCycle ? 1 : 0;
			PrepareDealingRangeBoxTags(history);

			for (int index = 0; index < history; index++)
			{
				DateTime boxStart = currentCycleStart.AddMinutes(-(firstOffset + index) * interval);
				DateTime boxEnd = boxStart.AddMinutes(interval);
				if (VisualDealingRangeMode == ICTDealingRangeMode.LiveCycle && index == 0 && boxEnd > Time[0])
					boxEnd = Time[0] > boxStart ? Time[0] : boxStart.AddSeconds(1);
				if (boxEnd > Time[0])
					continue;

				double high;
				double low;
				if (!TryGetRangeBetween(boxStart, boxEnd, out high, out low))
					continue;

				Draw.Rectangle(this, "SE_DR_BOX_" + index, false,
					boxStart, high,
					boxEnd, low,
					Brushes.Black, Brushes.Gray, index == 0 ? 12 : 8);
			}
		}

		private bool TryGetRangeBetween(DateTime start, DateTime end, out double high, out double low)
		{
			high = double.NaN;
			low = double.NaN;
			bool found = false;
			bool includeEnd = end >= Time[0];
			for (int barsAgo = 0; barsAgo <= CurrentBar; barsAgo++)
			{
				DateTime barTime = Time[barsAgo];
				if (barTime < start)
					break;
				if (barTime > end || (!includeEnd && barTime >= end))
					continue;

				high = found ? Math.Max(high, High[barsAgo]) : High[barsAgo];
				low = found ? Math.Min(low, Low[barsAgo]) : Low[barsAgo];
				found = true;
			}
			return found && IsValidPrice(high) && IsValidPrice(low) && high > low;
		}

		private bool TryGetDealingRangeBoxTimes(ICTDealingRangeMode mode, out DateTime boxStart, out DateTime boxEnd)
		{
			boxStart = Core.Globals.MinDate;
			boxEnd = Core.Globals.MinDate;

			if (mode == ICTDealingRangeMode.LiveCycle || mode == ICTDealingRangeMode.CompletedCycle)
			{
				int interval = Math.Max(1, DealingRangeCycleMinutes);
				DateTime currentCycleStart = GetCycleStart(Time[0], interval);
				if (mode == ICTDealingRangeMode.CompletedCycle)
				{
					boxEnd = currentCycleStart;
					boxStart = boxEnd.AddMinutes(-interval);
				}
				else
				{
					boxStart = currentCycleStart;
					boxEnd = Time[0] > boxStart ? Time[0] : boxStart.AddSeconds(1);
				}
				return boxEnd > boxStart;
			}

			if (mode == ICTDealingRangeMode.SessionRange && sessionRangeStart > Core.Globals.MinDate)
			{
				boxStart = sessionRangeStart;
				boxEnd = Time[0] > boxStart ? Time[0] : boxStart.AddSeconds(1);
				return boxEnd > boxStart;
			}

			return false;
		}

		private DateTime GetCycleStart(DateTime time, int intervalMinutes)
		{
			DateTime baseTime = new DateTime(time.Year, time.Month, time.Day, TimeCycleStartHour, TimeCycleStartMinute, 0);
			if (time < baseTime)
				baseTime = baseTime.AddDays(-1);

			double elapsedMinutes = (time - baseTime).TotalMinutes;
			int periodIndex = Math.Max(0, (int)Math.Floor(elapsedMinutes / intervalMinutes));
			return baseTime.AddMinutes(periodIndex * intervalMinutes);
		}

		private void DrawOteFibLines(double drHigh, double drLow, string mode, int startBarsAgo)
		{
			RemoveOteFibDrawObjects();
			double range = drHigh - drLow;
			if (range <= TickSize)
				return;

			ICTOteFibDirection direction = ResolveVisualOteFibDirection();
			if (direction == ICTOteFibDirection.Long || direction == ICTOteFibDirection.Both)
			{
				DrawDrLine("SE_DR_LOTE618", "DR Long 0.618 " + mode, drHigh - range * 0.618, startBarsAgo, Brushes.Black, DashStyleHelper.Dot, 2);
				DrawDrLine("SE_DR_LOTE705", "DR Long 0.705 " + mode, drHigh - range * 0.705, startBarsAgo, Brushes.Black, DashStyleHelper.Dot, 2);
				DrawDrLine("SE_DR_LOTE786", "DR Long 0.786 " + mode, drHigh - range * 0.786, startBarsAgo, Brushes.Black, DashStyleHelper.Dot, 2);
			}
			if (direction == ICTOteFibDirection.Short || direction == ICTOteFibDirection.Both)
			{
				DrawDrLine("SE_DR_SOTE618", "DR Short 0.618 " + mode, drLow + range * 0.618, startBarsAgo, Brushes.Black, DashStyleHelper.Dot, 2);
				DrawDrLine("SE_DR_SOTE705", "DR Short 0.705 " + mode, drLow + range * 0.705, startBarsAgo, Brushes.Black, DashStyleHelper.Dot, 2);
				DrawDrLine("SE_DR_SOTE786", "DR Short 0.786 " + mode, drLow + range * 0.786, startBarsAgo, Brushes.Black, DashStyleHelper.Dot, 2);
			}
		}

		private ICTOteFibDirection ResolveVisualOteFibDirection()
		{
			if (VisualOteFibDirection != ICTOteFibDirection.Auto)
				return VisualOteFibDirection;
			if (panel.BestSignal != null && panel.BestSignal.Direction == ICTTradeDirection.Long)
				return ICTOteFibDirection.Long;
			if (panel.BestSignal != null && panel.BestSignal.Direction == ICTTradeDirection.Short)
				return ICTOteFibDirection.Short;
			if (panel.Context != null && panel.Context.HigherBias > 0)
				return ICTOteFibDirection.Long;
			if (panel.Context != null && panel.Context.HigherBias < 0)
				return ICTOteFibDirection.Short;
			return ICTOteFibDirection.Both;
		}

		private void DrawDrLine(string tag, string label, double price, int startBarsAgo, Brush brush, DashStyleHelper dash, int width)
		{
			Draw.Line(this, tag, false, startBarsAgo, price, -10, price, brush, dash, width);
			Draw.Text(this, tag + "_LBL", false, label + " " + FormatPrice(price), 0, price, 0, brush, new SimpleFont("Consolas", 10), System.Windows.TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void RemoveDealingRangeDrawObjects()
		{
			RemoveDrawObject("SE_DR_HIGH");
			RemoveDrawObject("SE_DR_EQ");
			RemoveDrawObject("SE_DR_LOW");
			RemoveDealingRangeBoxObjects();
			RemoveDrawObject("SE_DR_HIGH_LBL");
			RemoveDrawObject("SE_DR_EQ_LBL");
			RemoveDrawObject("SE_DR_LOW_LBL");
			RemoveOteFibDrawObjects();
		}

		private void RemoveDealingRangeBoxObjects()
		{
			RemoveDrawObject("SE_DR_BOX");
			for (int i = 0; i < 50; i++)
				RemoveDrawObject("SE_DR_BOX_" + i);
			lastDrBoxActiveCount = -1;
		}

		private void PrepareDealingRangeBoxTags(int activeCount)
		{
			RemoveDrawObject("SE_DR_BOX");
			if (lastDrBoxMode == VisualDealingRangeMode && lastDrBoxActiveCount == activeCount)
				return;

			for (int i = activeCount; i < 50; i++)
				RemoveDrawObject("SE_DR_BOX_" + i);

			lastDrBoxMode = VisualDealingRangeMode;
			lastDrBoxActiveCount = activeCount;
		}

		private void PrepareSingleDealingRangeBoxTag()
		{
			if (lastDrBoxMode == VisualDealingRangeMode && lastDrBoxActiveCount == 0)
				return;

			for (int i = 0; i < 50; i++)
				RemoveDrawObject("SE_DR_BOX_" + i);

			lastDrBoxMode = VisualDealingRangeMode;
			lastDrBoxActiveCount = 0;
		}

		private void RemoveOteFibDrawObjects()
		{
			string[] tags = new string[]
			{
				"SE_DR_OTE62", "SE_DR_OTE705", "SE_DR_OTE79",
				"SE_DR_LOTE618", "SE_DR_LOTE705", "SE_DR_LOTE786",
				"SE_DR_SOTE618", "SE_DR_SOTE705", "SE_DR_SOTE786"
			};
			foreach (string tag in tags)
			{
				RemoveDrawObject(tag);
				RemoveDrawObject(tag + "_LBL");
			}
		}

		private void DebugState(ICTMarketContext context, ICTSetupSignal signal)
		{
			if (!DebugPrint)
				return;

			string reason = panel.BlockReason + "|" + DescribeSignal(signal);
			if (panel.Status == lastDebugStatus && reason == lastDebugReason)
				return;

			lastDebugStatus = panel.Status;
			lastDebugReason = reason;
			Debug("STATE " + panel.Status + " | " + DescribeContext(context) + " | " + DescribeSignal(signal) + " | block=" + panel.BlockReason);
		}

		private void Debug(string message)
		{
			if (DebugPrint)
				Print(Time[0].ToString("yyyy-MM-dd HH:mm:ss") + " Setup_Engine " + message);
		}

		private string DescribeContext(ICTMarketContext c)
		{
			return "KZ=" + c.KillZoneName +
				" DR=" + FormatPrice(c.DealingRangeHigh) + "/" + FormatPrice(c.PremiumDiscountMid) + "/" + FormatPrice(c.DealingRangeLow) +
				" PD=" + (c.InDiscount ? "D" : (c.InPremium ? "P" : "-")) +
				" sweepL/H=" + AgeText(c.SellsideSweepAge) + "/" + AgeText(c.BuysideSweepAge) +
				" mssB/S=" + AgeText(c.BullMssAge) + "/" + AgeText(c.BearMssAge) +
				" smt=" + (c.BullSmt ? "B" : (c.BearSmt ? "S" : "-")) + c.SmtTimeframe;
		}

		private string DescribeSignal(ICTSetupSignal signal)
		{
			if (signal == null)
				return "signal=null";
			return signal.Kind + " " + signal.Direction + " score=" + signal.Score +
				" entry=" + FormatPrice(signal.Entry) + " stop=" + FormatPrice(signal.Stop) + " target=" + FormatPrice(signal.Target) +
				" zone=" + FormatPrice(signal.ZoneTop) + "/" + FormatPrice(signal.ZoneBottom) +
				" reason=" + signal.Reason;
		}

		private string AgeText(int age)
		{
			return age >= 0 ? age.ToString() : "-";
		}

		private string FormatPrice(double price)
		{
			if (price <= 0 || double.IsNaN(price) || double.IsInfinity(price))
				return "-";
			return Instrument.MasterInstrument.FormatPrice(price);
		}

		[NinjaScriptProperty]
		[Display(Name = "Enable Trading", GroupName = "00. Master", Order = 0)]
		public bool EnableTrading { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Manual Exit Override", GroupName = "00. Master", Order = 1)]
		public bool ManualExitOverride { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Panel", GroupName = "00. Master", Order = 2)]
		public bool ShowPanel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Detailed Panel", GroupName = "00. Master", Order = 3)]
		public bool ShowDetailedPanel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Floating Control Panel", GroupName = "00. Master", Order = 4)]
		public bool ShowControlPanel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Three Push Bonus", GroupName = "03. Setup Switches", Order = 7)]
		public bool UseThreePushBonus { get; set; }

		[NinjaScriptProperty]
		[Range(10, 300)]
		[Display(Name = "Three Push Lookback Bars", GroupName = "08. Scoring / Arbitration", Order = 4)]
		public int ThreePushLookbackBars { get; set; }

		[NinjaScriptProperty]
		[Range(0, 30)]
		[Display(Name = "Three Push Bonus Score", GroupName = "08. Scoring / Arbitration", Order = 5)]
		public int ThreePushBonusScore { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Three Push Quantity", GroupName = "08. Scoring / Arbitration", Order = 6)]
		public int ThreePushQuantity { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Dealing Range Lines", GroupName = "04. Dealing Range", Order = 0)]
		public bool ShowDealingRangeLines { get; set; }

		[NinjaScriptProperty]
		[Range(10, 2000)]
		[Display(Name = "Dealing Range Line Lookback Bars", GroupName = "04. Dealing Range", Order = 4)]
		public int DealingRangeLineLookbackBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Dealing Range Max History", GroupName = "04. Dealing Range", Order = 5)]
		public int DealingRangeMaxHistory { get; set; }

		[NinjaScriptProperty]
		[Range(15, 240)]
		[Display(Name = "Dealing Range Cycle Minutes", GroupName = "04. Dealing Range", Order = 3)]
		public int DealingRangeCycleMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Setup Dealing Range Mode", GroupName = "04. Dealing Range", Order = 1)]
		public ICTDealingRangeMode SetupDealingRangeMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Visual Dealing Range Mode", GroupName = "04. Dealing Range", Order = 2)]
		public ICTDealingRangeMode VisualDealingRangeMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Visual OTE Fib Direction", GroupName = "04. Dealing Range", Order = 6)]
		public ICTOteFibDirection VisualOteFibDirection { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Debug Print", GroupName = "00. Master", Order = 7)]
		public bool DebugPrint { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trade Long", GroupName = "00. Master", Order = 5)]
		public bool TradeLong { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trade Short", GroupName = "00. Master", Order = 6)]
		public bool TradeShort { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use ICT2022", GroupName = "03. Setup Switches", Order = 0)]
		public bool UseICT2022Setup { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use IFVG-CISD", GroupName = "03. Setup Switches", Order = 1)]
		public bool UseIFVGCISDSetup { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Breaker", GroupName = "03. Setup Switches", Order = 2)]
		public bool UseBreakerSetup { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Unicorn", GroupName = "03. Setup Switches", Order = 3)]
		public bool UseUnicornSetup { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use OTE", GroupName = "03. Setup Switches", Order = 4)]
		public bool UseOTESetup { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use OB-CISD", GroupName = "03. Setup Switches", Order = 5)]
		public bool UseOBCISDSetup { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Turtle Soup", GroupName = "03. Setup Switches", Order = 6)]
		public bool UseTurtleSoupSetup { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Minimum Score", GroupName = "08. Scoring / Arbitration", Order = 0)]
		public int MinimumScore { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 10)]
		[Display(Name = "Minimum RR", GroupName = "08. Scoring / Arbitration", Order = 1)]
		public double MinimumRR { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Fixed Quantity", GroupName = "09. Risk", Order = 0)]
		public bool UseFixedQuantity { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Fixed Quantity", GroupName = "09. Risk", Order = 1)]
		public int FixedQuantity { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100000)]
		[Display(Name = "Account Risk Dollars", GroupName = "09. Risk", Order = 2)]
		public double AccountRiskDollars { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100000)]
		[Display(Name = "Max Daily Loss Dollars", GroupName = "09. Risk", Order = 3)]
		public double MaxDailyLossDollars { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1000)]
		[Display(Name = "Max Trades Per Day", GroupName = "09. Risk", Order = 4)]
		public int MaxTradesPerDay { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1000)]
		[Display(Name = "Max Stop Points", GroupName = "09. Risk", Order = 5)]
		public double MaxStopPoints { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Break Even", GroupName = "11. Exit / Management", Order = 0)]
		public bool UseBreakEven { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 10)]
		[Display(Name = "Break Even At R", GroupName = "11. Exit / Management", Order = 1)]
		public double BreakEvenAtR { get; set; }

		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "Break Even Plus Ticks", GroupName = "11. Exit / Management", Order = 2)]
		public int BreakEvenPlusTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Partial At 1R", GroupName = "11. Exit / Management", Order = 3)]
		public bool UsePartialAt1R { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 10)]
		[Display(Name = "Partial At R", GroupName = "11. Exit / Management", Order = 4)]
		public double PartialAtR { get; set; }

		[NinjaScriptProperty]
		[Range(0.2, 20)]
		[Display(Name = "Runner Target R", GroupName = "11. Exit / Management", Order = 6)]
		public double RunnerTargetR { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Partial Quantity", GroupName = "11. Exit / Management", Order = 5)]
		public int PartialQuantity { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Manual Adjust Ticks", GroupName = "11. Exit / Management", Order = 10)]
		public int ManualAdjustTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Protective Stop Buffer Ticks", GroupName = "09. Risk", Order = 6)]
		public int ProtectiveStopBufferTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Limit Entries", GroupName = "10. Entry", Order = 0)]
		public bool UseLimitEntries { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow Market If Entry Touched", GroupName = "10. Entry", Order = 1)]
		public bool AllowMarketIfEntryTouched { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Entry Timeout Bars", GroupName = "10. Entry", Order = 2)]
		public int EntryTimeoutBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use ATR Trailing Stop", GroupName = "11. Exit / Management", Order = 7)]
		public bool UseAtrTrailingStop { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 10)]
		[Display(Name = "Trail After R", GroupName = "11. Exit / Management", Order = 8)]
		public double TrailAfterR { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 10)]
		[Display(Name = "Trail ATR Multiple", GroupName = "11. Exit / Management", Order = 9)]
		public double TrailAtrMultiple { get; set; }

		[NinjaScriptProperty]
		[Range(2, 20)]
		[Display(Name = "Swing Length", GroupName = "05. Structure", Order = 0)]
		public int SwingLength { get; set; }

		[NinjaScriptProperty]
		[Range(2, 30)]
		[Display(Name = "CISD Lookback", GroupName = "05. Structure", Order = 1)]
		public int CisdLookback { get; set; }

		[NinjaScriptProperty]
		[Range(3, 100)]
		[Display(Name = "ATR Period", GroupName = "09. Risk", Order = 7)]
		public int AtrPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Min Bars Between Entries", GroupName = "08. Scoring / Arbitration", Order = 2)]
		public int MinBarsBetweenEntries { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Sweep TTL Bars", GroupName = "08. Scoring / Arbitration", Order = 3)]
		public int SweepTtlBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "MSS TTL Bars", GroupName = "05. Structure", Order = 2)]
		public int MssTtlBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "CISD TTL Bars", GroupName = "05. Structure", Order = 3)]
		public int CisdTtlBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "PDA TTL Bars", GroupName = "06. PDA / Imbalance", Order = 0)]
		public int PdaTtlBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 2000)]
		[Display(Name = "SMT TTL Bars", GroupName = "07. SMT", Order = 2)]
		public int SmtTtlBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use TimeCycle Inputs", GroupName = "01. Instruments / Data", Order = 0)]
		public bool UseTimeCycleInputs { get; set; }

		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name = "TimeCycle Start Hour", GroupName = "01. Instruments / Data", Order = 1)]
		public int TimeCycleStartHour { get; set; }

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name = "TimeCycle Start Minute", GroupName = "01. Instruments / Data", Order = 2)]
		public int TimeCycleStartMinute { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use SMT Inputs", GroupName = "01. Instruments / Data", Order = 9)]
		public bool UseSmtInputs { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "SMT Asset A", GroupName = "01. Instruments / Data", Order = 10)]
		public string SmtAssetA { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "SMT Asset B", GroupName = "01. Instruments / Data", Order = 11)]
		public string SmtAssetB { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show SMT Visuals", GroupName = "07. SMT", Order = 0)]
		public bool ShowSmtVisuals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show SMT Labels", GroupName = "07. SMT", Order = 1)]
		public bool ShowSmtLabels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "SMT TF 5", GroupName = "07. SMT", Order = 3)]
		public bool SmtTF5 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "SMT TF 15", GroupName = "07. SMT", Order = 4)]
		public bool SmtTF15 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "SMT TF 30", GroupName = "07. SMT", Order = 5)]
		public bool SmtTF30 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "SMT TF 60", GroupName = "07. SMT", Order = 6)]
		public bool SmtTF60 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "SMT TF 90", GroupName = "07. SMT", Order = 7)]
		public bool SmtTF90 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "SMT TF 240", GroupName = "07. SMT", Order = 8)]
		public bool SmtTF240 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Structure Inputs", GroupName = "01. Instruments / Data", Order = 3)]
		public bool UseStructureInputs { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Structure Visuals", GroupName = "01. Instruments / Data", Order = 4)]
		public bool ShowStructureVisuals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use PDA Inputs", GroupName = "01. Instruments / Data", Order = 5)]
		public bool UsePdaInputs { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use HTF PDA Projector Inputs", GroupName = "01. Instruments / Data", Order = 6)]
		public bool UseHtfPdaProjectorInputs { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use M1 Execution PDA", GroupName = "01. Instruments / Data", Order = 7)]
		public bool UseM1ExecutionPda { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use HTF Suite Targets", GroupName = "01. Instruments / Data", Order = 8)]
		public bool UseHtfSuiteTargets { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use KillZones", GroupName = "02. Sessions / Time Filter", Order = 0)]
		public bool UseKillZones { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require KillZone For Entry", GroupName = "02. Sessions / Time Filter", Order = 1)]
		public bool RequireKillZoneForEntry { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "Asian Start HHmm", GroupName = "02. Sessions / Time Filter", Order = 2)]
		public int AsianStart { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "Asian End HHmm", GroupName = "02. Sessions / Time Filter", Order = 3)]
		public int AsianEnd { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "London Start HHmm", GroupName = "02. Sessions / Time Filter", Order = 4)]
		public int LondonStart { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "London End HHmm", GroupName = "02. Sessions / Time Filter", Order = 5)]
		public int LondonEnd { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "NY AM Start HHmm", GroupName = "02. Sessions / Time Filter", Order = 6)]
		public int NewYorkAMStart { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "NY AM End HHmm", GroupName = "02. Sessions / Time Filter", Order = 7)]
		public int NewYorkAMEnd { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "NY PM Start HHmm", GroupName = "02. Sessions / Time Filter", Order = 8)]
		public int NewYorkPMStart { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "NY PM End HHmm", GroupName = "02. Sessions / Time Filter", Order = 9)]
		public int NewYorkPMEnd { get; set; }
	}
}
