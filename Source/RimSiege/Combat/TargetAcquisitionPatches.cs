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
    [HarmonyPatch]
    internal static class AcquisitionRangeFramePatch
    {
        // CE's optimized picker owns candidate enumeration, validation and scoring.
        private static MethodBase TargetMethod() => AccessTools.Method(
            typeof(Verb_LaunchProjectileCE).Assembly.GetType(
                "CombatExtended.HarmonyCE.Harmony_AttackTargetFinder+Harmony_AttackTargetFinder_BestAttackTarget"),
            "FindAttackTargetForRangedAttack");

        private static bool WithinRange(ref IntVec3 target, IntVec3 source, float radius, IAttackTargetSearcher searcher)
        {
            Map map = searcher.Thing.Map;
            if (searcher.CurrentEffectiveVerb is not Verb_LaunchProjectileCE || !IsoFloor.HasFloors(map) ||
                IsoFloor.FloorOf(target, map) == IsoFloor.FloorOf(source, map))
                return target.InHorDistOf(source, radius);
            return IsoFloor.RangeDistance(source, target, map) <= radius;
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
                    c.operand = AccessTools.Method(typeof(AcquisitionRangeFramePatch), nameof(WithinRange));
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
            var verb = (seer as IAttackTargetSearcher)?.CurrentEffectiveVerb as Verb_LaunchProjectileCE;
            if (verb == null || !IsoFloor.HasFloors(map) || IsoFloor.FloorOf(start, map) == IsoFloor.FloorOf(end, map))
                return GenSight.LineOfSight(start, end, map, skipFirstCell, validator, halfXOffset, halfZOffset);

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
            foreach (var c in instructions)
            {
                if (c.Calls(sight))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(AcquisitionSightFramePatch), nameof(Sees));
                }
                yield return c;
            }
        }
    }

    [HarmonyPatch(typeof(AttackTargetFinder), "GetShootingTargetScore")]
    internal static class AcquisitionScoreDistancePatch
    {
        private static float Distance(ref IntVec3 delta, IAttackTarget target, IAttackTargetSearcher searcher)
        {
            Map map = searcher.Thing.Map;
            if (searcher.CurrentEffectiveVerb is not Verb_LaunchProjectileCE || !IsoFloor.HasFloors(map) ||
                IsoFloor.FloorOf(target.Thing.Position, map) == IsoFloor.FloorOf(searcher.Thing.Position, map))
                return delta.LengthHorizontal;
            return IsoFloor.Distance3D(searcher.Thing.Position, target.Thing.Position, map);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo distance = AccessTools.PropertyGetter(typeof(IntVec3), nameof(IntVec3.LengthHorizontal));
            foreach (var c in instructions)
            {
                if (c.Calls(distance))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(AcquisitionScoreDistancePatch), nameof(Distance));
                }
                yield return c;
            }
        }
    }
}
