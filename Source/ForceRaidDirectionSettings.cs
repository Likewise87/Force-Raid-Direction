using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace TSA.ForceRaidDirection
{
    public class ForceRaidDirectionSettings : ModSettings
    {
        // Global default weights (8 directions)
        public int WeightLeft = 50;
        public int WeightRight = 50;
        public int WeightTop = 50;
        public int WeightBottom = 50;
        public int WeightLeftTop = 50;
        public int WeightRightTop = 50;
        public int WeightLeftBottom = 50;
        public int WeightRightBottom = 50;

        // Band geometry
        public int EdgeBandWidth = 8;
        public string EdgeBandWidthBuffer = "8";

        // Segmented mode
        public bool SegmentEdgesIntoCenterAndCorners = false;
        public float SegmentCenterFraction = 0.5f; // 50% center → 25% to each corner

        // Arrival coercions
        public bool CoerceCenterDropToEdgeDrop = true;
        public bool CoerceMultiSideToSingleEdge = true;

        // NEW: convert StageThenAttack → ImmediateAttack
        public bool ConvertStageThenAttackToImmediate = false;

        // NEW: apply steering to Manhunter packs
        public bool ApplyToManhunters = true;

        // Debug
        public bool DebugLogging = true;

        // Per-faction rules (per savegame)
        public Dictionary<string, FactionEdgeRule> RulesByFactionId = new Dictionary<string, FactionEdgeRule>();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref WeightLeft, nameof(WeightLeft), 100);
            Scribe_Values.Look(ref WeightRight, nameof(WeightRight), 0);
            Scribe_Values.Look(ref WeightTop, nameof(WeightTop), 0);
            Scribe_Values.Look(ref WeightBottom, nameof(WeightBottom), 0);
            Scribe_Values.Look(ref WeightLeftTop, nameof(WeightLeftTop), 0);
            Scribe_Values.Look(ref WeightRightTop, nameof(WeightRightTop), 0);
            Scribe_Values.Look(ref WeightLeftBottom, nameof(WeightLeftBottom), 0);
            Scribe_Values.Look(ref WeightRightBottom, nameof(WeightRightBottom), 0);

            Scribe_Values.Look(ref EdgeBandWidth, nameof(EdgeBandWidth), 8);
            Scribe_Values.Look(ref EdgeBandWidthBuffer, nameof(EdgeBandWidthBuffer), "8");

            Scribe_Values.Look(ref SegmentEdgesIntoCenterAndCorners, nameof(SegmentEdgesIntoCenterAndCorners), false);
            Scribe_Values.Look(ref SegmentCenterFraction, nameof(SegmentCenterFraction), 0.5f);

            Scribe_Values.Look(ref CoerceCenterDropToEdgeDrop, nameof(CoerceCenterDropToEdgeDrop), true);
            Scribe_Values.Look(ref CoerceMultiSideToSingleEdge, nameof(CoerceMultiSideToSingleEdge), true);

            // NEW
            Scribe_Values.Look(ref ConvertStageThenAttackToImmediate, nameof(ConvertStageThenAttackToImmediate), false);
            Scribe_Values.Look(ref ApplyToManhunters, nameof(ApplyToManhunters), true);

            Scribe_Values.Look(ref DebugLogging, nameof(DebugLogging), true);

            Scribe_Collections.Look(ref RulesByFactionId, nameof(RulesByFactionId), LookMode.Value, LookMode.Deep);
            if (RulesByFactionId == null)
                RulesByFactionId = new Dictionary<string, FactionEdgeRule>();

            ClampAll();

            if (string.IsNullOrEmpty(EdgeBandWidthBuffer))
                EdgeBandWidthBuffer = EdgeBandWidth.ToString();
        }

        private void ClampAll()
        {
            EdgeBandWidth = Mathf.Clamp(EdgeBandWidth, 2, 24);
            SegmentCenterFraction = Mathf.Clamp01(SegmentCenterFraction);

            WeightLeft = Mathf.Clamp(WeightLeft, 0, 100);
            WeightRight = Mathf.Clamp(WeightRight, 0, 100);
            WeightTop = Mathf.Clamp(WeightTop, 0, 100);
            WeightBottom = Mathf.Clamp(WeightBottom, 0, 100);
            WeightLeftTop = Mathf.Clamp(WeightLeftTop, 0, 100);
            WeightRightTop = Mathf.Clamp(WeightRightTop, 0, 100);
            WeightLeftBottom = Mathf.Clamp(WeightLeftBottom, 0, 100);
            WeightRightBottom = Mathf.Clamp(WeightRightBottom, 0, 100);
        }

        /// <summary>
        /// Returns effective weights for a faction (if a rule exists and is enabled), otherwise global defaults.
        /// </summary>
        public void GetWeightsFor(Faction faction,
            out int left, out int right, out int top, out int bottom,
            out int leftTop, out int rightTop, out int leftBottom, out int rightBottom)
        {
            if (faction != null)
            {
                string key = faction.GetUniqueLoadID();
                if (!string.IsNullOrEmpty(key)
                    && RulesByFactionId != null
                    && RulesByFactionId.TryGetValue(key, out var rule)
                    && rule != null
                    && rule.Enabled)
                {
                    rule.EnsureAnyPositive();
                    left = rule.Left; right = rule.Right; top = rule.Top; bottom = rule.Bottom;
                    leftTop = rule.LeftTop; rightTop = rule.RightTop; leftBottom = rule.LeftBottom; rightBottom = rule.RightBottom;
                    return;
                }
            }

            left = WeightLeft; right = WeightRight; top = WeightTop; bottom = WeightBottom;
            leftTop = WeightLeftTop; rightTop = WeightRightTop; leftBottom = WeightLeftBottom; rightBottom = WeightRightBottom;

            if (left + right + top + bottom + leftTop + rightTop + leftBottom + rightBottom == 0)
                left = 1; // prevent degenerate zero-sum
        }
    }

    public class FactionEdgeRule : IExposable
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
        public int LeftTop;
        public int RightTop;
        public int LeftBottom;
        public int RightBottom;
        public bool Enabled;

        public FactionEdgeRule() { }

        public FactionEdgeRule(int left, int right, int top, int bottom,
                               int leftTop, int rightTop, int leftBottom, int rightBottom, bool enabled)
        {
            Left = left; Right = right; Top = top; Bottom = bottom;
            LeftTop = leftTop; RightTop = rightTop; LeftBottom = leftBottom; RightBottom = rightBottom;
            Enabled = enabled;
        }

        public void EnsureAnyPositive()
        {
            if (Left + Right + Top + Bottom + LeftTop + RightTop + LeftBottom + RightBottom == 0)
                Left = 1;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Left, nameof(Left), 100);
            Scribe_Values.Look(ref Right, nameof(Right), 0);
            Scribe_Values.Look(ref Top, nameof(Top), 0);
            Scribe_Values.Look(ref Bottom, nameof(Bottom), 0);
            Scribe_Values.Look(ref LeftTop, nameof(LeftTop), 0);
            Scribe_Values.Look(ref RightTop, nameof(RightTop), 0);
            Scribe_Values.Look(ref LeftBottom, nameof(LeftBottom), 0);
            Scribe_Values.Look(ref RightBottom, nameof(RightBottom), 0);
            Scribe_Values.Look(ref Enabled, nameof(Enabled), false);
        }
    }
}
