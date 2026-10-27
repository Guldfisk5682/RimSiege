using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Projectiles
{
    [HarmonyPatch(typeof(ProjectileCE), "CheckIntercept")]
    internal static class CeShieldCenterFramePatch
    {
        public static Vector3 Center(ref IntVec3 cell, float altitude, ProjectileCE projectile)
        {
            Vector3 point = cell.ToVector3ShiftedWithAltitude(altitude);
            if (!IsoFloor.HasFloors(projectile.Map)) return point;
            point = FloorCoordinates.Ground(point, cell, projectile.Map);
            point.y += CombatHeights.Base(cell, projectile.Map);
            return point;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo position = AccessTools.Method(typeof(IntVec3), nameof(IntVec3.ToVector3ShiftedWithAltitude), new[] { typeof(float) });
            MethodInfo center = AccessTools.Method(typeof(GenThing), nameof(GenThing.TrueCenter), new[] { typeof(Thing) });
            foreach (CodeInstruction code in instructions)
            {
                if (code.Calls(position))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code).MoveBlocksFrom(code);
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(CeShieldCenterFramePatch), nameof(Center));
                }
                else if (code.Calls(center)) code.operand = AccessTools.Method(typeof(FloorCoordinates), nameof(FloorCoordinates.TrueCenter));
                yield return code;
            }
        }
    }
}
