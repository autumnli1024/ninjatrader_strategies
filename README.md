# NinjaTrader 8 ICT / TTFM Strategies

This repository mirrors selected NinjaTrader 8 custom source files used for ICT and TTFM context analysis.

## Current milestone

- `Strategies/TTFM_Context_Engine.cs`
  - H1/H4 TTFM context strategy.
  - Candle number state machine for C2-C6.
  - Independent CISD state machine.
  - HTF PDA / FVG ROI integration.
  - NT8 relay bridge publishing and snapshot support.
- `Strategies/TTFM_Types.cs`
  - Shared TTFM context data contracts and bus.
- `AddOns/NT8RelayBridge.cs`
  - Local HTTP bridge for Codex/AI access to NT8 context snapshots and memory bus.
- `Indicators/ICT_HTF_PDA_Projector.cs`
  - HTF imbalance / PDA ROI source used by TTFM context.

## Notes

The live NinjaTrader files remain under:

`C:\Users\liyan\Documents\NinjaTrader 8\bin\Custom`

Before each iteration, back up live NT8 files first, then sync stable changes into this repository.
