using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA.ForceRaidDirection
{
    public class Mod_ForceRaidDirection : Mod
    {
        public static ForceRaidDirectionSettings Settings = null!;

        public Mod_ForceRaidDirection(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ForceRaidDirectionSettings>();
            new Harmony("TSA.ForceRaidDirection").PatchAll();
        }

        public override string SettingsCategory() => "Force Raid Direction";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (Settings == null)
            {
                Widgets.Label(inRect, "Settings not initialized.");
                return;
            }

            var ls = new Listing_Standard();
            ls.Begin(inRect);

            DrawGlobalSettings(ls);

            ls.GapLine();

            var row = ls.GetRect(32f);
            bool inGame = Current.Game != null && Find.CurrentMap != null;
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.Label(row.LeftPart(0.65f),
                    inGame
                        ? "Configure per-faction rules in a dedicated window:"
                        : "Per-faction rules are available in-game.");
            }

            if (Widgets.ButtonText(row.RightPart(0.35f), "Open Advanced Rules…"))
            {
                if (inGame)
                    Find.WindowStack.Add(new AdvancedRulesWindow(Settings));
                else
                    Messages.Message("Open a save (in-game) to edit per-faction rules.", MessageTypeDefOf.NeutralEvent, false);
            }

            ls.Gap();
            {
                Rect resetRect = ls.GetRect(28f);
                Color prev = GUI.color;
                GUI.color = new Color(1f, 0.5f, 0.5f);
                if (Widgets.ButtonText(resetRect, "Restore defaults"))
                {
                    RestoreDefaultsSafe();
                    Event.current?.Use();
                    GUI.color = prev;
                    ls.End();
                    return;
                }
                GUI.color = prev;
            }

            ls.End();
        }

        private void DrawGlobalSettings(Listing_Standard ls)
        {
            ls.Label("Default direction weights (used if no enabled per-faction rule applies):");

            float l = Settings.WeightLeft, r = Settings.WeightRight, t = Settings.WeightTop, b = Settings.WeightBottom;
            float lt = Settings.WeightLeftTop, rt = Settings.WeightRightTop, lb = Settings.WeightLeftBottom, rb = Settings.WeightRightBottom;
            float sum = Mathf.Max(0.0001f, l + r + t + b + lt + rt + lb + rb);

            DrawEdgeWeightInline(ls, "↑ Top", ref Settings.WeightTop, Percent(sum, t),
                "Default weight for TOP edge before feasibility filters.");
            DrawEdgeWeightInline(ls, "↓ Bottom", ref Settings.WeightBottom, Percent(sum, b),
                "Default weight for BOTTOM edge before feasibility filters.");
            DrawEdgeWeightInline(ls, "→ Right", ref Settings.WeightRight, Percent(sum, r),
                "Default weight for RIGHT edge before feasibility filters.");
            DrawEdgeWeightInline(ls, "← Left", ref Settings.WeightLeft, Percent(sum, l),
                "Default weight for LEFT edge before feasibility filters.");

            DrawEdgeWeightInline(ls, "↗ Right-Top", ref Settings.WeightRightTop, Percent(sum, rt),
                "Default weight for corner band (or segmented corner slice).");
            DrawEdgeWeightInline(ls, "↖ Left-Top", ref Settings.WeightLeftTop, Percent(sum, lt),
                "Default weight for corner band (or segmented corner slice).");
            DrawEdgeWeightInline(ls, "↘ Right-Bottom", ref Settings.WeightRightBottom, Percent(sum, rb),
                "Default weight for corner band (or segmented corner slice).");
            DrawEdgeWeightInline(ls, "↙ Left-Bottom", ref Settings.WeightLeftBottom, Percent(sum, lb),
                "Default weight for corner band (or segmented corner slice).");

            if (l + r + t + b + lt + rt + lb + rb == 0)
            {
                var warn = ls.GetRect(24f);
                Widgets.DrawBoxSolidWithOutline(warn, new Color(0.25f, 0f, 0f, 0.15f), Color.red);
                Widgets.Label(warn, "At least one direction must have a Weight > 0.");
            }

            // NEW: Distribute equally button
            if (Widgets.ButtonText(ls.GetRect(28f), "Distribute equally (8-dir)"))
            {
                SetAllWeightsEqual(Settings, 50);
                if (Settings.DebugLogging)
                    Log.Message("[TSA.ForceRaidDirection][Defaults] Distributed equally (8-dir) → 50");
            }

            ls.GapLine();

            {
                var spanRow = ls.GetRect(30f);
                var lab = spanRow.LeftPart(0.58f);
                var slider = spanRow.RightPart(0.42f);
                TooltipHandler.TipRegion(lab,
                    "Pecentage allocated to top, bottom, left, right. The rest goes to the corners.\n" +
                    "Example: Setting 50 % means that the middle 50 % of the right side go to ↑, the top 25 % of the right side go to ↗ and the bottom 25 % of the right side go to ↘.\n" +
                    "The same logic is applied to all sides.");
                string centerPct = $"{(int)Mathf.Round(Settings.SegmentCenterFraction * 100f)}%".Colorize(Color.cyan);
                Widgets.Label(lab, $"Center span: {centerPct}  (corners take the rest equally)");
                float cf = Settings.SegmentCenterFraction;
                cf = Widgets.HorizontalSlider(slider, cf, 0.1f, 0.9f, middleAlignment: false, label: "");
                if (Mathf.Abs(cf - Settings.SegmentCenterFraction) > 0.0001f)
                    Settings.SegmentCenterFraction = cf;
            }

            // Band width
            {
                var bandRow = ls.GetRect(28f);
                var lab = bandRow.LeftPart(0.58f);
                var fld = bandRow.RightPart(0.42f);
                TooltipHandler.TipRegion(lab,
                    "How far inward from the chosen side/corner raids may spawn.\n" +
                    "2 = right at the rim, 24 = further in.\n" +
                    "Default: 8 tiles. Range: 2–24.");
                string bandVal = Settings.EdgeBandWidth.ToString().Colorize(Color.cyan);
                Widgets.Label(lab, $"Band width (tiles): {bandVal}");
                Widgets.TextFieldNumeric(fld, ref Settings.EdgeBandWidth, ref Settings.EdgeBandWidthBuffer, 2, 24);
            }
            ls.Gap(6f);

            DrawCheckbox(ls, "Convert center-drop raids to edge/corner drop pods", ref Settings.CoerceCenterDropToEdgeDrop,
                "Pods that would normally drop in the center are instead dropped along a chosen side/corner (based on weights).");
            DrawCheckbox(ls, "Convert multi-side arrivals to a single direction", ref Settings.CoerceMultiSideToSingleEdge,
                "If the game would pick several sides, convert it into a single direction based on your weights.");

            // NEW: Stage→Immediate toggle
            DrawCheckbox(ls, "Convert “Stage, then attack” raids to “Attack immediately”",
                ref Settings.ConvertStageThenAttackToImmediate,
                "When enabled, StageThenAttack raids become ImmediateAttack (or Smart if the faction supports avoid-grid AI). Unset strategies are left for vanilla to resolve.");

            // NEW: Apply to Manhunter packs
            DrawCheckbox(ls, "Apply to Manhunter packs",
                ref Settings.ApplyToManhunters,
                "When enabled, manhunter pack incidents will use the same forced entry direction logic.");

            ls.GapLine();

            DrawCheckbox(ls, "Enable debug logging", ref Settings.DebugLogging,
                "Logs arrival method/strategy, band feasibility, normalized weights, chosen direction and spawn center.");
        }

        private void DrawEdgeWeightInline(Listing_Standard ls, string label, ref int value, string normalizedPct, string tooltip)
        {
            ls.Gap(2f);
            var row = ls.GetRect(24f);
            var left = row.LeftPart(0.55f);
            var right = row.RightPart(0.45f);

            TooltipHandler.TipRegion(left, tooltip);
            string cyanVal = $"{value} ({normalizedPct})".Colorize(Color.cyan);
            Widgets.Label(left, $"{label}: {cyanVal}");

            float fVal = value;
            fVal = Widgets.HorizontalSlider(right, fVal, 0f, 100f, false, label: "");
            int iVal = Mathf.RoundToInt(fVal);
            if (iVal != value) value = iVal;
        }

        private static void DrawCheckbox(Listing_Standard ls, string label, ref bool value, string tooltip = null)
        {
            ls.Gap(2f);
            Rect r = ls.GetRect(24f);
            if (!tooltip.NullOrEmpty()) TooltipHandler.TipRegion(r, tooltip);
            Widgets.CheckboxLabeled(r, label, ref value);
        }

        private static string Percent(float sum, float part)
        {
            float p = sum <= 0f ? 0f : (part / sum) * 100f;
            return $"{p:0.#}%";
        }

        private static void RestoreDefaultsSafe()
        {
            if (Settings == null) return;

            Settings.WeightLeft = 50;
            Settings.WeightRight = 50;
            Settings.WeightTop = 50;
            Settings.WeightBottom = 50;
            Settings.WeightLeftTop = 50;
            Settings.WeightRightTop = 50;
            Settings.WeightLeftBottom = 50;
            Settings.WeightRightBottom = 50;

            Settings.EdgeBandWidth = 8;
            Settings.EdgeBandWidthBuffer = "8";

            Settings.SegmentCenterFraction = 0.5f;

            Settings.CoerceCenterDropToEdgeDrop = true;
            Settings.CoerceMultiSideToSingleEdge = true;

            // NEW default
            Settings.ConvertStageThenAttackToImmediate = false;
            Settings.ApplyToManhunters = true;

            Settings.DebugLogging = true;

            Settings.RulesByFactionId = new Dictionary<string, FactionEdgeRule>();
            Settings.Write();
        }

        // NEW helper
        private static void SetAllWeightsEqual(ForceRaidDirectionSettings s, int value = 50)
        {
            value = Mathf.Clamp(value, 0, 100);
            s.WeightLeft = s.WeightRight = s.WeightTop = s.WeightBottom =
            s.WeightLeftTop = s.WeightRightTop = s.WeightLeftBottom = s.WeightRightBottom = value;
        }

        private readonly struct TextBlock : System.IDisposable
        {
            private readonly TextAnchor _old;
            public TextBlock(TextAnchor anchor) { _old = Text.Anchor; Text.Anchor = anchor; }
            public void Dispose() => Text.Anchor = _old;
        }
    }
}
