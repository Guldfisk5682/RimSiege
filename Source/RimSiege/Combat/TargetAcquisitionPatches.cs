using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using System;
using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Aiming;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using UnityEngine;
using Verse.AI;
using Verse;

namespace RimSiege.Combat
{
    internal static class AcquisitionContext
    {
        [ThreadStatic] internal static IAttackTargetSearcher Searcher;
        [ThreadStatic] internal static Verb_LaunchProjectileCE SightVerb;

        internal static bool Supported(Verb verb) => verb is Verb_LaunchProjectileCE || verb is Verb_LaunchProjectile;

        internal static Verb VerbFor(Thing seer) => SightVerb?.Caster == seer ? SightVerb :
            Searcher?.Thing == seer ? Searcher.CurrentEffectiveVerb : (seer as IAttackTargetSearcher)?.CurrentEffectiveVerb;
    }

    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget))]
    internal static class AcquisitionContextPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(IAttackTargetSearcher searcher, out IAttackTargetSearcher __state)
        {
            __state = AcquisitionContext.Searcher;
            AcquisitionContext.Searcher = searcher;
        }

        private static void Finalizer(IAttackTargetSearcher __state) => AcquisitionContext.Searcher = __state;
    }

    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget))]
    internal static class IsoTargetSelectionPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(IAttackTargetSearcher searcher, TargetScanFlags flags, Predicate<Thing> validator,
            float minDist, float maxDist, bool canBashDoors, bool canTakeTargetsCloserThanEffectiveMinRange,
            bool canBashFences, bool onlyRanged, ref IAttackTarget __result)
        {
            if (AcquisitionContext.Supported(searcher.CurrentEffectiveVerb) && IsoFloor.HasFloors(searcher.Thing.Map))
                IsoCombatAi.SelectTarget(searcher, flags, validator, minDist, maxDist, canBashDoors,
                    canTakeTargetsCloserThanEffectiveMinRange, canBashFences, onlyRanged, ref __result);
        }
    }

    [HarmonyPatch]
    internal static class SameFloorCandidatesPatch
    {
        // As in ProjectZ, preserve the game's own-floor result and let its
        // cross-floor picker compare against it, including the walk-under fallback.
        private static MethodBase TargetMethod() => AccessTools.Method(
            typeof(Verb_LaunchProjectileCE).Assembly.GetType(
                "CombatExtended.HarmonyCE.Harmony_AttackTargetFinder+Harmony_AttackTargetFinder_BestAttackTarget"),
            "FindAttackTargetForRangedAttack");

        private static bool WithinRange(ref IntVec3 target, IntVec3 source, float radius, IAttackTargetSearcher searcher)
        {
            Map map = searcher.Thing.Map;
            if (!AcquisitionContext.Supported(searcher.CurrentEffectiveVerb) || !IsoFloor.HasFloors(map) ||
                IsoFloor.FloorOf(target, map) == IsoFloor.FloorOf(source, map))
                return target.InHorDistOf(source, radius);
            return false;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo range = AccessTools.Method(typeof(IntVec3), nameof(IntVec3.InHorDistOf), new[] { typeof(IntVec3), typeof(float) });
            foreach (var c in instructions)
            {
                if (c.Calls(range))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(SameFloorCandidatesPatch), nameof(WithinRange));
                }
                yield return c;
            }
        }
    }

    [HarmonyPatch(typeof(ProjectZ.Combat.Sight), nameof(ProjectZ.Combat.Sight.Sees))]
    internal static class IsoTargetSightPatch
    {
        private static bool Prefix(Thing seer, Thing target, Verb verb, Func<IntVec3, bool> validator,
            ref bool __result, out Verb_LaunchProjectileCE __state)
        {
            __state = AcquisitionContext.SightVerb;
            if (verb is not Verb_LaunchProjectileCE ce || !IsoFloor.HasFloors(seer.Map) ||
                IsoFloor.FloorOf(seer.Position, seer.Map) == IsoFloor.FloorOf(target.Position, seer.Map))
                return true;

            // Use CE's loaded projectile and CE's range-independent cell checks.
            // The explicit verb also preserves independent attached-turret sights.
            AcquisitionContext.SightVerb = ce;
            __result = ce.Projectile.projectile.flyOverhead || AttackTargetFinder.CanSee(seer, target, validator);
            return false;
        }

        private static void Finalizer(Verb_LaunchProjectileCE __state) => AcquisitionContext.SightVerb = __state;
    }

    [HarmonyPatch]
    internal static class IsoEmpTargetPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ProjectZ.Combat.AttackTargetFinder_Best_Patch), "Valid");

        // CE's own picker removes vanilla's flesh-target exclusion for EMP.
        private static bool OnlyMachines(Verb verb) => verb is not Verb_LaunchProjectileCE && verb.IsEMP();

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo emp = AccessTools.Method(typeof(VerbUtility), nameof(VerbUtility.IsEMP));
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(emp))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(IsoEmpTargetPatch), nameof(OnlyMachines));
                }
                yield return c;
            }
        }
    }

    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.CanSee))]
    internal static class AcquisitionSightFramePatch
    {
        // Keep the native destination/lean loops, and replace only their cell LOS.
        // Ignoring range here preserves CanSee's meaning for AI that may move.
        private static bool Sees(IntVec3 start, IntVec3 end, Map map, bool skipFirstCell,
            Func<IntVec3, bool> validator, int halfXOffset, int halfZOffset, Thing seer, Thing target)
        {
            Verb effective = AcquisitionContext.VerbFor(seer);
            if (!AcquisitionContext.Supported(effective) || !IsoFloor.HasFloors(map) || IsoFloor.FloorOf(start, map) == IsoFloor.FloorOf(end, map))
                return GenSight.LineOfSight(start, end, map, skipFirstCell, validator, halfXOffset, halfZOffset);

            if (effective is not Verb_LaunchProjectileCE verb)
                return IsoVanillaCombat.Sight(start, end, map, target.def.Fillage == FillCategory.Full, validator);

            Vector3 source = FloorCoordinates.Ground(start.ToVector3Shifted(), start, map);
            source.y = verb.ShotHeight;
            if (!CeAccess.CanHitCell(verb, source, end, target)) return false;
            if (validator != null)
            {
                foreach (var cell in SightQueries.SightCells(source, end.ToVector3Shifted(), verb, target))
                    if ((!skipFirstCell || cell != start) && !validator(cell)) return false;
            }
            return true;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo sight = AccessTools.Method(typeof(GenSight), nameof(GenSight.LineOfSight),
                new[] { typeof(IntVec3), typeof(IntVec3), typeof(Map), typeof(bool), typeof(Func<IntVec3, bool>), typeof(int), typeof(int) });
            MethodInfo sources = AccessTools.Method(typeof(ShootLeanUtility), nameof(ShootLeanUtility.LeanShootingSourcesFromTo));
            MethodInfo destinations = AccessTools.Method(typeof(ShootLeanUtility), nameof(ShootLeanUtility.CalcShootableCellsOf));
            foreach (var c in instructions)
            {
                if (c.Calls(sight))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(AcquisitionSightFramePatch), nameof(Sees));
                }
                else if (c.Calls(sources) || c.Calls(destinations))
                {
                    bool source = c.Calls(sources);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(LeanQueries), source ? nameof(LeanQueries.Sources) : nameof(LeanQueries.Destinations));
                }
                yield return c;
            }
        }
    }

    [HarmonyPatch]
    internal static class CeTargetScoreDistancePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(
            typeof(Verb_LaunchProjectileCE).Assembly.GetType(
                "CombatExtended.HarmonyCE.Harmony_AttackTargetFinder+Harmony_AttackTargetFinder_GetShootingTargetScore"),
            "Postfix");

        // ProjectZ.Score already replaces the vanilla flat-distance penalty.
        // Only CE's additional score terms still need their band-space distances lifted.
        private static float Distance(IntVec3 from, IntVec3 to, IAttackTargetSearcher searcher)
        {
            Map map = searcher.Thing.Map;
            if (!AcquisitionContext.Supported(searcher.CurrentEffectiveVerb) || !IsoFloor.HasFloors(map) ||
                IsoFloor.FloorOf(from, map) == IsoFloor.FloorOf(to, map))
                return from.DistanceTo(to);
            return IsoFloor.Distance3D(from, to, map);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo distance = AccessTools.Method(typeof(IntVec3Utility), nameof(IntVec3Utility.DistanceTo), new[] { typeof(IntVec3), typeof(IntVec3) });
            foreach (var c in instructions)
            {
                if (c.Calls(distance))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(CeTargetScoreDistancePatch), nameof(Distance));
                }
                yield return c;
            }
        }
    }
}
