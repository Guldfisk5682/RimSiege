using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    internal static class NativeSiegeOwnership
    {
        // Native construction credits the worker's lord. Shared builders must
        // instead credit the planned camp, including workers without a lord.
        private static Lord ConstructionLord(Pawn worker, Frame frame)
        {
            // CompleteConstruction has already destroyed the frame here.
            Map map = worker.Map;
            if (map != null)
                foreach (ArtilleryRaid raid in map.GetComponent<ArtilleryMap>().ActiveRaids)
                {
                    Lord owner = raid.CampLord;
                    if (raid.Camp == null || raid.Faction != frame.Faction || owner == null ||
                        !map.lordManager.lords.Contains(owner) || owner.CurLordToil is not LordToil_Siege) continue;
                    if (!ArtilleryCamp.Placements(raid.Camp).Any(item =>
                        item.Frame == frame ||
                        (item.Cell == frame.Position && item.Def == frame.def.entityDefToBuild))) continue;
                    return owner;
                }
            return worker.GetLord();
        }

        [HarmonyPatch]
        private static class CampFrames
        {
            private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(LordToil_Siege), "Frames");

            private static void Postfix(LordToil_Siege __instance, ref IEnumerable<Frame> __result)
            {
                ArtilleryRaid raid = NativeSiege.Raid(__instance);
                if (raid?.Camp == null) return;
                __result = __result.Where(frame => ArtilleryCamp.Placements(raid.Camp).Any(item =>
                    item.Frame == frame || (item.Cell == frame.Position && item.Def == frame.def.entityDefToBuild)));
            }
        }

        [HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
        private static class CompletedBuilding
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo original = AccessTools.Method(typeof(LordUtility), nameof(LordUtility.GetLord), new[] { typeof(Pawn) });
                MethodInfo replacement = AccessTools.Method(typeof(NativeSiegeOwnership), nameof(ConstructionLord));
                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.Calls(original))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = replacement;
                    }
                    yield return instruction;
                }
            }
        }
    }
}
