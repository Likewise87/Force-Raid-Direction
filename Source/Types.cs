using Verse;

namespace TSA.ForceRaidDirection
{
    /// <summary>
    /// Map-relative spawn edges and corners.
    /// Left=West, Right=East, Top=North, Bottom=South.
    /// </summary>
    public enum EdgeDir
    {
        Left,       // W
        Right,      // E
        Top,        // N
        Bottom,     // S
        LeftTop,    // NW
        RightTop,   // NE
        LeftBottom, // SW
        RightBottom // SE
    }

    /// <summary>
    /// Lightweight container for 8-direction weights (runtime only).
    /// Used by WorldDirectionResolver → SpawnEdgeUtility shim. Not saved.
    /// </summary>
    public class Weights8
    {
        public int Left, Right, Top, Bottom, LeftTop, RightTop, LeftBottom, RightBottom;

        public void Zero()
        {
            Left = Right = Top = Bottom = LeftTop = RightTop = LeftBottom = RightBottom = 0;
        }

        public int Sum() =>
            Left + Right + Top + Bottom + LeftTop + RightTop + LeftBottom + RightBottom;

        public void DistributeEqually()
        {
            Left = Right = Top = Bottom = LeftTop = RightTop = LeftBottom = RightBottom = 12; // 96 total
            RightTop += 4; // bias remainder to a stable slot → 100
        }

        public void NormalizeTo100BiasMax()
        {
            int total = Sum();
            if (total == 100) return;
            int[] a = { Left, Right, Top, Bottom, LeftTop, RightTop, LeftBottom, RightBottom };
            int idx = 0; for (int i = 1; i < a.Length; i++) if (a[i] > a[idx]) idx = i;
            a[idx] += (100 - total);
            Left = a[0]; Right = a[1]; Top = a[2]; Bottom = a[3];
            LeftTop = a[4]; RightTop = a[5]; LeftBottom = a[6]; RightBottom = a[7];
        }
    }
}
