using UnityEngine;
using Verse;

namespace TSA.ForceRaidDirection
{
    /// <summary>
    /// Balanced corner weaver:
    /// Given 4-dir inputs (L/R/T/B), push weight into the appropriate corner
    /// without over-accentuating. Works per adjacent pair (R+T, L+T, L+B, R+B).
    ///
    /// Heuristic (hard-coded):
    ///   Let A=horiz, B=vert, S=A+B, hi=max(A,B), lo=min(A,B), r=lo/hi (0..1).
    ///   Stage 1 (corner share): diag = lo + γ * (r^p) * hi, clamped to [0,S]
    ///       γ = 0.60f, p = 2.0f  → 50/50 => 80% corner; 75/25 => ~30% corner
    ///   Stage 2 (leftovers): keep a small fraction of the weaker, rest to stronger
    ///       weakLeft = ε * lo, strongLeft = (S - diag) - weakLeft  with ε = 0.20f
    ///
    /// Examples:
    ///   T50 R50 -> RT=80, T=10, R=10
    ///   T75 R25 -> T=65, RT=30, R=5
    /// Pure cardinals pass through unchanged.
    /// </summary>
    public static class CornerWeaver
    {
        // Hard-coded knobs (no UI):
        private const float Gamma = 0.60f;      // base diagonal pull at true diagonal
        private const float Power = 2.0f;       // how fast diagonal pull shrinks when imbalanced
        private const float KeepWeakFrac = 0.20f; // keep a small part of weaker cardinal

        public static void Weave(int L, int R, int T, int B, ref Weights8 outW)
        {
            outW.Zero();

            // Pure cardinal (or none) → pass through
            int positives = (L > 0 ? 1 : 0) + (R > 0 ? 1 : 0) + (T > 0 ? 1 : 0) + (B > 0 ? 1 : 0);
            if (positives <= 1)
            {
                outW.Left = L; outW.Right = R; outW.Top = T; outW.Bottom = B;
                outW.NormalizeTo100BiasMax();
                return;
            }

            // Cornerize helper with balanced leftovers and robust rounding
            void Cornerize(int horiz, int vert, out int diag, out int oHoriz, out int oVert)
            {
                float h = horiz, v = vert;
                bool vertIsStronger = v >= h;
                float hi = vertIsStronger ? v : h;
                float lo = vertIsStronger ? h : v;
                float S = h + v;

                if (S <= 0f)
                {
                    diag = 0; oHoriz = 0; oVert = 0; return;
                }

                // Stage 1: corner share
                float r = (hi > 0f) ? (lo / hi) : 0f;    // 0..1
                float diagF = lo + Gamma * Mathf.Pow(r, Power) * hi;
                if (diagF < 0f) diagF = 0f;
                if (diagF > S) diagF = S;

                // Stage 2: leftovers
                float remainderF = S - diagF;
                float weakKeepF = KeepWeakFrac * lo;
                if (weakKeepF < 0f) weakKeepF = 0f;
                if (weakKeepF > remainderF) weakKeepF = remainderF; // can't exceed remainder
                float strongKeepF = remainderF - weakKeepF;

                // Round with sum conservation (bias to corner)
                int iDiag = Mathf.RoundToInt(diagF);
                int iWeak = Mathf.RoundToInt(weakKeepF);
                int iStrong = Mathf.RoundToInt(strongKeepF);

                int sInt = (int)(h + v); // original integers sum
                int sum = iDiag + iWeak + iStrong;
                int delta = sInt - sum;

                if (delta != 0)
                {
                    // Prefer adjusting the corner share to keep totals exact
                    int newDiag = iDiag + delta;
                    if (newDiag < 0)
                    {
                        // Borrow from strong, then weak if necessary
                        int need = -newDiag;
                        int takeFromStrong = Mathf.Min(iStrong, need);
                        iStrong -= takeFromStrong; need -= takeFromStrong;
                        int takeFromWeak = Mathf.Min(iWeak, need);
                        iWeak -= takeFromWeak; need -= takeFromWeak;
                        newDiag = 0;
                        // If still need > 0 (very rare), do nothing further; total may be off by 1
                    }
                    iDiag = newDiag;
                }

                // Map back to axes
                if (vertIsStronger) { oHoriz = iWeak; oVert = iStrong; }
                else { oHoriz = iStrong; oVert = iWeak; }

                diag = iDiag;
            }

            // Only one adjacent pair should be positive from the 4-dir split
            if (R > 0 && T > 0)
            {
                Cornerize(R, T, out int d, out int oR, out int oT);
                outW.RightTop = d; outW.Right = oR; outW.Top = oT;
            }
            else if (L > 0 && T > 0)
            {
                Cornerize(L, T, out int d, out int oL, out int oT);
                outW.LeftTop = d; outW.Left = oL; outW.Top = oT;
            }
            else if (L > 0 && B > 0)
            {
                Cornerize(L, B, out int d, out int oL, out int oB);
                outW.LeftBottom = d; outW.Left = oL; outW.Bottom = oB;
            }
            else if (R > 0 && B > 0)
            {
                Cornerize(R, B, out int d, out int oR, out int oB);
                outW.RightBottom = d; outW.Right = oR; outW.Bottom = oB;
            }
            else
            {
                // Unexpected mixed case: pass-through
                outW.Left = L; outW.Right = R; outW.Top = T; outW.Bottom = B;
            }

            outW.NormalizeTo100BiasMax();
        }
    }
}
