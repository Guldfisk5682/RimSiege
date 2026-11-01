using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ProjectZ;
using ProjectZ.Patches;
using Verse;

namespace RimSiege.Integration
{
    // Generic Walkable requires both normal and fence-blocked grids in 1.7.
    // Iso builds its vertical graph on the normal grid; traveller restrictions
    // remain in StairGrid.MayTake and the native region/path queries.
    [HarmonyPatch]
    internal static class IsoLadderTraversal
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LadderSite), nameof(LadderSite.Find));
            yield return AccessTools.Method(typeof(LadderSite), nameof(LadderSite.Spawn));
            yield return AccessTools.Method(typeof(StairGrid), nameof(StairGrid.UsableIn));
            yield return AccessTools.Method(typeof(StairTouch), nameof(StairTouch.UpperCells));
            yield return AccessTools.Method(typeof(RaidVertical), nameof(RaidVertical.ClearableTop));
            yield return AccessTools.Method(typeof(LadderPerch), nameof(LadderPerch.MapComponentTick));
            foreach (string name in new[] { "MakeSite", "HopOk", "BlockerSitesWithTopIn", "Extras" })
                yield return AccessTools.Method(typeof(ClimbPlanner), name);
            foreach (MethodInfo method in typeof(JobDriver_LadderPerch).GetNestedTypes(AccessTools.all)
                .SelectMany(AccessTools.GetDeclaredMethods))
                if (method.Name.StartsWith("<MakeNewToils>")) yield return method;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(GenGrid), nameof(GenGrid.Walkable));
            MethodInfo normal = AccessTools.Method(typeof(GenGrid), nameof(GenGrid.WalkableByNormal));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = normal;
                }
                yield return instruction;
            }
        }
    }
}
