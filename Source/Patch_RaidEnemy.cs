using HarmonyLib;
using RimWorld;
using Verse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace TSA.ForceRaidDirection
{
    /// <summary>
    /// Steers enemy raids to weighted edges/corners and does small arrival tweaks.
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker")]
    public static class Patch_RaidEnemy_TryExecuteWorker
    {
        private static void EnsureFactionResolved(IncidentWorker_RaidEnemy instance, IncidentParms parms)
        {
            if (parms.faction != null) return;

            var m = AccessTools.Method(typeof(IncidentWorker_Raid), "TryResolveRaidFaction");
            if (m != null)
            {
                try { m.Invoke(instance, new object[] { parms }); } catch { /* ignore */ }
            }

            if (parms.faction == null)
            {
                var f = Find.FactionManager.RandomEnemyFaction(allowHidden: false, allowDefeated: false, allowNonHumanlike: true);
                if (f != null) parms.faction = f;
            }
        }

        private static string N(Def def) => def?.defName ?? "(null)";

        private static void PreprocessArrivalMode(Map map, IncidentParms parms, ForceRaidDirectionSettings s)
        {
            // Capture originals for logging
            var origStrategy = parms.raidStrategy;
            var origArrival = parms.raidArrivalMode;

            // --- Strategy conversion: StageThenAttack only -> 50/50 ImmediateAttack/Smart
            // Do not rewrite null: vanilla ResolveRaidStrategy must pick a CanUseWith-valid strategy.
            // Forcing Smart onto a null WD/scripted raid can fail pawn generation for some factions.
            if (s.ConvertStageThenAttackToImmediate)
            {
                var current = parms.raidStrategy;
                bool isStage = current != null && (current.defName.Equals("StageThenAttack", StringComparison.OrdinalIgnoreCase) ||
                                                   (current.workerClass != null && current.workerClass.Name.Contains("StageThenAttack")));

                if (isStage)
                {
                    var smart = DefDatabase<RaidStrategyDef>.GetNamedSilentFail("ImmediateAttackSmart");
                    var immediate = DefDatabase<RaidStrategyDef>.GetNamedSilentFail("ImmediateAttack");

                    // Prefer Smart only when the faction can use avoid-grid AI.
                    bool canSmart = smart != null && parms.faction != null && parms.faction.def.canUseAvoidGrid;
                    if (canSmart && Rand.Value < 0.5f)
                        parms.raidStrategy = smart;
                    else
                        parms.raidStrategy = immediate ?? smart;
                }
            }

            // --- Arrival conversions
            if (!SpaceMapGuard.IsSpaceLike(map))
            {
                if (s.CoerceCenterDropToEdgeDrop &&
                    (parms.raidArrivalMode == PawnsArrivalModeDefOf.CenterDrop ||
                     parms.raidArrivalMode == PawnsArrivalModeDefOf.RandomDrop))
                {
                    parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeDrop;
                }
            }

            if (s.CoerceMultiSideToSingleEdge)
            {
                if (parms.raidArrivalMode == PawnsArrivalModeDefOf.EdgeWalkInGroups)
                    parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;

                var edgeDropGroups = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail("EdgeDropGroups");
                if (edgeDropGroups != null && parms.raidArrivalMode == edgeDropGroups)
                    parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeDrop;

                var edgeDist = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail("EdgeWalkInDistributed");
                if (edgeDist != null && parms.raidArrivalMode == edgeDist)
                    parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            }

            // Fallback if arrival is still unset
            if (parms.raidArrivalMode == null)
                parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;

            // Log original -> final mapping
            if (s.DebugLogging)
            {
                SpawnEdgeUtility.LogDebug(
                    $"[Raid preprocess] Strategy: {N(origStrategy)} -> {N(parms.raidStrategy)} | Arrival: {N(origArrival)} -> {N(parms.raidArrivalMode)}");
            }
        }

        private static void PickEdgeAndCenter(
            Map map, Faction faction, ForceRaidDirectionSettings s,
            out EdgeDir chosen, out IntVec3 center, out Rot4 rot,
            out int wl, out int wr, out int wt, out int wb, out int wlt, out int wrt, out int wlb, out int wrb)
        {
            center = IntVec3.Invalid;
            rot = Rot4.Random;

            s.GetWeightsFor(faction, out wl, out wr, out wt, out wb, out wlt, out wrt, out wlb, out wrb);

            if (wl + wr + wt + wb + wlt + wrt + wlb + wrb == 0)
            {
                if (faction != null && WorldDirectionResolver.TryCompute8DirFromNearestSettlement(map, faction, out var derived))
                {
                    wl = derived.Left; wr = derived.Right; wt = derived.Top; wb = derived.Bottom;
                    wlt = derived.LeftTop; wrt = derived.RightTop; wlb = derived.LeftBottom; wrb = derived.RightBottom;
                }
                else
                {
                    wl = 100;
                }
            }

            int band = s.EdgeBandWidth > 0 ? s.EdgeBandWidth : 12;
            chosen = SpawnEdgeUtility.TryPickWeightedDir(map, wl, wr, wt, wb, wlt, wrt, wlb, wrb, band);

            if (SpawnEdgeUtility.TryPickBandCell(map, chosen, band, out var spawnCell) && spawnCell.IsValid)
            {
                center = spawnCell;
                rot = SpawnEdgeUtility.DesiredRotationFor(chosen, map, spawnCell);
            }
            else
            {
                center = CellFinder.RandomEdgeCell(map);
                rot = Rot4.FromAngleFlat((map.Center - center).ToVector3Shifted().AngleFlat());
            }
        }

        static void Prefix(IncidentWorker_RaidEnemy __instance, IncidentParms parms)
        {
            var s = Mod_ForceRaidDirection.Settings;
            if (s == null) return;

            var map = parms.target as Map;
            if (map == null) return;

            if (SpaceMapGuard.IsSpaceLike(map))
            {
                if (s.DebugLogging)
                    SpawnEdgeUtility.LogDebug("Space-like map detected; skipping raid steering/coercion.");
                return;
            }

            if (CaravanClashGuard.ShouldSkipSteering(map))
            {
                if (s.DebugLogging)
                    SpawnEdgeUtility.LogDebug("WD caravan clash / interception map; skipping raid steering.");
                return;
            }

            EnsureFactionResolved(__instance, parms);
            PreprocessArrivalMode(map, parms, s);

            PickEdgeAndCenter(map, parms.faction, s,
                out var chosen, out var center, out var rot,
                out int wl, out int wr, out int wt, out int wb, out int wlt, out int wrt, out int wlb, out int wrb);

            if (center.IsValid)
            {
                parms.spawnCenter = center;
                parms.spawnRotation = rot;

                if (s.DebugLogging)
                {
                    string facName = parms.faction != null
                        ? $"{parms.faction.Name} ({parms.faction.def?.defName})"
                        : "no faction";
                    SpawnEdgeUtility.LogDebug(
                        $"Dir chosen: {chosen}; spawnCenter={parms.spawnCenter} rot={parms.spawnRotation.AsInt}; " +
                        $"weights L={wl} R={wr} T={wt} B={wb} LT={wlt} RT={wrt} LB={wlb} RB={wrb} band={s.EdgeBandWidth}; faction={facName}");
                }
            }
            else if (s.DebugLogging)
            {
                string facName = parms.faction != null
                    ? $"{parms.faction.Name} ({parms.faction.def?.defName})"
                    : "no faction";
                SpawnEdgeUtility.LogDebug(
                    $"Dir chosen: {chosen}; no valid cell found (arrivalMode will handle). " +
                    $"weights L={wl} R={wr} T={wt} B={wb} LT={wlt} RT={wrt} LB={wlb} RB={wrb} band={s.EdgeBandWidth}; faction={facName}");
            }
        }
    }

    // ---------------------------------------------------------------
    // Manhunters: bracket + targeted transpiler to force entry edge
    // ---------------------------------------------------------------
    [HarmonyPatch]
    public static class Patch_IncidentWorker_TryExecute_Manhunter
    {
        [ThreadStatic] private static bool _inManhunter;
        [ThreadStatic] private static Map _ctxMap;
        [ThreadStatic] private static float _ctxPoints;

        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(IncidentWorker), "TryExecute");

        static void Prefix(IncidentWorker __instance, IncidentParms parms, ref bool __state)
        {
            __state = false;
            _ctxMap = null;
            _ctxPoints = 0f;

            var s = Mod_ForceRaidDirection.Settings;
            if (s == null) return;

            if (__instance?.def?.defName == "ManhunterPack" && parms?.target is Map map)
            {
                if (s.ApplyToManhunters && !SpaceMapGuard.IsSpaceLike(map))
                {
                    _inManhunter = true;
                    _ctxMap = map;
                    _ctxPoints = parms.points;
                    __state = true;

                    // Tell the forcer which map is active.
                    ManhunterEntryForcer.Begin(map);

                    // (removed noisy bracket-begin log)
                }
            }
        }

        static void Postfix(IncidentWorker __instance, bool __state, bool __result)
        {
            if (__state && __instance?.def?.defName == "ManhunterPack")
            {
                _inManhunter = false;
                _ctxMap = null;
                _ctxPoints = 0f;

                // Clear context
                ManhunterEntryForcer.End();

                // (removed result/bracket-end logs)
            }
        }

        internal static bool InManhunter => _inManhunter;
        internal static Map CtxMap => _ctxMap;
    }

    // ----------------------------------------------
    // Targeted transpiler: rewrite only the manhunter
    // worker’s calls to RCellFinder.TryFindRandomPawnEntryCell
    // ----------------------------------------------
    [HarmonyPatch]
    public static class Patch_AggressiveAnimals_TryExecuteWorker_Transpile
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var list = new List<MethodBase>();
            var t1 = AccessTools.TypeByName("RimWorld.IncidentWorker_AggressiveAnimals");
            if (t1 != null)
            {
                var m = AccessTools.Method(t1, "TryExecuteWorker");
                if (m != null) list.Add(m);
            }
            var t2 = AccessTools.TypeByName("RimWorld.IncidentWorker_ManhunterPack"); // legacy/compat
            if (t2 != null)
            {
                var m = AccessTools.Method(t2, "TryExecuteWorker");
                if (m != null) list.Add(m);
            }

            if (list.Count == 0 && Mod_ForceRaidDirection.Settings?.DebugLogging == true)
                SpawnEdgeUtility.LogDebug("[Manhunter] No AggressiveAnimals worker found to transpile (version mismatch?).");

            return list;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);

            // Original overloads we might see
            var orig4 = AccessTools.Method(typeof(RCellFinder), "TryFindRandomPawnEntryCell",
                new Type[] { typeof(IntVec3).MakeByRefType(), typeof(Map), typeof(float), typeof(bool) });
            var orig5 = AccessTools.Method(typeof(RCellFinder), "TryFindRandomPawnEntryCell",
                new Type[] { typeof(IntVec3).MakeByRefType(), typeof(Map), typeof(float), typeof(bool), typeof(Predicate<IntVec3>) });

            // Our replacements (same signatures)
            var rep4 = AccessTools.Method(typeof(ManhunterEntryForcer), nameof(ManhunterEntryForcer.TryFindForced4));
            var rep5 = AccessTools.Method(typeof(ManhunterEntryForcer), nameof(ManhunterEntryForcer.TryFindForced5));

            int patched = 0;
            for (int i = 0; i < code.Count; i++)
            {
                var ci = code[i];
                if (ci.opcode == OpCodes.Call && ci.operand is MethodInfo mi)
                {
                    if (orig4 != null && mi == orig4)
                    {
                        ci.operand = rep4;
                        patched++;
                    }
                    else if (orig5 != null && mi == orig5)
                    {
                        ci.operand = rep5;
                        patched++;
                    }
                }
            }

            if (Mod_ForceRaidDirection.Settings?.DebugLogging == true)
                SpawnEdgeUtility.LogDebug($"[Manhunter] Transpiler patched {patched} call(s) to RCellFinder.TryFindRandomPawnEntryCell in worker.");

            return code;
        }
    }

    // ----------------------------------------------
    // Helper that applies our band constraint safely
    // ----------------------------------------------
    internal static class ManhunterEntryForcer
    {
        [ThreadStatic] private static Map _mapCtx;
        public static void Begin(Map map) => _mapCtx = map;
        public static void End() => _mapCtx = null;

        private static bool ActiveFor(Map map)
        {
            return map != null
                   && _mapCtx == map
                   && Mod_ForceRaidDirection.Settings != null
                   && Mod_ForceRaidDirection.Settings.ApplyToManhunters
                   && !SpaceMapGuard.IsSpaceLike(map);
        }

        // Matches 4-arg overload
        public static bool TryFindForced4(out IntVec3 result, Map map, float roadChance, bool allowFogged)
        {
            if (!ActiveFor(map))
                return RCellFinder.TryFindRandomPawnEntryCell(out result, map, roadChance, allowFogged);

            var s = Mod_ForceRaidDirection.Settings;
            s.GetWeightsFor(null, out int wl, out int wr, out int wt, out int wb, out int wlt, out int wrt, out int wlb, out int wrb);
            int band = s.EdgeBandWidth > 0 ? s.EdgeBandWidth : 12;
            var chosen = SpawnEdgeUtility.TryPickWeightedDir(map, wl, wr, wt, wb, wlt, wrt, wlb, wrb, band);

            Predicate<IntVec3> bandValidator = c => IsInDirBand(map, c, chosen, band);

            // Prefer constrained search (allow fogged like vanilla usually does)
            if (RCellFinder.TryFindRandomPawnEntryCell(out result, map, roadChance, true, bandValidator))
            {
                if (s.DebugLogging)
                {
                    var rot = SpawnEdgeUtility.DesiredRotationFor(chosen, map, result);
                    const string facName = "Manhunters";
                    SpawnEdgeUtility.LogDebug(
                        $"Dir chosen: {chosen}; spawnCenter={result} rot={rot.AsInt}; " +
                        $"weights L={wl} R={wr} T={wt} B={wb} LT={wlt} RT={wrt} LB={wlb} RB={wrb} band={band}; faction={facName}");
                }
                return true;
            }

            // Fallback to vanilla 4-arg so incident cannot fail
            if (s.DebugLogging) SpawnEdgeUtility.LogDebug("[Manhunter] Constrained search failed; falling back to vanilla entry.");
            return RCellFinder.TryFindRandomPawnEntryCell(out result, map, roadChance, allowFogged);
        }

        // Matches 5-arg overload
        public static bool TryFindForced5(out IntVec3 result, Map map, float roadChance, bool allowFogged, Predicate<IntVec3> extraValidator)
        {
            if (!ActiveFor(map))
                return RCellFinder.TryFindRandomPawnEntryCell(out result, map, roadChance, allowFogged, extraValidator);

            var s = Mod_ForceRaidDirection.Settings;
            s.GetWeightsFor(null, out int wl, out int wr, out int wt, out int wb, out int wlt, out int wrt, out int wlb, out int wrb);
            int band = s.EdgeBandWidth > 0 ? s.EdgeBandWidth : 12;
            var chosen = SpawnEdgeUtility.TryPickWeightedDir(map, wl, wr, wt, wb, wlt, wrt, wlb, wrb, band);

            Predicate<IntVec3> bandValidator = c => IsInDirBand(map, c, chosen, band);
            Predicate<IntVec3> combined = extraValidator == null ? bandValidator : (c => bandValidator(c) && extraValidator(c));

            if (RCellFinder.TryFindRandomPawnEntryCell(out result, map, roadChance, true, combined))
            {
                if (s.DebugLogging)
                {
                    var rot = SpawnEdgeUtility.DesiredRotationFor(chosen, map, result);
                    const string facName = "no faction";
                    SpawnEdgeUtility.LogDebug(
                        $"Dir chosen: {chosen}; spawnCenter={result} rot={rot.AsInt}; " +
                        $"weights L={wl} R={wr} T={wt} B={wb} LT={wlt} RT={wrt} LB={wlb} RB={wrb} band={band}; faction={facName}");
                }
                return true;
            }

            if (s.DebugLogging) SpawnEdgeUtility.LogDebug("[Manhunter] Constrained+combined failed; falling back to vanilla entry.");
            return RCellFinder.TryFindRandomPawnEntryCell(out result, map, roadChance, allowFogged, extraValidator);
        }

        private static bool IsInDirBand(Map map, IntVec3 c, EdgeDir dir, int band)
        {
            int w = map.Size.x;
            int h = map.Size.z;

            switch (dir)
            {
                case EdgeDir.Left: return c.x < band;
                case EdgeDir.Right: return c.x >= w - band;
                case EdgeDir.Top: return c.z >= h - band;
                case EdgeDir.Bottom: return c.z < band;

                case EdgeDir.LeftTop: return c.x < band && c.z >= h - band;
                case EdgeDir.RightTop: return c.x >= w - band && c.z >= h - band;
                case EdgeDir.LeftBottom: return c.x < band && c.z < band;
                case EdgeDir.RightBottom: return c.x >= w - band && c.z < band;
                default: return false;
            }
        }
    }
}
