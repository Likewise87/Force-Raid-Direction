using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA.ForceRaidDirection
{
    /// <summary>
    /// Computes world-based approach direction without any corner logic.
    /// This is your original 4-direction ENU/tangent-plane method (unchanged),
    /// split out into its own file. 8-dir is built from this via CornerWeaver.
    /// </summary>
    public static class WorldDirectionResolver
    {
        public static bool DebugEnabled => Mod_ForceRaidDirection.Settings?.DebugLogging == true;

        public static void LogDebug(string msg)
        {
            if (DebugEnabled) Log.Message("[TSA.ForceRaidDirection] " + msg);
        }

        /// <summary>
        /// ORIGINAL 4-DIR METHOD: returns L/R/T/B percentages summing to 100.
        /// Matches the legacy SpawnEdgeUtility.cs you provided:
        /// - Up   = colony radial (tile center normal)
        /// - North= project GLOBAL +Y onto tangent plane (fallback +Z)
        /// - East = North × Up  (note the order!)
        /// - Project chord to tangent, split into E/W & N/S, normalize
        /// - Flip mapping so Right=west, Left=east (as in your working mod)
        /// </summary>
        public static bool TryCompute4DirFromNearestSettlement(
            Map map, Faction faction,
            out int weightLeft, out int weightRight, out int weightTop, out int weightBottom)
        {
            weightLeft = weightRight = weightTop = weightBottom = 0;

            if (map == null || faction == null)
                return false;

            var settlements = Find.WorldObjects.Settlements
                .Where(s => s != null && s.Faction == faction)
                .ToList();
            if (settlements.Count == 0)
                return false;

            int mapTile = map.Tile;
            var grid = Find.WorldGrid;

            // Nearest settlement (tile distance)
            Settlement nearest = null;
            float bestDist = float.MaxValue;
            foreach (var s in settlements)
            {
                float dist = grid.ApproxDistanceInTiles(mapTile, s.Tile);
                if (dist < bestDist) { bestDist = dist; nearest = s; }
            }
            if (nearest == null)
                return false;

            // Globe-space vectors
            Vector3 from = grid.GetTileCenter(mapTile).normalized;
            Vector3 to = grid.GetTileCenter(nearest.Tile).normalized;

            // Tangent frame at colony
            Vector3 up = from.normalized;

            // North = project GLOBAL +Y onto tangent plane (fallback +Z)
            Vector3 globalNorth = new Vector3(0f, 1f, 0f);
            Vector3 northT = globalNorth - Vector3.Dot(globalNorth, up) * up;
            if (northT.sqrMagnitude < 1e-8f)
            {
                var alt = new Vector3(0f, 0f, 1f);
                northT = alt - Vector3.Dot(alt, up) * up;
            }
            northT = northT.normalized;

            // East = North × Up  (RIGHT-HANDED) — NOTE ORDER!
            Vector3 eastT = Vector3.Cross(northT, up).normalized;

            // Project chord onto tangent plane
            Vector3 chord = (to - from);
            if (chord.sqrMagnitude < 1e-8f)
            {
                weightLeft = 100; // deterministic fallback
                LogDebug("4dir: identical tile → L100");
                return true;
            }
            Vector3 tangentDir = chord - Vector3.Dot(chord, up) * up;
            if (tangentDir.sqrMagnitude < 1e-8f)
            {
                weightLeft = 100;
                LogDebug("4dir: degenerate tangent → L100");
                return true;
            }
            tangentDir.Normalize();

            // Components in local basis
            float eComp = Vector3.Dot(tangentDir, eastT);   // + east,  - west
            float nComp = Vector3.Dot(tangentDir, northT);  // + north, - south

            // Half-wave split (quadrant clamp)
            float east = Mathf.Max(0f, eComp);
            float west = Mathf.Max(0f, -eComp);
            float north = Mathf.Max(0f, nComp);
            float south = Mathf.Max(0f, -nComp);

            float sum = east + west + north + south;
            if (sum <= 1e-6f)
            {
                weightTop = 100;
                LogDebug("4dir: zero sum → T100");
                return true;
            }

            east /= sum;
            west /= sum;
            north /= sum;
            south /= sum;

            // Flip mapping EXACTLY like your working version:
            // Right = west, Left = east
            weightRight = Mathf.RoundToInt(west * 100f);
            weightLeft = Mathf.RoundToInt(east * 100f);
            weightTop = Mathf.RoundToInt(north * 100f);
            weightBottom = Mathf.RoundToInt(south * 100f);

            // Fix rounding drift (bias largest)
            int total = weightTop + weightBottom + weightRight + weightLeft;
            if (total != 100)
            {
                int[] vals = { weightTop, weightBottom, weightRight, weightLeft };
                int idx = 0; for (int i = 1; i < 4; i++) if (vals[i] > vals[idx]) idx = i;
                vals[idx] += (100 - total);
                weightTop = vals[0]; weightBottom = vals[1]; weightRight = vals[2]; weightLeft = vals[3];
            }

            if (DebugEnabled)
            {
                LogDebug($"4dir result (nearest='{nearest?.LabelCap ?? "Unknown"}') → L{weightLeft} R{weightRight} T{weightTop} B{weightBottom}");
            }

            return true;
        }

        /// <summary>
        /// Convenience: build 8-dir by running original 4-dir then the corner weaver.
        /// </summary>
        public static bool TryCompute8DirFromNearestSettlement(Map map, Faction faction, out Weights8 w)
        {
            w = new Weights8();
            if (!TryCompute4DirFromNearestSettlement(map, faction, out int L, out int R, out int T, out int B))
                return false;

            CornerWeaver.Weave(L, R, T, B, ref w);
            return true;
        }
    }
}
