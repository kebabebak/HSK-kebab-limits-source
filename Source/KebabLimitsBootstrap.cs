using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Runs once when the mod assembly loads; confirms HSK kebab limits is present in Player.log (if logging enabled).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class KebabLimitsBootstrap
    {
        /// <summary>Logs assembly load after RimWorld startup static constructors run.</summary>
        static KebabLimitsBootstrap()
        {
            KebabLimitsLog.Message("[HSK kebab limits] Assembly loaded.");
        }
    }
}
