using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using RimSiege.Integration;
using Verse;

namespace RimSiege.Combat.Aiming
{
    [HarmonyPatch(typeof(Building_TurretGunCE), nameof(Building_TurretGunCE.OrderAttack))]
    internal static class TurretOrderFramePatch
    {
        private static float Distance(ref IntVec3 delta, Building_TurretGunCE turret,
            LocalTargetInfo target, bool maximum)
        {
            Map map = turret.Map;
            if (!IsoFloor.HasFloors(map)) return delta.LengthHorizontal;
            // Match the existing CE verb range adapter. CE retains its own
            // min/max thresholds, rejection messages and forced-target handling.
            return maximum ? IsoFloor.RangeDistance(turret.Position, target.Cell, map)
                : IsoFloor.Distance3D(turret.Position, target.Cell, map);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo distance = AccessTools.PropertyGetter(typeof(IntVec3), nameof(IntVec3.LengthHorizontal));
            int check = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(distance))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    // Native OrderAttack tests minimum range, then maximum.
                    yield return new CodeInstruction(check++ == 0 ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1);
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(TurretOrderFramePatch), nameof(Distance));
                }
                yield return instruction;
            }
        }
    }
}
