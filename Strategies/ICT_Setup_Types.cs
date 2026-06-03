#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public enum ICTSetupKind
	{
		None,
		ICT2022,
		IFVG_CISD,
		Breaker,
		Unicorn,
		OTE,
		OB_CISD,
		TurtleSoup
	}

	public enum ICTTradeDirection
	{
		None = 0,
		Long = 1,
		Short = -1
	}

	public enum ICTDealingRangeMode
	{
		LiveCycle,
		CompletedCycle,
		SessionRange,
		SwingRange
	}

	public enum ICTOteFibDirection
	{
		Auto,
		Long,
		Short,
		Both
	}

	public class ICTMarketContext
	{
		public int CurrentBar;
		public DateTime Time;
		public double Open;
		public double High;
		public double Low;
		public double Close;
		public double Atr;
		public double TickSize;
		public double PointValue;
		public bool InKillZone;
		public string KillZoneName;
		public int HigherBias;
		public double PremiumDiscountMid;
		public double DealingRangeHigh;
		public double DealingRangeLow;
		public bool InPremium;
		public bool InDiscount;
		public double RecentSwingHigh;
		public double RecentSwingLow;
		public double BullPdaTop;
		public double BullPdaBottom;
		public double BullPdaMid;
		public double BearPdaTop;
		public double BearPdaBottom;
		public double BearPdaMid;
		public double SweepHigh;
		public double SweepLow;
		public double PriorDayHigh;
		public double PriorDayLow;
		public bool SweptHigh;
		public bool SweptLow;
		public bool BullMss;
		public bool BearMss;
		public bool BullCisd;
		public bool BearCisd;
		public bool BullFvg;
		public bool BearFvg;
		public bool BullFvgTapped;
		public bool BearFvgTapped;
		public int BullFvgAge;
		public int BearFvgAge;
		public bool BullIfvg;
		public bool BearIfvg;
		public bool BullBreaker;
		public bool BearBreaker;
		public bool BullOb;
		public bool BearOb;
		public bool BullUnicorn;
		public bool BearUnicorn;
		public bool BullSmt;
		public bool BearSmt;
		public int SmtTimeframe;
		public bool ThreePushHigh;
		public bool ThreePushLow;
		public int ThreePushHighAge;
		public int ThreePushLowAge;
		public int SellsideSweepAge;
		public int BuysideSweepAge;
		public int BullMssAge;
		public int BearMssAge;
		public int BullCisdAge;
		public int BearCisdAge;
		public int BullSmtAge;
		public int BearSmtAge;
		public List<ICTKeyLevelFact> HtfKeyLevels = new List<ICTKeyLevelFact>();
		public string Notes = string.Empty;
	}

	public class ICTFactSnapshot
	{
		public int CurrentBar;
		public DateTime Time;
		public double Open;
		public double High;
		public double Low;
		public double Close;
		public double Atr;
		public double TickSize;
		public double PointValue;
		public bool InKillZone;
		public string KillZoneName = "Off";
		public int HigherBias;
		public double PriorDayHigh = double.NaN;
		public double PriorDayLow = double.NaN;
		public double DealingRangeHigh = double.NaN;
		public double DealingRangeLow = double.NaN;
		public double DealingRangeMid = double.NaN;
		public bool InPremium;
		public bool InDiscount;
		public double RecentSwingHigh = double.NaN;
		public double RecentSwingLow = double.NaN;
		public bool SellsideSweepEvent;
		public bool BuysideSweepEvent;
		public bool BullMssEvent;
		public bool BearMssEvent;
		public bool BullCisdEvent;
		public bool BearCisdEvent;
		public bool BullFvgEvent;
		public bool BearFvgEvent;
		public bool BullIfvgEvent;
		public bool BearIfvgEvent;
		public bool BullBreakerEvent;
		public bool BearBreakerEvent;
		public bool BullObEvent;
		public bool BearObEvent;
		public bool BullUnicornEvent;
		public bool BearUnicornEvent;
		public bool BullSmtEvent;
		public bool BearSmtEvent;
		public int SmtTimeframe;
		public bool ThreePushHighEvent;
		public bool ThreePushLowEvent;
		public double BullFvgTop = double.NaN;
		public double BullFvgBottom = double.NaN;
		public double BearFvgTop = double.NaN;
		public double BearFvgBottom = double.NaN;
		public double BullPdaTop = double.NaN;
		public double BullPdaBottom = double.NaN;
		public double BearPdaTop = double.NaN;
		public double BearPdaBottom = double.NaN;
		public List<ICTKeyLevelFact> HtfKeyLevels = new List<ICTKeyLevelFact>();
	}

	public class ICTEventLatch
	{
		public int SweepTtlBars = 20;
		public int MssTtlBars = 12;
		public int CisdTtlBars = 8;
		public int PdaTtlBars = 10;
		public int SmtTtlBars = 80;

		private int sellsideSweepBar = -1;
		private int buysideSweepBar = -1;
		private int bullMssBar = -1;
		private int bearMssBar = -1;
		private int bullCisdBar = -1;
		private int bearCisdBar = -1;
		private int bullPdaBar = -1;
		private int bearPdaBar = -1;
		private int bullFvgBar = -1;
		private int bearFvgBar = -1;
		private int bullSmtBar = -1;
		private int bearSmtBar = -1;
		private int threePushHighBar = -1;
		private int threePushLowBar = -1;
		private int bullSmtTf;
		private int bearSmtTf;
		private double sweepHigh = double.NaN;
		private double sweepLow = double.NaN;
		private double bullPdaTop = double.NaN;
		private double bullPdaBottom = double.NaN;
		private double bearPdaTop = double.NaN;
		private double bearPdaBottom = double.NaN;
		private double bullFvgTop = double.NaN;
		private double bullFvgBottom = double.NaN;
		private double bearFvgTop = double.NaN;
		private double bearFvgBottom = double.NaN;
		private bool bullFvgTapped;
		private bool bearFvgTapped;

		public void Update(ICTFactSnapshot f)
		{
			if (f.SellsideSweepEvent) { sellsideSweepBar = f.CurrentBar; sweepLow = f.Low; }
			if (f.BuysideSweepEvent) { buysideSweepBar = f.CurrentBar; sweepHigh = f.High; }
			if (f.BullMssEvent) bullMssBar = f.CurrentBar;
			if (f.BearMssEvent) bearMssBar = f.CurrentBar;
			if (f.BullCisdEvent) bullCisdBar = f.CurrentBar;
			if (f.BearCisdEvent) bearCisdBar = f.CurrentBar;
			UpdateFvgLifecycle(f);

			if (f.BullIfvgEvent || f.BullUnicornEvent)
			{
				bullPdaBar = f.CurrentBar;
				bullPdaTop = f.BullPdaTop;
				bullPdaBottom = f.BullPdaBottom;
			}
			if (f.BearIfvgEvent || f.BearUnicornEvent)
			{
				bearPdaBar = f.CurrentBar;
				bearPdaTop = f.BearPdaTop;
				bearPdaBottom = f.BearPdaBottom;
			}
			if (f.BullSmtEvent) { bullSmtBar = f.CurrentBar; bullSmtTf = f.SmtTimeframe; }
			if (f.BearSmtEvent) { bearSmtBar = f.CurrentBar; bearSmtTf = f.SmtTimeframe; }
			if (f.ThreePushHighEvent) threePushHighBar = f.CurrentBar;
			if (f.ThreePushLowEvent) threePushLowBar = f.CurrentBar;
		}

		public ICTMarketContext ToContext(ICTFactSnapshot f)
		{
			bool hasSellsideSweep = IsFresh(sellsideSweepBar, f.CurrentBar, SweepTtlBars);
			bool hasBuysideSweep = IsFresh(buysideSweepBar, f.CurrentBar, SweepTtlBars);
			bool hasBullMss = IsFresh(bullMssBar, f.CurrentBar, MssTtlBars);
			bool hasBearMss = IsFresh(bearMssBar, f.CurrentBar, MssTtlBars);
			bool hasBullCisd = IsFresh(bullCisdBar, f.CurrentBar, CisdTtlBars);
			bool hasBearCisd = IsFresh(bearCisdBar, f.CurrentBar, CisdTtlBars);
			bool hasBullPda = IsFresh(bullPdaBar, f.CurrentBar, PdaTtlBars);
			bool hasBearPda = IsFresh(bearPdaBar, f.CurrentBar, PdaTtlBars);
			bool hasBullFvg = IsFresh(bullFvgBar, f.CurrentBar, PdaTtlBars);
			bool hasBearFvg = IsFresh(bearFvgBar, f.CurrentBar, PdaTtlBars);
			bool hasBullSmt = IsFresh(bullSmtBar, f.CurrentBar, SmtTtlBars);
			bool hasBearSmt = IsFresh(bearSmtBar, f.CurrentBar, SmtTtlBars);
			bool hasThreePushHigh = IsFresh(threePushHighBar, f.CurrentBar, SweepTtlBars);
			bool hasThreePushLow = IsFresh(threePushLowBar, f.CurrentBar, SweepTtlBars);

			return new ICTMarketContext
			{
				CurrentBar = f.CurrentBar,
				Time = f.Time,
				Open = f.Open,
				High = f.High,
				Low = f.Low,
				Close = f.Close,
				Atr = f.Atr,
				TickSize = f.TickSize,
				PointValue = f.PointValue,
				InKillZone = f.InKillZone,
				KillZoneName = f.KillZoneName,
				HigherBias = f.HigherBias,
				PremiumDiscountMid = f.DealingRangeMid,
				DealingRangeHigh = f.DealingRangeHigh,
				DealingRangeLow = f.DealingRangeLow,
				InPremium = f.InPremium,
				InDiscount = f.InDiscount,
				RecentSwingHigh = f.RecentSwingHigh,
				RecentSwingLow = f.RecentSwingLow,
				BullPdaTop = hasBullPda ? bullPdaTop : double.NaN,
				BullPdaBottom = hasBullPda ? bullPdaBottom : double.NaN,
				BullPdaMid = hasBullPda ? (bullPdaTop + bullPdaBottom) * 0.5 : double.NaN,
				BearPdaTop = hasBearPda ? bearPdaTop : double.NaN,
				BearPdaBottom = hasBearPda ? bearPdaBottom : double.NaN,
				BearPdaMid = hasBearPda ? (bearPdaTop + bearPdaBottom) * 0.5 : double.NaN,
				SweepHigh = sweepHigh,
				SweepLow = sweepLow,
				PriorDayHigh = f.PriorDayHigh,
				PriorDayLow = f.PriorDayLow,
				SweptHigh = hasBuysideSweep,
				SweptLow = hasSellsideSweep,
				BullMss = hasBullMss && OccurredAfter(bullMssBar, sellsideSweepBar),
				BearMss = hasBearMss && OccurredAfter(bearMssBar, buysideSweepBar),
				BullCisd = hasBullCisd && OccurredAfter(bullCisdBar, sellsideSweepBar),
				BearCisd = hasBearCisd && OccurredAfter(bearCisdBar, buysideSweepBar),
				BullFvg = hasBullFvg,
				BearFvg = hasBearFvg,
				BullFvgTapped = hasBullFvg && bullFvgTapped,
				BearFvgTapped = hasBearFvg && bearFvgTapped,
				BullFvgAge = Age(bullFvgBar, f.CurrentBar),
				BearFvgAge = Age(bearFvgBar, f.CurrentBar),
				BullIfvg = f.BullIfvgEvent,
				BearIfvg = f.BearIfvgEvent,
				BullBreaker = f.BullBreakerEvent,
				BearBreaker = f.BearBreakerEvent,
				BullOb = f.BullObEvent,
				BearOb = f.BearObEvent,
				BullUnicorn = f.BullUnicornEvent || (hasBullPda && f.BullBreakerEvent),
				BearUnicorn = f.BearUnicornEvent || (hasBearPda && f.BearBreakerEvent),
				BullSmt = hasBullSmt,
				BearSmt = hasBearSmt,
				SmtTimeframe = hasBullSmt ? bullSmtTf : (hasBearSmt ? bearSmtTf : 0),
				ThreePushHigh = hasThreePushHigh,
				ThreePushLow = hasThreePushLow,
				ThreePushHighAge = Age(threePushHighBar, f.CurrentBar),
				ThreePushLowAge = Age(threePushLowBar, f.CurrentBar),
				SellsideSweepAge = Age(sellsideSweepBar, f.CurrentBar),
				BuysideSweepAge = Age(buysideSweepBar, f.CurrentBar),
				BullMssAge = Age(bullMssBar, f.CurrentBar),
				BearMssAge = Age(bearMssBar, f.CurrentBar),
				BullCisdAge = Age(bullCisdBar, f.CurrentBar),
				BearCisdAge = Age(bearCisdBar, f.CurrentBar),
				BullSmtAge = Age(bullSmtBar, f.CurrentBar),
				BearSmtAge = Age(bearSmtBar, f.CurrentBar),
				HtfKeyLevels = f.HtfKeyLevels != null ? new List<ICTKeyLevelFact>(f.HtfKeyLevels) : new List<ICTKeyLevelFact>()
			};
		}

		private bool IsFresh(int eventBar, int currentBar, int ttl)
		{
			return eventBar >= 0 && currentBar >= eventBar && currentBar - eventBar <= ttl;
		}

		private void UpdateFvgLifecycle(ICTFactSnapshot f)
		{
			if (IsFresh(bullFvgBar, f.CurrentBar, PdaTtlBars))
			{
				if (f.Low <= bullFvgTop && f.Close >= bullFvgBottom)
					bullFvgTapped = true;
				if (f.Close < bullFvgBottom)
					ClearBullFvg();
			}

			if (IsFresh(bearFvgBar, f.CurrentBar, PdaTtlBars))
			{
				if (f.High >= bearFvgBottom && f.Close <= bearFvgTop)
					bearFvgTapped = true;
				if (f.Close > bearFvgTop)
					ClearBearFvg();
			}

			bool bullSequence = IsFresh(sellsideSweepBar, f.CurrentBar, SweepTtlBars)
				&& ((IsFresh(bullMssBar, f.CurrentBar, MssTtlBars) && OccurredAfter(bullMssBar, sellsideSweepBar))
					|| (IsFresh(bullCisdBar, f.CurrentBar, CisdTtlBars) && OccurredAfter(bullCisdBar, sellsideSweepBar)));
			bool bearSequence = IsFresh(buysideSweepBar, f.CurrentBar, SweepTtlBars)
				&& ((IsFresh(bearMssBar, f.CurrentBar, MssTtlBars) && OccurredAfter(bearMssBar, buysideSweepBar))
					|| (IsFresh(bearCisdBar, f.CurrentBar, CisdTtlBars) && OccurredAfter(bearCisdBar, buysideSweepBar)));

			if (f.BullFvgEvent && bullSequence && IsValidZone(f.BullFvgTop, f.BullFvgBottom))
			{
				bullFvgBar = f.CurrentBar;
				bullFvgTop = Math.Max(f.BullFvgTop, f.BullFvgBottom);
				bullFvgBottom = Math.Min(f.BullFvgTop, f.BullFvgBottom);
				bullFvgTapped = false;
				bullPdaBar = f.CurrentBar;
				bullPdaTop = bullFvgTop;
				bullPdaBottom = bullFvgBottom;
			}

			if (f.BearFvgEvent && bearSequence && IsValidZone(f.BearFvgTop, f.BearFvgBottom))
			{
				bearFvgBar = f.CurrentBar;
				bearFvgTop = Math.Max(f.BearFvgTop, f.BearFvgBottom);
				bearFvgBottom = Math.Min(f.BearFvgTop, f.BearFvgBottom);
				bearFvgTapped = false;
				bearPdaBar = f.CurrentBar;
				bearPdaTop = bearFvgTop;
				bearPdaBottom = bearFvgBottom;
			}
		}

		private void ClearBullFvg()
		{
			bullFvgBar = -1;
			bullFvgTop = double.NaN;
			bullFvgBottom = double.NaN;
			bullFvgTapped = false;
		}

		private void ClearBearFvg()
		{
			bearFvgBar = -1;
			bearFvgTop = double.NaN;
			bearFvgBottom = double.NaN;
			bearFvgTapped = false;
		}

		private bool IsValidZone(double top, double bottom)
		{
			return top > 0 && bottom > 0 && !double.IsNaN(top) && !double.IsNaN(bottom) && !double.IsInfinity(top) && !double.IsInfinity(bottom);
		}

		private bool OccurredAfter(int laterBar, int earlierBar)
		{
			return laterBar >= 0 && earlierBar >= 0 && laterBar >= earlierBar;
		}

		private int Age(int eventBar, int currentBar)
		{
			return eventBar >= 0 ? currentBar - eventBar : -1;
		}
	}

	public class ICTSetupSignal
	{
		public ICTSetupKind Kind = ICTSetupKind.None;
		public ICTTradeDirection Direction = ICTTradeDirection.None;
		public int Score;
		public double Entry;
		public double Stop;
		public double Target;
		public double ZoneTop = double.NaN;
		public double ZoneBottom = double.NaN;
		public bool PreferLimitEntry = true;
		public string EntryModel = string.Empty;
		public string Reason = string.Empty;

		public bool IsValid
		{
			get { return Kind != ICTSetupKind.None && Direction != ICTTradeDirection.None && Entry > 0 && Stop > 0 && Target > 0; }
		}

		public double ZoneMid
		{
			get
			{
				if (ZoneTop > 0 && ZoneBottom > 0 && !double.IsNaN(ZoneTop) && !double.IsNaN(ZoneBottom))
					return (ZoneTop + ZoneBottom) * 0.5;
				return double.NaN;
			}
		}

		public double RiskPoints
		{
			get { return Math.Abs(Entry - Stop); }
		}
	}

	public class ICTPanelState
	{
		public string Status = "WARMUP";
		public ICTSetupSignal BestSignal = new ICTSetupSignal();
		public ICTMarketContext Context = new ICTMarketContext();
		public string BlockReason = string.Empty;
		public double LongScore;
		public double ShortScore;
		public int TradesToday;
		public double DailyPnL;
	}

	public interface IICTSetup
	{
		ICTSetupSignal Evaluate(ICTMarketContext context);
	}
}
