#region Using declarations
using System.Collections.Generic;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class ICT_Setup_Engine
	{
		private readonly List<IICTSetup> setups = new List<IICTSetup>();

		public ICT_Setup_Engine(bool useICT2022, bool useIfvgCisd, bool useBreaker, bool useUnicorn, bool useOte, bool useObCisd, bool useTurtleSoup)
		{
			if (useICT2022) setups.Add(new ICT_2022_Setup());
			if (useIfvgCisd) setups.Add(new ICT_IFVG_CISD_Setup());
			if (useBreaker) setups.Add(new ICT_Breaker_Setup());
			if (useUnicorn) setups.Add(new ICT_Unicorn_Setup());
			if (useOte) setups.Add(new ICT_OTE_Setup());
			if (useObCisd) setups.Add(new ICT_OB_CISD_Setup());
			if (useTurtleSoup) setups.Add(new ICT_TurtleSoup_Setup());
		}

		public List<ICTSetupSignal> Evaluate(ICTMarketContext context)
		{
			List<ICTSetupSignal> signals = new List<ICTSetupSignal>();
			foreach (IICTSetup setup in setups)
				signals.Add(setup.Evaluate(context));
			return signals;
		}
	}
}
