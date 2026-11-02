using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Aiming
{
    [HarmonyPatch]
    internal static class VerbPositionCallsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(Verb_LaunchProjectileCE))
            .Where(m => new[] { "ShiftTarget", "ShiftVecReportFor", "ShotAngle", "ShotRotation", "AdjustShotHeight", "GetHighestCoverAndSmokeForTarget", "CanHitCellFromCellIgnoringRange" }.Contains(m.Name));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            foreach (var c in instructions)
            {
                if (c.operand is MethodInfo method)
                {
                    if (method == AccessTools.Method(typeof(GenThing), "TrueCenter", new[] { typeof(Thing) }))
                    {
                        c.opcode = OpCodes.Call;
                        c.operand = AccessTools.Method(typeof(FloorCoordinates), nameof(FloorCoordinates.TrueCenter));
                    }
                    else if (method == AccessTools.PropertyGetter(typeof(Thing), "DrawPos"))
                    {
                        c.opcode = OpCodes.Call;
                        c.operand = AccessTools.Method(typeof(FloorCoordinates), nameof(FloorCoordinates.DrawPos));
                    }
                    else if (__originalMethod.Name == "AdjustShotHeight" && method == AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Position)))
                    {
                        // Only the triangle's horizontal distance is read here;
                        // the bounds already carry CE world heights.
                        c.opcode = OpCodes.Call;
                        c.operand = AccessTools.Method(typeof(FloorCoordinates), nameof(FloorCoordinates.GroundPosition));
                    }
                    else if (method.DeclaringType == typeof(GenSightCE) && method.Name == "AllPointsOnLineOfSight")
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                        yield return new CodeInstruction(OpCodes.Ldarg_1);
                        c.opcode = OpCodes.Call;
                        c.operand = AccessTools.Method(typeof(CoverQueries), nameof(CoverQueries.Cells));
                    }
                    else if (__originalMethod.Name == "CanHitCellFromCellIgnoringRange" && method == AccessTools.PropertyGetter(typeof(CollisionVertical), "Max"))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_3).MoveLabelsFrom(c).MoveBlocksFrom(c);
                        c.opcode = OpCodes.Call;
                        c.operand = AccessTools.Method(typeof(CombatHeights), nameof(CombatHeights.TargetTop));
                    }
                }
                yield return c;
            }
        }
    }

    [HarmonyPatch]
    internal static class ShiftTargetFramePatch
    {
        private static MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(Verb_LaunchProjectileCE)).Single(m => m.Name == "ShiftTarget" && m.GetParameters().Length == 5);
        private static void Prefix(Verb_LaunchProjectileCE __instance, ref Vector3 v)
        {
            if (IsoFloor.HasFloors(__instance.Caster.Map))
                v = FloorCoordinates.Ground(v, __instance.CurrentTarget.Cell, __instance.Caster.Map);
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), nameof(Verb_LaunchProjectileCE.ShiftVecReportFor), new[] { typeof(LocalTargetInfo), typeof(IntVec3) })]
    internal static class ReportFramePatch
    {
        private static void Postfix(Verb_LaunchProjectileCE __instance, LocalTargetInfo target, ref ShiftVecReport __result)
        {
            Map map = __instance.Caster.Map;
            if (!IsoFloor.HasFloors(map)) return;
            IntVec3 from = IsoFloor.GroundCellOf(__instance.Caster.Position, map), to = IsoFloor.GroundCellOf(target.Cell, map);
            __result.shotDist = (to - from).LengthHorizontal;
            Vector3 source = FloorCoordinates.TrueCenter(__instance.Caster);
            CeAccess.ReportSource(__result) = new Vector2(source.x, source.z);
        }
    }

    [HarmonyPatch]
    internal static class ShotSourceFramePatch
    {
        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(Verb_LaunchProjectileCE), "ShotSource");
        private static void Postfix(Verb_LaunchProjectileCE __instance, ref Vector3 __result)
        {
            Map map = CeArtillerySight.MapFor(__instance.Caster);
            if (IsoFloor.HasFloors(map))
                __result = FloorCoordinates.Ground(__result, __instance.Caster.Position, map);
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), "CanHitCellFromCellIgnoringRange")]
    internal static class SightSourceFramePatch
    {
        private static void Prefix(Verb_LaunchProjectileCE __instance, ref Vector3 shotSource)
        {
            Map map = CeArtillerySight.MapFor(__instance.Caster);
            if (IsoFloor.HasFloors(map))
                shotSource = FloorCoordinates.Ground(shotSource, map);
            // Plane rejection now runs after CE adjusts muzzle height and lean,
            // using the exact same endpoints as its per-cell fine-check Ray.
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), "GetMinCollisionDistance")]
    internal static class MinimumDistanceFramePatch
    {
        private static void Prefix(Verb_LaunchProjectileCE __instance, ref float targetDistance)
        {
            Map map = __instance.Caster.Map;
            if (IsoFloor.HasFloors(map))
                targetDistance = (IsoFloor.GroundCellOf(__instance.CurrentTarget.Cell, map) - IsoFloor.GroundCellOf(__instance.Caster.Position, map)).LengthHorizontal;
        }
    }
}
