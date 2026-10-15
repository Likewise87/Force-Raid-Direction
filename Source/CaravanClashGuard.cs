using Verse;

namespace TSA.ForceRaidDirection
{
    /// <summary>
    /// Skip edge steering on World Domination traveler interception maps (WD sets spawn; component may be present before/during raid).
    /// Uses reflection so this mod does not need a compile reference to TSA World Domination.
    /// </summary>
    public static class CaravanClashGuard
    {
        private const string WdComponentTypeName = "WD_MapComponent_CaravanClash";
        private const string SkipPropertyName = "ShouldSkipExternalRaidSteering";

        public static bool ShouldSkipSteering(Map map)
        {
            if (map?.components == null) return false;

            foreach (var comp in map.components)
            {
                if (comp == null) continue;
                if (comp.GetType().Name != WdComponentTypeName) continue;

                var pi = comp.GetType().GetProperty(SkipPropertyName);
                if (pi?.PropertyType == typeof(bool) && (bool)pi.GetValue(comp))
                    return true;
            }

            return false;
        }
    }
}
