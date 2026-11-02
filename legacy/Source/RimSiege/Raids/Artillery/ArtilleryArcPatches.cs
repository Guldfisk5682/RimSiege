using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Raids.Artillery
{
    internal static class EmplacementArcCoordinates
    {
        private static bool Managed(Thing thing) => thing == CeArtillerySight.Current?.Gun ||
            thing is Building building && ArtilleryRegistry.Find(building) != null;

        internal static Vector3 Source(Thing thing)
        {
            Vector3 source = thing.DrawPos;
            Map map = CeArtillerySight.MapFor(thing);
            return Managed(thing) && IsoFloor.HasFloors(map) ? FloorCoordinates.Ground(source, thing.Position, map) : source;
        }

        private static Vector3 Target(ref LocalTargetInfo target, Thing gun)
        {
            Vector3 center = target.CenterVector3;
            Map map = CeArtillerySight.MapFor(gun);
            return Managed(gun) && IsoFloor.HasFloors(map) ? FloorCoordinates.Ground(center, target.Cell, map) : center;
        }

        internal static Vector3 ArcTarget(ref LocalTargetInfo target, CompFireArc arc) => Target(ref target, arc.parent);
        internal static Vector3 GunTarget(ref LocalTargetInfo target, Building_TurretGunCE gun) => Target(ref target, gun);

        internal static IEnumerable<CodeInstruction> Convert(IEnumerable<CodeInstruction> instructions, MethodInfo targetHelper)
        {
            MethodInfo target = AccessTools.PropertyGetter(typeof(LocalTargetInfo), nameof(LocalTargetInfo.CenterVector3));
            MethodInfo source = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.DrawPos));
            foreach (CodeInstruction code in instructions)
            {
                if (code.Calls(target))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code).MoveBlocksFrom(code);
                    code.opcode = OpCodes.Call;
                    code.operand = targetHelper;
                }
                else if (code.Calls(source))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(EmplacementArcCoordinates), nameof(Source));
                }
                yield return code;
            }
        }
    }

    [HarmonyPatch(typeof(CompFireArc), nameof(CompFireArc.WithinFireArc))]
    internal static class EmplacementFireArcPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            EmplacementArcCoordinates.Convert(instructions, AccessTools.Method(typeof(EmplacementArcCoordinates), nameof(EmplacementArcCoordinates.ArcTarget)));
    }

    [HarmonyPatch(typeof(Building_TurretGunCE), nameof(Building_TurretGunCE.DeltaAngle), MethodType.Getter)]
    internal static class EmplacementTurnPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            EmplacementArcCoordinates.Convert(instructions, AccessTools.Method(typeof(EmplacementArcCoordinates), nameof(EmplacementArcCoordinates.GunTarget)));
    }
}
