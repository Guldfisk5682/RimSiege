using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Aiming
{
    [HarmonyPatch]
    internal static class IndirectChargeFramePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Verb_LaunchProjectileCE), "ShotSpeed");
            foreach (MethodBase method in CeAccess.Methods(typeof(Verb_ShootMortarCE), "GetMissRadiusForDist"))
                yield return method;
        }

        public static float Distance(ref IntVec3 delta, Verb_LaunchProjectileCE verb)
        {
            Map map = verb.Caster.Map;
            return IsoFloor.HasFloors(map)
                ? (IsoFloor.GroundCellOf(verb.CurrentTarget.Cell, map) - IsoFloor.GroundCellOf(verb.Caster.Position, map)).LengthHorizontal
                : delta.LengthHorizontal;
        }

        public static float Height(Verb_LaunchProjectileCE verb)
        {
            Map map = verb.Caster.Map;
            // Charges predict range to y=0. Move that zero to the target floor;
            // CE's charge speeds and maximum-range formula remain unchanged.
            return verb.ShotHeight - (IsoFloor.HasFloors(map) && verb.CurrentTarget.IsValid
                ? CombatHeights.Base(verb.CurrentTarget.Cell, map) : 0f);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo distance = AccessTools.PropertyGetter(typeof(IntVec3), nameof(IntVec3.LengthHorizontal));
            MethodInfo height = AccessTools.PropertyGetter(typeof(Verb_LaunchProjectileCE), nameof(Verb_LaunchProjectileCE.ShotHeight));
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(distance))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(IndirectChargeFramePatch), nameof(Distance));
                }
                else if (c.Calls(height))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(IndirectChargeFramePatch), nameof(Height));
                }
                yield return c;
            }
        }
    }

    [HarmonyPatch]
    internal static class IndirectVisibilityPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(Verb_ShootMortarCE), "ShiftVecReportFor")
            .Where(m => m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(LocalTargetInfo));

        public static bool Sees(IntVec3 from, IntVec3 to, Map map, bool skipFirst,
            Func<IntVec3, bool> validator, int halfX, int halfZ)
        {
            if (!IsoFloor.HasFloors(map) || IsoFloor.FloorOf(from, map) == IsoFloor.FloorOf(to, map))
                return GenSight.LineOfSight(from, to, map, skipFirst, validator, halfX, halfZ);
            return IsoFloor.CanSeeBetweenFloors(from, to, map);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo sight = AccessTools.Method(typeof(GenSight), nameof(GenSight.LineOfSight),
                new[] { typeof(IntVec3), typeof(IntVec3), typeof(Map), typeof(bool), typeof(Func<IntVec3, bool>), typeof(int), typeof(int) });
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(sight)) c.operand = AccessTools.Method(typeof(IndirectVisibilityPatch), nameof(Sees));
                yield return c;
            }
        }
    }

    [HarmonyPatch]
    internal static class LaunchRoofReferencePatch
    {
        private static MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(ProjectileCE))
            .Single(m => m.Name == "Launch" && m.GetParameters().Length == 8);

        public static float RoofHeight(ProjectileCE projectile) => CollisionVertical.WallCollisionHeight +
            (IsoFloor.HasFloors(projectile.Map) ? CombatHeights.Base(projectile.Position, projectile.Map) : 0f);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction c in instructions)
            {
                if (c.opcode == OpCodes.Ldc_R4 && c.operand is float value && value == CollisionVertical.WallCollisionHeight)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LaunchRoofReferencePatch), nameof(RoofHeight)));
                    continue;
                }
                yield return c;
            }
        }
    }
}
