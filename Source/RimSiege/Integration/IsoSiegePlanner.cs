using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ProjectZ;
using ProjectZ.Patches;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.Integration
{
    // The existing Iso planner, with a separate walk-only connectivity graph.
    // Its site generation, A*, costs, shared plans and job construction stay native.
    internal sealed partial class IsoSiegePlanner
    {
        [ThreadStatic] internal static IsoSiegePlanner Active;
        internal readonly Map Map;
        internal readonly RouteGraph Graph;
        private readonly ClimbPlans placingPlans, existingPlans;
        private bool allowNewLadders = true;
        internal ClimbPlans Plans => allowNewLadders ? placingPlans : existingPlans;
        internal Pawn Traveller;
        private readonly CacheImage cache = new CacheImage();
        private readonly Func<int> epoch;
        private RouteGraph main;
        private int synchronizedEpoch = -1;
        private bool checkingRoute, selectingTarget;
        private IntVec3 reachableTarget;
        private delegate Thing TargetPicker(Pawn pawn, Lord lord, RouteGraph graph, int startComp, ClimbPlans plans, out bool anyReachable);
        private static readonly TargetPicker PickTarget = AccessTools.MethodDelegate<TargetPicker>(AccessTools.Method(typeof(RaidClimb), "PickTarget"));

        internal IsoSiegePlanner(Map map, Func<int> epoch, Func<Thing, bool> canClearTop)
        {
            Map = map;
            this.epoch = epoch;
            Graph = new RouteGraph(map);
            placingPlans = new ClimbPlans(map);
            existingPlans = new ClimbPlans(map);
            this.canClearTop = canClearTop;
        }

        internal int Version => RouteGraph.For(Map).anyVersion + epoch();

        private void Synchronize(Pawn pawn)
        {
            if (Map.regionDirtyer.AnyDirty) Map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
            main = RouteGraph.For(Map);
            if (synchronizedEpoch != epoch())
            {
                Graph.ClearReach2();
                synchronizedEpoch = epoch();
            }
            Graph.anyVersion = main.anyVersion + epoch();
            Graph.stairsVersion = main.stairsVersion;
            Traveller = pawn;
        }

        internal IntVec3 Destination(Pawn pawn, bool withoutClimbing)
        {
            Synchronize(pawn);
            IsoSiegePlanner previous = Active;
            Active = this;
            try
            {
                IntVec3 destination = pawn.mindState?.duty != null ? pawn.mindState.duty.focus.Cell : IntVec3.Invalid;
                if (!destination.IsValid)
                {
                    Lord lord = pawn.GetLord();
                    int startComp = ClimbPlanner.CompAt(Graph, pawn.Position, Map);
                    if (lord == null || startComp == int.MinValue) return IntVec3.Invalid;
                    reachableTarget = IntVec3.Invalid;
                    selectingTarget = true;
                    Thing target = PickTarget(pawn, lord, Graph, startComp, Plans, out bool anyReachable);
                    selectingTarget = false;
                    destination = anyReachable ? reachableTarget : target?.Position ?? IntVec3.Invalid;
                }
                if (!destination.IsValid) return IntVec3.Invalid;
                checkingRoute = true;
                TraverseParms traversal = TraverseParms.For(pawn);
                bool reached = withoutClimbing
                    ? main.FloorReach(pawn.Position, destination, PathEndMode.Touch, traversal)
                    : Map.reachability.CanReach(pawn.Position, destination, PathEndMode.Touch, traversal);
                return reached ? destination : IntVec3.Invalid;
            }
            finally { selectingTarget = checkingRoute = false; Active = previous; }
        }

        internal Job JobFor(Pawn pawn, bool placing = true)
        {
            Synchronize(pawn);
            IsoSiegePlanner previous = Active;
            CacheImage native = new CacheImage();
            native.Capture();
            cache.Apply();
            bool vertical = ClimbPlanner.NoVertical;
            bool previousPlacing = allowNewLadders;
            allowNewLadders = placing;
            // Reuse native blocked-top ladders. Clearance bindings restrict
            // their targets and leave ceiling excavation disabled.
            ClimbPlanner.NoVertical = false;
            Active = this;
            try { return RaidClimb.TryGive(pawn, false, false); }
            finally
            {
                cache.Capture();
                native.Apply();
                ClimbPlanner.NoVertical = vertical;
                allowNewLadders = previousPlacing;
                Active = previous;
            }
        }

        // Iso stores two geometry caches statically. Preserve their native
        // contents while the stricter planner uses its own entries. The readonly
        // dictionary objects and the search algorithm themselves are unchanged.
        private sealed class CacheImage
        {
            private static readonly FieldInfo[] Dictionaries = { Field("rims"), Field("compSites") };
            private static readonly FieldInfo[] Values = { Field("rimMap"), Field("compSitesMap"), Field("compSitesFor") };
            private readonly List<DictionaryEntry>[] entries = { new List<DictionaryEntry>(), new List<DictionaryEntry>() };
            private readonly object[] values = { null, null, -1 };
            private static FieldInfo Field(string name) => AccessTools.Field(typeof(ClimbPlanner), name);

            internal void Capture()
            {
                for (int i = 0; i < Dictionaries.Length; i++)
                {
                    entries[i].Clear();
                    foreach (DictionaryEntry pair in (IDictionary)Dictionaries[i].GetValue(null)) entries[i].Add(pair);
                }
                for (int i = 0; i < Values.Length; i++) values[i] = Values[i].GetValue(null);
            }

            internal void Apply()
            {
                for (int i = 0; i < Dictionaries.Length; i++)
                {
                    IDictionary dictionary = (IDictionary)Dictionaries[i].GetValue(null);
                    dictionary.Clear();
                    foreach (DictionaryEntry pair in entries[i]) dictionary.Add(pair.Key, pair.Value);
                }
                for (int i = 0; i < Values.Length; i++) Values[i].SetValue(null, values[i]);
            }
        }

        [HarmonyPatch(typeof(RouteGraph), nameof(RouteGraph.For))]
        private static class GraphBinding
        {
            private static void Postfix(Map map, ref RouteGraph __result)
            {
                if (Active != null && !Active.checkingRoute && Active.Map == map) __result = Active.Graph;
            }
        }

        [HarmonyPatch(typeof(RaidClimb), "PickTarget")]
        private static class ReachableTargetBinding
        {
            private static int Component(RouteGraph graph, IntVec3 cell, Map map, int startComp)
            {
                int component = ClimbPlanner.CompAt(graph, cell, map);
                // Iso returns only a flag when it finds a reachable target.
                if (Active != null && Active.selectingTarget && Active.Graph == graph && Active.Map == map && component == startComp)
                    Active.reachableTarget = cell;
                return component;
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo compAt = AccessTools.Method(typeof(ClimbPlanner), nameof(ClimbPlanner.CompAt));
                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.Calls(compAt))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_3).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                        instruction.operand = AccessTools.Method(typeof(ReachableTargetBinding), nameof(Component));
                    }
                    yield return instruction;
                }
            }
        }

        [HarmonyPatch(typeof(ClimbPlans), nameof(ClimbPlans.For))]
        private static class PlanBinding
        {
            private static void Postfix(Map map, ref ClimbPlans __result)
            {
                if (Active != null && Active.Map == map) __result = Active.Plans;
            }
        }

        [HarmonyPatch(typeof(RouteGraph), nameof(RouteGraph.LevelVersion))]
        private static class VersionBinding
        {
            private static bool Prefix(RouteGraph __instance, int L, ref int __result)
            {
                if (Active == null || __instance != Active.Graph) return true;
                __result = Active.main.LevelVersion(L) + Active.epoch();
                return false;
            }
        }

        [HarmonyPatch(typeof(RouteGraph), "DigPassable")]
        private static class WalkableBinding
        {
            private static bool Prefix(RouteGraph __instance, IntVec3 c, PathGrid pg, ref bool __result)
            {
                if (Active == null || __instance != Active.Graph) return true;
                Building_Door door = c.GetEdifice(Active.Map) as Building_Door;
                __result = pg.WalkableFast(c) && (door == null || door.FreePassage || door.PawnCanOpen(Active.Traveller));
                return false;
            }
        }

        [HarmonyPatch]
        private static class TraversalBinding
        {
            private static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(TraverseParms))
                .Where(m => m.Name == nameof(TraverseParms.For));

            private static void Postfix(ref TraverseParms __result)
            {
                if (Active == null) return;
                __result.pawn = Active.Traveller;
                __result.mode = TraverseMode.ByPawn;
                __result.canBashDoors = false;
                __result.canBashFences = false;
            }
        }
    }
}
