using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA.ForceRaidDirection
{
    public static class SpawnBandUtility
    {
        public static bool DebugEnabled => Mod_ForceRaidDirection.Settings?.DebugLogging == true;

        public static void LogDebug(string msg)
        {
            if (DebugEnabled) Log.Message("[TSA.ForceRaidDirection] " + msg);
        }

        public static CellRect GetBandRect(Map map, EdgeDir dir, int bandWidth)
        {
            if (map == null) return CellRect.Empty;
            var rect = CellRect.WholeMap(map);
            if (rect.Area <= 0) return CellRect.Empty;

            int w = Mathf.Clamp(bandWidth, 1, Mathf.Max(rect.Width, rect.Height));

            var s = Mod_ForceRaidDirection.Settings;
            bool segmented = s != null && s.SegmentEdgesIntoCenterAndCorners;
            float centerFrac = Mathf.Clamp01(s?.SegmentCenterFraction ?? 0.5f);
            float cornerFrac = Mathf.Clamp01((1f - centerFrac) * 0.5f);

            if (!segmented)
            {
                switch (dir)
                {
                    case EdgeDir.Left: return new CellRect(rect.minX, rect.minZ, w, rect.Height);
                    case EdgeDir.Right: return new CellRect(rect.maxX - (w - 1), rect.minZ, w, rect.Height);
                    case EdgeDir.Top: return new CellRect(rect.minX, rect.maxZ - (w - 1), rect.Width, w);
                    case EdgeDir.Bottom: return new CellRect(rect.minX, rect.minZ, rect.Width, w);
                    case EdgeDir.LeftTop: return new CellRect(rect.minX, rect.maxZ - (w - 1), w, w);
                    case EdgeDir.RightTop: return new CellRect(rect.maxX - (w - 1), rect.maxZ - (w - 1), w, w);
                    case EdgeDir.LeftBottom: return new CellRect(rect.minX, rect.minZ, w, w);
                    case EdgeDir.RightBottom: return new CellRect(rect.maxX - (w - 1), rect.minZ, w, w);
                    default: return CellRect.Empty;
                }
            }

            // segmented mode
            int fullH = rect.Height;
            int fullW = rect.Width;

            void SplitV(out int zBotStart, out int zMidStart, out int zMidEnd, out int zTopStart)
            {
                int topH = Mathf.RoundToInt(fullH * cornerFrac);
                int midH = Mathf.RoundToInt(fullH * centerFrac);
                int botH = fullH - topH - midH;

                zBotStart = rect.minZ;
                int zBotEnd = zBotStart + botH - 1;
                zMidStart = zBotEnd + 1;
                zMidEnd = zMidStart + midH - 1;
                zTopStart = zMidEnd + 1;
            }

            void SplitH(out int xLeftStart, out int xMidStart, out int xMidEnd, out int xRightStart)
            {
                int leftW = Mathf.RoundToInt(fullW * cornerFrac);
                int midW = Mathf.RoundToInt(fullW * centerFrac);
                int rgtW = fullW - leftW - midW;

                xLeftStart = rect.minX;
                int xLeftEnd = xLeftStart + leftW - 1;
                xMidStart = xLeftEnd + 1;
                xMidEnd = xMidStart + midW - 1;
                xRightStart = xMidEnd + 1;
            }

            switch (dir)
            {
                // vertical edges center
                case EdgeDir.Left:
                    {
                        SplitV(out _, out int zMidStart, out int zMidEnd, out _);
                        return new CellRect(rect.minX, zMidStart, w, zMidEnd - zMidStart + 1);
                    }
                case EdgeDir.Right:
                    {
                        SplitV(out _, out int zMidStart, out int zMidEnd, out _);
                        return new CellRect(rect.maxX - (w - 1), zMidStart, w, zMidEnd - zMidStart + 1);
                    }

                // horizontal edges center
                case EdgeDir.Top:
                    {
                        SplitH(out _, out int xMidStart, out int xMidEnd, out _);
                        return new CellRect(xMidStart, rect.maxZ - (w - 1), xMidEnd - xMidStart + 1, w);
                    }
                case EdgeDir.Bottom:
                    {
                        SplitH(out _, out int xMidStart, out int xMidEnd, out _);
                        return new CellRect(xMidStart, rect.minZ, xMidEnd - xMidStart + 1, w);
                    }

                // corners 50/50 vertical vs horizontal slice
                case EdgeDir.RightTop:
                    {
                        SplitV(out _, out _, out _, out int zTopStart);
                        int topH = Mathf.Max(1, rect.maxZ - zTopStart + 1);
                        SplitH(out _, out _, out _, out int xRightStart);
                        int rightW = Mathf.Max(1, rect.maxX - xRightStart + 1);
                        if (Rand.Bool) return new CellRect(rect.maxX - (w - 1), zTopStart, w, topH);
                        return new CellRect(xRightStart, rect.maxZ - (w - 1), rightW, w);
                    }
                case EdgeDir.RightBottom:
                    {
                        SplitV(out int zBotStart, out int zMidStart, out _, out _);
                        int botH = Mathf.Max(1, zMidStart - zBotStart);
                        SplitH(out _, out _, out _, out int xRightStart);
                        int rightW = Mathf.Max(1, rect.maxX - xRightStart + 1);
                        if (Rand.Bool) return new CellRect(rect.maxX - (w - 1), zBotStart, w, botH);
                        return new CellRect(xRightStart, rect.minZ, rightW, w);
                    }
                case EdgeDir.LeftTop:
                    {
                        SplitV(out _, out _, out _, out int zTopStart);
                        int topH = Mathf.Max(1, rect.maxZ - zTopStart + 1);
                        SplitH(out int xLeftStart, out int xMidStart, out _, out _);
                        int leftW = Mathf.Max(1, xMidStart - xLeftStart);
                        if (Rand.Bool) return new CellRect(rect.minX, zTopStart, w, topH);
                        return new CellRect(xLeftStart, rect.maxZ - (w - 1), leftW, w);
                    }
                case EdgeDir.LeftBottom:
                    {
                        SplitV(out int zBotStart, out int zMidStart, out _, out _);
                        int botH = Mathf.Max(1, zMidStart - zBotStart);
                        SplitH(out int xLeftStart, out int xMidStart, out _, out _);
                        int leftW = Mathf.Max(1, xMidStart - xLeftStart);
                        if (Rand.Bool) return new CellRect(rect.minX, zBotStart, w, botH);
                        return new CellRect(xLeftStart, rect.minZ, leftW, w);
                    }
                default: return CellRect.Empty;
            }
        }

        public static bool TryFindSpawnCell(Map map, EdgeDir dir, int bandWidth, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (map == null) return false;

            var band = GetBandRect(map, dir, bandWidth);
            if (band.Area <= 0) return false;

            for (int i = 0; i < 240; i++)
            {
                var c = band.RandomCell;
                if (c.InBounds(map) && !c.Fogged(map) && c.Standable(map))
                { cell = c; return true; }
            }
            for (int i = 0; i < 480; i++)
            {
                var c = band.RandomCell;
                if (c.InBounds(map) && !c.Fogged(map) && c.Walkable(map))
                { cell = c; return true; }
            }
            return false;
        }

        public static bool BandLooksUsable(Map map, EdgeDir dir, int bandWidth)
        {
            var band = GetBandRect(map, dir, bandWidth);
            if (band.Area <= 0) return false;

            int checks = Mathf.Min(220, band.Area);
            for (int i = 0; i < checks; i++)
            {
                var c = band.RandomCell;
                if (c.InBounds(map) && !c.Fogged(map) && c.Walkable(map))
                    return true;
            }
            return false;
        }

        public static EdgeDir ChooseDirByWeights(
            int left, int right, int top, int bottom,
            int leftTop, int rightTop, int leftBottom, int rightBottom,
            System.Random rng = null)
        {
            int[] w = { left, right, top, bottom, leftTop, rightTop, leftBottom, rightBottom };
            int total = w.Sum();
            if (total <= 0) return EdgeDir.Left;

            int roll = (rng ?? new System.Random()).Next(total);
            EdgeDir[] dirs = {
                EdgeDir.Left, EdgeDir.Right, EdgeDir.Top, EdgeDir.Bottom,
                EdgeDir.LeftTop, EdgeDir.RightTop, EdgeDir.LeftBottom, EdgeDir.RightBottom
            };

            for (int i = 0; i < w.Length; i++)
            {
                roll -= w[i];
                if (roll < 0) return dirs[i];
            }
            return EdgeDir.Left;
        }

        public static EdgeDir TryPickWeightedDir(Map map,
            int l, int r, int t, int b, int lt, int rt, int lb, int rb, int bandWidth)
        {
            var rng = new System.Random();
            for (int i = 0; i < 8; i++)
            {
                var e = ChooseDirByWeights(l, r, t, b, lt, rt, lb, rb, rng);
                if (BandLooksUsable(map, e, bandWidth))
                    return e;
            }
            return ChooseDirByWeights(l, r, t, b, lt, rt, lb, rb, rng);
        }

        public static bool TryPickBandCell(Map map, EdgeDir dir, int bandWidth, out IntVec3 cell)
            => TryFindSpawnCell(map, dir, bandWidth, out cell);

        public static Rot4 DesiredRotationFor(EdgeDir dir, Map map, IntVec3 spawnCell)
        {
            switch (dir)
            {
                case EdgeDir.Left: return Rot4.East;
                case EdgeDir.Right: return Rot4.West;
                case EdgeDir.Top: return Rot4.South;
                case EdgeDir.Bottom: return Rot4.North;
                case EdgeDir.LeftTop:
                case EdgeDir.RightTop:
                case EdgeDir.LeftBottom:
                case EdgeDir.RightBottom:
                    {
                        var toCenter = (map.Center - spawnCell).ToVector3Shifted();
                        float ang = toCenter.AngleFlat();
                        return Rot4.FromAngleFlat(ang);
                    }
                default:
                    return Rot4.Random;
            }
        }
    }
}
