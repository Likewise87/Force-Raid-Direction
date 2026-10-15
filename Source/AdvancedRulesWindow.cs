using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA.ForceRaidDirection
{
    public class AdvancedRulesWindow : Window
    {
        private readonly ForceRaidDirectionSettings _settings;
        private Vector2 _scroll;
        private List<Faction> _factions;

        public override Vector2 InitialSize => new Vector2(900f, 720f);

        public AdvancedRulesWindow(ForceRaidDirectionSettings settings)
        {
            _settings = settings;

            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            draggable = true;

            closeOnClickedOutside = false;
            onlyOneOfTypeAllowed = true;
            doWindowBackground = true;

            SetupFactionList();
        }

        private void SetupFactionList()
        {
            _settings.RulesByFactionId ??= new Dictionary<string, FactionEdgeRule>();

            _factions = Find.FactionManager.AllFactionsListForReading
                .Where(f => f != null && !f.IsPlayer && !f.def.hidden)
                .OrderBy(f => f.def.LabelCap.Resolve())
                .ThenBy(f => f.Name)
                .ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "Advanced Faction Rules");
            Text.Font = GameFont.Small;

            Rect body = new Rect(inRect.x, inRect.y + 38f, inRect.width, inRect.height - 78f);
            Rect footer = new Rect(inRect.x, inRect.yMax - 36f, inRect.width, 32f);

            Widgets.DrawMenuSection(body);

            float y = body.y + 4f;
            const float rowH = 26f;
            const float pad = 4f;

            Rect header = new Rect(body.x + 6f, y, body.width - 12f, rowH);
            DrawHeader(header);
            y += rowH + pad;

            float contentH = pad;
            foreach (var fac in _factions)
            {
                var rule = GetOrCreateRule(fac);
                contentH += rowH + pad;                 // header row
                if (rule.Enabled) contentH += 8 * 28f;  // 8 sliders
                contentH += pad;
            }

            Rect outRect = new Rect(body.x + 4f, y, body.width - 8f, body.yMax - y - 4f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, contentH));
            Widgets.BeginScrollView(outRect, ref _scroll, viewRect);

            float cy = 0f;
            foreach (var fac in _factions)
            {
                var rule = GetOrCreateRule(fac);
                string label = $"{fac.Name} ({fac.def.LabelCap})";

                Rect row = new Rect(0, cy, viewRect.width, rowH);
                Widgets.DrawHighlightIfMouseover(row);

                Rect nameRect = row.LeftPart(0.42f);
                using (new TextBlock(TextAnchor.MiddleLeft))
                    Widgets.Label(nameRect, label);

                Rect enableColRect = new Rect(row.x + row.width * 0.42f, row.y, 90f, row.height);
                Rect iconRect = new Rect(
                    enableColRect.x + (enableColRect.width - 24f) * 0.5f,
                    enableColRect.y + (enableColRect.height - 24f) * 0.5f,
                    24f, 24f);

                Texture2D icon = rule.Enabled ? Widgets.CheckboxOnTex : TexButton.CloseXSmall;
                if (Widgets.ButtonImage(iconRect, icon, doMouseoverSound: true))
                {
                    rule.Enabled = !rule.Enabled;
                    if (_settings.DebugLogging)
                        Log.Message($"[TSA.ForceRaidDirection][UI] {(rule.Enabled ? "Enabled" : "Disabled")} rule for {fac.GetUniqueLoadID()} {fac.Name}");
                }
                TooltipHandler.TipRegion(iconRect, rule.Enabled ? "Enabled" : "Disabled");

                Rect fromRect = new Rect(enableColRect.xMax + 6f, row.y, 180f, row.height);
                if (Widgets.ButtonText(fromRect, "From village (8-dir)"))
                {
                    TryApplyFromVillage8(fac, rule);
                }
                TooltipHandler.TipRegion(fromRect, "Derives weights from the direction to the nearest settlement (8 directions, soft distribution).");

                // Distribute equally button
                Rect equalRect = new Rect(fromRect.xMax + 6f, row.y, 180f, row.height);
                if (Widgets.ButtonText(equalRect, "Distribute equally"))
                {
                    rule.Left = rule.Right = rule.Top = rule.Bottom =
                    rule.LeftTop = rule.RightTop = rule.LeftBottom = rule.RightBottom = 50;
                    rule.Enabled = true;
                    if (_settings.DebugLogging)
                        Log.Message($"[TSA.ForceRaidDirection][UI] {fac.GetUniqueLoadID()} {fac.Name}: distributed equally (8-dir) → 50");
                }
                TooltipHandler.TipRegion(equalRect, "Sets all 8 directional weights to the same value (50).");

                cy += rowH + pad;

                if (rule.Enabled)
                {
                    float l = rule.Left, r = rule.Right, t = rule.Top, b = rule.Bottom;
                    float lt = rule.LeftTop, rt = rule.RightTop, lb = rule.LeftBottom, rb = rule.RightBottom;
                    float sum = Mathf.Max(0.0001f, l + r + t + b + lt + rt + lb + rb);

                    cy = DrawEdgeSlider(viewRect, cy, "↑ Top", ref rule.Top, Percent(sum, t), fac.Name);
                    cy = DrawEdgeSlider(viewRect, cy, "↓ Bottom", ref rule.Bottom, Percent(sum, b), fac.Name);
                    cy = DrawEdgeSlider(viewRect, cy, "→ Right", ref rule.Right, Percent(sum, r), fac.Name);
                    cy = DrawEdgeSlider(viewRect, cy, "← Left", ref rule.Left, Percent(sum, l), fac.Name);

                    cy = DrawEdgeSlider(viewRect, cy, "↗ Right-Top", ref rule.RightTop, Percent(sum, rt), fac.Name);
                    cy = DrawEdgeSlider(viewRect, cy, "↖ Left-Top", ref rule.LeftTop, Percent(sum, lt), fac.Name);
                    cy = DrawEdgeSlider(viewRect, cy, "↘ Right-Bottom", ref rule.RightBottom, Percent(sum, rb), fac.Name);
                    cy = DrawEdgeSlider(viewRect, cy, "↙ Left-Bottom", ref rule.LeftBottom, Percent(sum, lb), fac.Name);

                    rule.EnsureAnyPositive();
                }

                _settings.RulesByFactionId[fac.GetUniqueLoadID()] = rule;
                cy += pad;
            }

            Widgets.EndScrollView();

            bool allEnabled = _factions.All(f => GetOrCreateRule(f).Enabled);
            string toggleLabel = allEnabled ? "Disable all" : "Enable all";

            Rect toggleRect = footer.LeftPartPixels(180f);
            if (Widgets.ButtonText(toggleRect, toggleLabel))
            {
                string confirmText = allEnabled
                    ? "Disable all faction rules?\n\nAre you sure?"
                    : "Enable all faction rules?\n\nAre you sure?";

                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmText, () =>
                {
                    foreach (var f in _factions)
                    {
                        var r = GetOrCreateRule(f);
                        r.Enabled = !allEnabled;
                        _settings.RulesByFactionId[f.GetUniqueLoadID()] = r;
                    }
                }, destructive: allEnabled));
            }

            Rect fromAllRect = new Rect(toggleRect.xMax + 8f, footer.y, 230f, footer.height);
            if (Widgets.ButtonText(fromAllRect, "From village for all (8-dir)"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Derive 8-directional weights from the nearest settlement for all factions?\n\nAre you sure?",
                    () =>
                    {
                        var map = Find.CurrentMap ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
                        foreach (var f in _factions)
                        {
                            var r = GetOrCreateRule(f);
                            TryApplyFromVillage8(f, r, map);
                            _settings.RulesByFactionId[f.GetUniqueLoadID()] = r;
                        }
                    }));
            }
            TooltipHandler.TipRegion(fromAllRect, "Derives 8-directional weights from the nearest settlement for each faction.");

            if (Widgets.ButtonText(footer.RightPartPixels(180f), "Close"))
            {
                Close();
            }
        }

        private static void DrawHeader(Rect header)
        {
            using (new TextBlock(TextAnchor.MiddleLeft))
                Widgets.Label(header.LeftPart(0.42f), "Faction");

            using (new TextBlock(TextAnchor.MiddleCenter))
                Widgets.Label(new Rect(header.x + header.width * 0.42f, header.y, 90f, header.height), "Enable?");

            using (new TextBlock(TextAnchor.MiddleCenter))
                Widgets.Label(new Rect(header.x + header.width * 0.42f + 96f, header.y, 180f, header.height), "From village");

            using (new TextBlock(TextAnchor.MiddleCenter))
                Widgets.Label(new Rect(header.x + header.width * 0.42f + 96f + 186f, header.y, 180f, header.height), "Distribute equally");
        }

        private float DrawEdgeSlider(Rect viewRect, float y, string label, ref int value, string pct, string who)
        {
            const float SliderH = 28f;

            Rect row = new Rect(0, y, viewRect.width, SliderH);
            Rect left = row.LeftPart(0.55f);
            Rect right = row.RightPart(0.45f);

            string cyanVal = $"{value} ({pct})".Colorize(Color.cyan);
            Widgets.Label(left, $"{label}: {cyanVal}");
            float f = value;
            f = Widgets.HorizontalSlider(right, f, 0f, 100f, false, "");
            int i = Mathf.RoundToInt(f);
            if (i != value)
            {
                value = i;
                if (_settings.DebugLogging)
                    Log.Message($"[TSA.ForceRaidDirection][UI] {who} {label} → {value}");
            }

            return y + SliderH;
        }

        private FactionEdgeRule GetOrCreateRule(Faction faction)
        {
            _settings.RulesByFactionId ??= new Dictionary<string, FactionEdgeRule>();
            string key = faction?.GetUniqueLoadID() ?? "";
            if (string.IsNullOrEmpty(key))
                return new FactionEdgeRule(100, 0, 0, 0, 0, 0, 0, 0, false);

            if (!_settings.RulesByFactionId.TryGetValue(key, out var rule) || rule == null)
            {
                rule = new FactionEdgeRule(100, 0, 0, 0, 0, 0, 0, 0, false);
                _settings.RulesByFactionId[key] = rule;
            }
            return rule;
        }

        private void TryApplyFromVillage8(Faction faction, FactionEdgeRule rule, Map mapOverride = null)
        {
            var map = mapOverride ?? Find.CurrentMap ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (map == null)
            {
                Messages.Message("No current map found.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (faction != null && SpawnEdgeUtility.TryComputeDirectionalWeightsFromNearestSettlement8(
                    map, faction,
                    out int wl, out int wr, out int wt, out int wb,
                    out int wlt, out int wrt, out int wlb, out int wrb))
            {
                rule.Left = wl; rule.Right = wr; rule.Top = wt; rule.Bottom = wb;
                rule.LeftTop = wlt; rule.RightTop = wrt; rule.LeftBottom = wlb; rule.RightBottom = wrb;
                rule.Enabled = true;

                if (_settings.DebugLogging)
                    Log.Message($"[TSA.ForceRaidDirection][UI] {faction.GetUniqueLoadID()} {faction.Name}: " +
                                $"T{rule.Top} B{rule.Bottom} R{rule.Right} L{rule.Left} " +
                                $"RT{rule.RightTop} LT{rule.LeftTop} RB{rule.RightBottom} LB{rule.LeftBottom} (enabled)");
            }
            else
            {
                // Fallback
                rule.Left = 100; rule.Right = rule.Top = rule.Bottom = 0;
                rule.LeftTop = rule.RightTop = rule.LeftBottom = rule.RightBottom = 0;
                rule.Enabled = true;
                if (_settings.DebugLogging)
                    Log.Message($"[TSA.ForceRaidDirection][UI] {faction?.GetUniqueLoadID() ?? "null"} fallback (no settlement)");
            }
        }

        private static string Percent(float sum, float part)
        {
            float p = sum <= 0f ? 0f : (part / sum) * 100f;
            return $"{p:0.#}%";
        }

        private readonly struct TextBlock : System.IDisposable
        {
            private readonly TextAnchor _old;
            public TextBlock(TextAnchor anchor)
            {
                _old = Text.Anchor;
                Text.Anchor = anchor;
            }
            public void Dispose() => Text.Anchor = _old;
        }
    }
}
