using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimSiege.Integration;
using RimWorld;
using Verse;

namespace RimSiege.Raids.Artillery
{
    // Keep vanilla's terrain, colony-distance, roof and reachability checks.
    // Only order its bounded random sample by distance from this raid's entry.
    [HarmonyPatch]
    internal static class CampEntrySearch
    {
        private sealed class Search
        {
            internal IntVec3 Entry;
            internal Map Map;
            internal List<IntVec3> Cells;
            internal int Next;
        }

        [ThreadStatic] private static Search current;

        internal static IntVec3 Find(IntVec3 entry, Map map)
        {
            Search previous = current;
            current = new Search { Entry = entry, Map = map };
            try { return RCellFinder.FindSiegePositionFrom(entry, map); }
            finally { current = previous; }
        }

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(RCellFinder), "TryFindSiegePosition");

        private static void Prefix()
        {
            if (current != null) { current.Cells = null; current.Next = 0; }
        }

        private static IntVec3 NextCell(ref CellRect rect)
        {
            Search search = current;
            if (search == null) return rect.RandomCell;
            if (search.Cells == null)
            {
                var cells = new HashSet<IntVec3>();
                for (int i = 0; i < 200; i++) cells.Add(rect.RandomCell);
                search.Cells = cells.OrderBy(cell => IsoFloor.HasFloors(search.Map)
                    ? IsoFloor.RangeDistance(search.Entry, cell, search.Map)
                    : (cell - search.Entry).LengthHorizontal).ToList();
            }
            return search.Next < search.Cells.Count ? search.Cells[search.Next++] : rect.RandomCell;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo random = AccessTools.PropertyGetter(typeof(CellRect), nameof(CellRect.RandomCell));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(random))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(CampEntrySearch), nameof(NextCell));
                }
                yield return instruction;
            }
        }
    }
}
