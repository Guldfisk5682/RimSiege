using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Combat.Projectiles;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Aiming
{
    [HarmonyPatch(typeof(ProjectileCE), "CheckForCollisionBetween")]
    internal static class SuppressionHeightFramePatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var codes = instructions.ToList();
            LocalVariableInfo map = __originalMethod.GetMethodBody().LocalVariables.First(v => v.LocalType == typeof(Map));
            FieldInfo height = AccessTools.Field(typeof(Vector3), nameof(Vector3.y));
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction code = codes[i];
                yield return code;
                if (code.opcode == OpCodes.Ldc_R4 && Equals(code.operand, CeAccess.SuppressionRadius) && i > 0 &&
                    codes[i - 1].opcode == OpCodes.Ldfld && Equals(codes[i - 1].operand, height))
                {
                    // CE's suppression radius is above the launch floor. Keep
                    // its value, and use its cached map even after an Impact.
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return CodeInstruction.LoadLocal(map.LocalIndex);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SuppressionGeometry), nameof(SuppressionGeometry.HeightLimit)));
                }
            }
        }
    }

    [HarmonyPatch(typeof(ProjectileCE), "ApplySuppression")]
    internal static class ProjectileSuppressionFloorPatch
    {
        private static bool Prefix(ProjectileCE __instance, Pawn pawn) => SuppressionGeometry.SameFloor(__instance, pawn);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo origin = AccessTools.Field(typeof(ProjectileCE), nameof(ProjectileCE.OriginIV3));
            MethodInfo draw = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.DrawPos));
            foreach (CodeInstruction code in instructions)
            {
                if (code.opcode == OpCodes.Ldfld && Equals(code.operand, origin))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(SuppressionGeometry), nameof(SuppressionGeometry.Origin));
                }
                else if (code.Calls(draw))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(FloorCoordinates), nameof(FloorCoordinates.DrawPos));
                }
                yield return code;
            }
        }
    }

    [HarmonyPatch(typeof(ProjectileCE), "RayCastSuppression")]
    internal static class SuppressionSegmentFramePatch
    {
        private static bool Prefix(ProjectileCE __instance, ref IntVec3 muzzle, ref IntVec3 destination, ref Map map)
        {
            map ??= LaserRayFrame.Current?.Map ?? __instance.Map;
            return SuppressionGeometry.Segment(__instance, map, ref muzzle, ref destination);
        }
    }

    [HarmonyPatch(typeof(CompSuppressable), nameof(CompSuppressable.AddSuppression))]
    internal static class SuppressionRecipientFloorPatch
    {
        private static bool Prefix(CompSuppressable __instance, IntVec3 origin)
        {
            Map map = __instance.parent.Map;
            return !IsoFloor.HasFloors(map) || !origin.IsValid ||
                IsoFloor.FloorOf(origin, map) == IsoFloor.FloorOf(__instance.parent.Position, map);
        }
    }

    [HarmonyPatch(typeof(SuppressionUtility), "GetCellCoverRatingForPawn")]
    internal static class SuppressionCoverFloorPatch
    {
        private static bool Prefix(Pawn pawn, IntVec3 cell, ref float __result)
        {
            if (!IsoFloor.HasFloors(pawn.Map) || IsoFloor.FloorOf(cell, pawn.Map) == IsoFloor.FloorOf(pawn.Position, pawn.Map)) return true;
            __result = -1000f; // CE's existing invalid-cover score.
            return false;
        }
    }
}
