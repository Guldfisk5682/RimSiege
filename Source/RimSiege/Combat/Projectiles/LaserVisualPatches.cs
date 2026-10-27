using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended.Lasers;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimSiege.Combat.Projectiles
{
    internal static class LaserVisual
    {
        internal static readonly AccessTools.FieldRef<LaserBeamGraphicCE, Vector3> Start =
            AccessTools.FieldRefAccess<LaserBeamGraphicCE, Vector3>("a");
        internal static readonly AccessTools.FieldRef<LaserBeamGraphicCE, Vector3> End =
            AccessTools.FieldRefAccess<LaserBeamGraphicCE, Vector3>("b");

        public static Vector3 Point(Vector3 point, LaserBeamGraphicCE beam) => IsoView.Ready && IsoFloor.HasFloors(beam.Map)
            ? VisualCoordinates.Projectile(point, 0f, beam.Map) : point;

        public static float Altitude(AltitudeLayer layer, LaserBeamGraphicCE beam) => layer.AltitudeFor() +
            (IsoView.Ready && IsoFloor.HasFloors(beam.Map) ? (Point(Start(beam), beam).y + Point(End(beam), beam).y) * 0.5f : 0f);

        public static IntVec3 Cell(Vector3 point, LaserBeamGraphicCE beam) => FloorCoordinates.WorldCell(point, beam.Map);

        public static Vector3 Origin(LaserBeamCE beam)
        {
            if (IsoFloor.HasFloors(beam.Map)) return CeProjectileOrigins.Of(beam).ToVector3Shifted();
            return CeAccess.LaserOrigin(beam);
        }

        public static Vector3 TargetCenter(Thing target) => IsoFloor.HasFloors(target.Map)
            ? CeAccess.BoundsFor(target).center : target.TrueCenter();
    }

    [HarmonyPatch(typeof(LaserBeamCE), "SpawnBeam")]
    internal static class LaserGraphicSpawnPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo origin = AccessTools.PropertyGetter(typeof(LaserBeamCE), "Origin");
            foreach (CodeInstruction code in instructions)
            {
                if (code.Calls(origin))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(LaserVisual), nameof(LaserVisual.Origin));
                }
                yield return code;
            }
        }
    }

    [HarmonyPatch(typeof(LaserBeamCE), "get_destination")]
    internal static class LaserContactDestinationPatch
    {
        private static void Postfix(LaserBeamCE __instance, ref Vector3 __result)
        {
            if (LaserRayFrame.Current?.Projectile == __instance && LaserRayFrame.Current.HasEnd)
                __result = LaserRayFrame.Current.End;
        }
    }

    [HarmonyPatch(typeof(LaserBeamCE), nameof(LaserBeamCE.Impact), new[] { typeof(Thing), typeof(Vector3) })]
    internal static class LaserReflectionFramePatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo center = AccessTools.Method(typeof(GenThing), nameof(GenThing.TrueCenter), new[] { typeof(Thing) });
            foreach (CodeInstruction code in instructions)
            {
                if (code.Calls(center)) code.operand = AccessTools.Method(typeof(LaserVisual), nameof(LaserVisual.TargetCenter));
                yield return code;
            }
        }
    }

    [HarmonyPatch]
    internal static class LaserGraphicFramePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LaserBeamGraphicCE), "SetupDrawing");
            yield return AccessTools.Method(typeof(LaserBeamGraphicCE), nameof(LaserBeamGraphicCE.DrawAt));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            LocalBuilder visualPoint = generator.DeclareLocal(typeof(Vector3));
            foreach (CodeInstruction code in instructions)
            {
                if (code.operand is MethodInfo method && method.Name == "AltitudeFor")
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code).MoveBlocksFrom(code);
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(LaserVisual), nameof(LaserVisual.Altitude));
                    yield return code;
                }
                else
                {
                    bool point = (code.opcode == OpCodes.Ldfld || code.opcode == OpCodes.Ldflda) &&
                        code.operand is FieldInfo field && field.DeclaringType == typeof(LaserBeamGraphicCE) &&
                        (field.Name == "a" || field.Name == "b");
                    bool address = point && code.opcode == OpCodes.Ldflda;
                    if (address) code.opcode = OpCodes.Ldfld;
                    yield return code;
                    if (point)
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LaserVisual), nameof(LaserVisual.Point)));
                        if (address)
                        {
                            yield return new CodeInstruction(OpCodes.Stloc, visualPoint);
                            yield return new CodeInstruction(OpCodes.Ldloca, visualPoint);
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch]
    internal static class LaserGraphicCellsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LaserBeamGraphicCE), nameof(LaserBeamGraphicCE.SpawnSetup));
            yield return AccessTools.Method(typeof(LaserBeamGraphicCE), nameof(LaserBeamGraphicCE.Explode));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo cell = AccessTools.Method(typeof(IntVec3Utility), nameof(IntVec3Utility.ToIntVec3), new[] { typeof(Vector3) });
            foreach (CodeInstruction code in instructions)
            {
                if (code.Calls(cell))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code).MoveBlocksFrom(code);
                    code.operand = AccessTools.Method(typeof(LaserVisual), nameof(LaserVisual.Cell));
                }
                yield return code;
            }
        }
    }
}
