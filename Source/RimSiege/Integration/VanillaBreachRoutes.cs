using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace RimSiege.Integration
{
    // Capture the exact path the game is about to widen, before its pool release.
    internal static class VanillaBreachRoutes
    {
        private sealed class Capture { internal RouteSegmentSnapshot Segment; }
        private static readonly ConditionalWeakTable<BreachingGrid, Capture> Captures = new ConditionalWeakTable<BreachingGrid, Capture>();

        internal static RouteSegmentSnapshot Snapshot(BreachingGrid grid) =>
            grid != null && Captures.TryGetValue(grid, out Capture capture) ? capture.Segment : null;

        internal static RouteSegmentSnapshot Preplan(Map map, IntVec3 start, IntVec3 goal)
        {
            if (!start.InBounds(map) || !goal.InBounds(map) ||
                !map.reachability.CanReach(start, goal, PathEndMode.OnCell, TraverseMode.PassAllDestroyableThings, Danger.Deadly)) return null;
            // A temporary native grid produces a provisional support line; it is not
            // represented as the future Lord's actual breach path.
            var grid = new BreachingGrid(map, null);
            grid.CreateBreachPath(start, goal, 1, 2);
            RouteSegmentSnapshot captured = Snapshot(grid);
            return captured == null ? null : new RouteSegmentSnapshot(start, goal, true, captured.Nodes);
        }

        private static void Record(PawnPath path, BreachingGrid grid, IntVec3 start, IntVec3 end)
        {
            Captures.GetValue(grid, _ => new Capture()).Segment = RouteSegmentSnapshot.FromPath(start, end, false, path);
        }

        [HarmonyPatch(typeof(BreachingGrid), nameof(BreachingGrid.Reset))]
        private static class ResetCapture
        {
            private static void Prefix(BreachingGrid __instance) => Captures.Remove(__instance);
        }

        [HarmonyPatch(typeof(BreachingGrid), nameof(BreachingGrid.CreateBreachPath))]
        private static class CapturePath
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo record = AccessTools.Method(typeof(VanillaBreachRoutes), nameof(Record));
                foreach (CodeInstruction instruction in instructions)
                {
                    yield return instruction;
                    if (instruction.operand is MethodInfo method && method.Name == "FindPathNow" && method.ReturnType == typeof(PawnPath))
                    {
                        yield return new CodeInstruction(OpCodes.Dup);
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Ldarg_1);
                        yield return new CodeInstruction(OpCodes.Ldarg_2);
                        yield return new CodeInstruction(OpCodes.Call, record);
                    }
                }
            }
        }
    }
}
