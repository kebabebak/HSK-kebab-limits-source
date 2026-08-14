using Multiplayer.API;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Registers Harmony sync workers with RimWorld Multiplayer when that mod is active.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class KebabLimitsMultiplayerCompat
    {
        /// <summary>Calls MP.RegisterAll so limit profiles and eject actions stay in sync in MP games.</summary>
        static KebabLimitsMultiplayerCompat()
        {
            if (MP.enabled)
            {
                MP.RegisterAll();
                KebabLimitsLog.Message("[kebabebak|HSKKebabLimits] Multiplayer compat enabled!");
            }
        }
    }
}
