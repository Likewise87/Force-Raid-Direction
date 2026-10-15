using RimWorld;
using UnityEngine;
using Verse;

namespace TSA.ForceRaidDirection
{
    /// <summary>
    /// Compatibility shim so the original UI & patch code can call the old
    /// SpawnEdgeUtility API while the core logic lives in the split modules
    /// (WorldDirectionResolver, SpawnBandUtility).
    ///
    /// IMPORTANT:
    /// - This file intentionally does NOT declare EdgeDir (keep your Types.cs).
    /// - UI/patch can remain exactly as in your "perfect UI" version.
    /// </summary>
    public static class SpawnEdgeUtility
    {
        public static bool DebugEnabled => Mod_ForceRaidDirection.Settings?.DebugLogging == true;

        public static void LogDebug(string msg)
        {
            if (DebugEnabled)
                Log.Message("[TSA.ForceRaidDirection] " + msg);
        }

        /// <summary>
        /// Derive 8-direction weights from nearest settlement.
        /// Delegates to the current split core:
        /// WorldDirectionResolver.TryCompute8DirFromNearestSettlement → Weights8.
        /// </summary>
        public static bool TryComputeDirectionalWeightsFromNearestSettlement8(
            Map map, Faction faction,
            out int wLeft, out int wRight, out int wTop, out int wBottom,
            out int wLeftTop, out int wRightTop, out int wLeftBottom, out int wRightBottom)
        {
            wLeft = wRight = wTop = wBottom = wLeftTop = wRightTop = wLeftBottom = wRightBottom = 0;

            if (map == null || faction == null)
                return false;

            if (WorldDirectionResolver.TryCompute8DirFromNearestSettlement(map, faction, out var w))
            {
                wLeft = w.Left;
                wRight = w.Right;
                wTop = w.Top;
                wBottom = w.Bottom;
                wLeftTop = w.LeftTop;
                wRightTop = w.RightTop;
                wLeftBottom = w.LeftBottom;
                wRightBottom = w.RightBottom;
                return true;
            }

            // deterministic fallback
            wLeft = 100;
            return true;
        }

        // ---------- spawn band helpers (delegate to SpawnBandUtility) ----------

        public static CellRect GetBandRect(Map map, EdgeDir dir, int bandWidth)
            => SpawnBandUtility.GetBandRect(map, dir, bandWidth);

        public static bool TryFindSpawnCell(Map map, EdgeDir dir, int bandWidth, out IntVec3 cell)
            => SpawnBandUtility.TryFindSpawnCell(map, dir, bandWidth, out cell);

        public static bool BandLooksUsable(Map map, EdgeDir dir, int bandWidth)
            => SpawnBandUtility.BandLooksUsable(map, dir, bandWidth);

        public static EdgeDir ChooseDirByWeights(
            int left, int right, int top, int bottom,
            int leftTop, int rightTop, int leftBottom, int rightBottom,
            System.Random rng = null)
            => SpawnBandUtility.ChooseDirByWeights(left, right, top, bottom, leftTop, rightTop, leftBottom, rightBottom, rng);

        public static EdgeDir TryPickWeightedDir(Map map,
            int l, int r, int t, int b, int lt, int rt, int lb, int rb, int bandWidth)
            => SpawnBandUtility.TryPickWeightedDir(map, l, r, t, b, lt, rt, lb, rb, bandWidth);

        public static bool TryPickBandCell(Map map, EdgeDir dir, int bandWidth, out IntVec3 cell)
            => SpawnBandUtility.TryPickBandCell(map, dir, bandWidth, out cell);

        public static Rot4 DesiredRotationFor(EdgeDir dir, Map map, IntVec3 spawnCell)
            => SpawnBandUtility.DesiredRotationFor(dir, map, spawnCell);
    }
}
