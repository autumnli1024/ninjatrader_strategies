#region Using declarations
using System;
using System.Collections.Generic;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public enum TTFMSetupStatus
	{
		Unknown,
		Forming,
		CandidateC2,
		WaitingC3,
		Valid,
		Paused,
		Completed,
		Failed
	}

	public class TTFM_Context
	{
		public string Key = string.Empty;
		public string Instrument = string.Empty;
		public string Profile = "D1-H1-M5-M1";
		public DateTime PublishedAt = Core.Globals.MinDate;
		public DateTime BarTime = Core.Globals.MinDate;
		public double CurrentPrice = double.NaN;
		public int Bias;
		public int Confidence;
		public ICTContextDirection AllowedDirection = ICTContextDirection.None;
		public TTFMSetupStatus SetupStatus = TTFMSetupStatus.Unknown;
		public string SetupPhase = string.Empty;
		public string Location = string.Empty;
		public int SetupId;
		public int SetupAgeBars;
		public string FailureReason = string.Empty;
		public int SequenceDirection;
		public int CurrentCandleNumber;
		public bool TSpotTouched;
		public bool TSpotViolated;
		public bool ProtectedSwingBroken;
		public bool CisdEnabled;
		public bool CisdConfirmed;
		public int CisdMinutes;
		public int CisdDirection;
		public int CisdAgeBars;
		public double CisdLevel = double.NaN;
		public double DistanceToCisdTicks = double.NaN;
		public DateTime CisdTime = Core.Globals.MinDate;
		public double DistanceToTSpotTicks = double.NaN;
		public double DistanceToProtectedTicks = double.NaN;

		public double C1High = double.NaN;
		public double C1Low = double.NaN;
		public double C1Open = double.NaN;
		public double C1Close = double.NaN;
		public DateTime C1Time = Core.Globals.MinDate;

		public double C2High = double.NaN;
		public double C2Low = double.NaN;
		public double C2Open = double.NaN;
		public double C2Close = double.NaN;
		public DateTime C2Time = Core.Globals.MinDate;

		public double C3High = double.NaN;
		public double C3Low = double.NaN;
		public double C3Open = double.NaN;
		public double C3Close = double.NaN;
		public double C3Eq = double.NaN;
		public DateTime C3Time = Core.Globals.MinDate;

		public double C4High = double.NaN;
		public double C4Low = double.NaN;
		public double C4Open = double.NaN;
		public double C4Close = double.NaN;
		public DateTime C4Time = Core.Globals.MinDate;

		public double TSpotUpper = double.NaN;
		public double TSpotLower = double.NaN;
		public double ProtectedSwing = double.NaN;
		public double InvalidationPrice = double.NaN;

		public double NearestBullZoneTop = double.NaN;
		public double NearestBullZoneBottom = double.NaN;
		public double NearestBullZoneCE = double.NaN;
		public double NearestBearZoneTop = double.NaN;
		public double NearestBearZoneBottom = double.NaN;
		public double NearestBearZoneCE = double.NaN;
		public string RoiSource = string.Empty;
		public int RoiHtfMinutes;
		public int RoiActiveBias;
		public string Narrative = string.Empty;
	}

	public static class TTFM_Context_Bus
	{
		private static readonly object sync = new object();
		private static readonly Dictionary<string, TTFM_Context> contexts = new Dictionary<string, TTFM_Context>();

		public static void Publish(string key, TTFM_Context context)
		{
			if (string.IsNullOrWhiteSpace(key) || context == null)
				return;

			lock (sync)
				contexts[key] = Clone(context);
		}

		public static bool TryGet(string key, out TTFM_Context context)
		{
			context = null;
			if (string.IsNullOrWhiteSpace(key))
				return false;

			lock (sync)
			{
				TTFM_Context stored;
				if (!contexts.TryGetValue(key, out stored))
					return false;

				context = Clone(stored);
				return true;
			}
		}

		public static void Clear(string key)
		{
			if (string.IsNullOrWhiteSpace(key))
				return;

			lock (sync)
				contexts.Remove(key);
		}

		public static string[] GetKeys()
		{
			lock (sync)
			{
				string[] keys = new string[contexts.Keys.Count];
				contexts.Keys.CopyTo(keys, 0);
				return keys;
			}
		}

		private static TTFM_Context Clone(TTFM_Context source)
		{
			return new TTFM_Context
			{
				Key = source.Key,
				Instrument = source.Instrument,
				Profile = source.Profile,
				PublishedAt = source.PublishedAt,
				BarTime = source.BarTime,
				CurrentPrice = source.CurrentPrice,
				Bias = source.Bias,
				Confidence = source.Confidence,
				AllowedDirection = source.AllowedDirection,
				SetupStatus = source.SetupStatus,
				SetupPhase = source.SetupPhase,
				Location = source.Location,
				SetupId = source.SetupId,
				SetupAgeBars = source.SetupAgeBars,
				FailureReason = source.FailureReason,
				SequenceDirection = source.SequenceDirection,
				CurrentCandleNumber = source.CurrentCandleNumber,
				TSpotTouched = source.TSpotTouched,
				TSpotViolated = source.TSpotViolated,
				ProtectedSwingBroken = source.ProtectedSwingBroken,
				CisdEnabled = source.CisdEnabled,
				CisdConfirmed = source.CisdConfirmed,
				CisdMinutes = source.CisdMinutes,
				CisdDirection = source.CisdDirection,
				CisdAgeBars = source.CisdAgeBars,
				CisdLevel = source.CisdLevel,
				CisdTime = source.CisdTime,
				DistanceToCisdTicks = source.DistanceToCisdTicks,
				DistanceToTSpotTicks = source.DistanceToTSpotTicks,
				DistanceToProtectedTicks = source.DistanceToProtectedTicks,
				C1High = source.C1High,
				C1Low = source.C1Low,
				C1Open = source.C1Open,
				C1Close = source.C1Close,
				C1Time = source.C1Time,
				C2High = source.C2High,
				C2Low = source.C2Low,
				C2Open = source.C2Open,
				C2Close = source.C2Close,
				C2Time = source.C2Time,
				C3High = source.C3High,
				C3Low = source.C3Low,
				C3Open = source.C3Open,
				C3Close = source.C3Close,
				C3Eq = source.C3Eq,
				C3Time = source.C3Time,
				C4High = source.C4High,
				C4Low = source.C4Low,
				C4Open = source.C4Open,
				C4Close = source.C4Close,
				C4Time = source.C4Time,
				TSpotUpper = source.TSpotUpper,
				TSpotLower = source.TSpotLower,
				ProtectedSwing = source.ProtectedSwing,
				InvalidationPrice = source.InvalidationPrice,
				NearestBullZoneTop = source.NearestBullZoneTop,
				NearestBullZoneBottom = source.NearestBullZoneBottom,
				NearestBullZoneCE = source.NearestBullZoneCE,
				NearestBearZoneTop = source.NearestBearZoneTop,
				NearestBearZoneBottom = source.NearestBearZoneBottom,
				NearestBearZoneCE = source.NearestBearZoneCE,
				RoiSource = source.RoiSource,
				RoiHtfMinutes = source.RoiHtfMinutes,
				RoiActiveBias = source.RoiActiveBias,
				Narrative = source.Narrative
			};
		}
	}
}
