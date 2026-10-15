using System;
using Verse;

namespace TSA.ForceRaidDirection
{
    /// <summary>
    /// Heuristics to detect "space-like" maps (Odyssee/asteroid/space biomes or ship maps).
    /// If true, we should not coerce arrival modes or steer spawnCenter; let vanilla/mod handle.
    /// </summary>
    public static class SpaceMapGuard
    {
        public static bool IsSpaceLike(Map map)
        {
            if (map == null) return false;

            // 1) Many space mods use special biomes. Check common keywords.
            string biome = map.Biome?.defName ?? string.Empty;
            if (biome.IndexOf("space", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (biome.IndexOf("asteroid", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (biome.IndexOf("vacuum", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            // 2) Map parent (site/ship/worldobject) often signals space contexts.
            string parent = map.Parent?.def?.defName ?? string.Empty;
            if (parent.IndexOf("Space", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (parent.IndexOf("Asteroid", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (parent.IndexOf("Odyssey", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (parent.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (parent.IndexOf("Grav", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            // 3) Some space maps don’t have a valid world tile.
            if (map.Tile < 0) return true;

            return false;
        }
    }
}
