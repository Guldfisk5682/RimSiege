using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ProjectZ;
using ProjectZ.Patches;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege.Integration
{
    // The existing Iso planner, with a separate no-demolition connectivity graph.
    // Its site generation, A*, costs, shared plans and job construction stay native.
    internal sealed class IsoSiegePlanner
    {
        [ThreadStatic] internal static IsoSiegePlanner Active;
        internal readonly Map Map;
        internal readonly RouteGraph Graph;
        internal readonly ClimbPlans Plans;
        internal Pawn Traveller;
        private readonly CacheImage cache = new CacheImage();
        private readonly Func<int> epoch;
        private RouteGraph main;
        private int synchronizedEpoch = -1;
        private bool checkingRoute, floorOnly, firstComponent;
        private IntVec3 destination;

        internal IsoSiegePlanner(Map map, Func<int> epoch)
        {
            Map = map;
            this.epoch = epoch;
            Graph = new RouteGraph(map);
            Plans = new ClimbPlans(map);
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
            checkingRoute = firstComponent = true;
            floorOnly = withoutClimbing;
            destination = IntVec3.Invalid;
            Active = this;
            try
            {
                RaidDegrade.RealRaidDest(pawn.Position, Map);
                return destination;
            }
            finally { checkingRoute = false; Active = previous; }
        }

        internal Job JobFor(Pawn pawn)
        {
            Synchronize(pawn);
            IsoSiegePlanner previous = Active;
            CacheImage native = new CacheImage();
            native.Capture();
            cache.Apply();
            bool vertical = ClimbPlanner.NoVertical;
            ClimbPlanner.NoVertical = true; // Ordinary ladder troops do not gain sapper/mining abilities.
            Active = this;
            try { return RaidClimb.TryGive(pawn, false, false); }
            finally
            {
                cache.Capture();
                native.Apply();
                ClimbPlanner.NoVertical = vertical;
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

        [HarmonyPatch(typeof(ClimbPlanner), nameof(ClimbPlanner.CompAt))]
        private static class DestinationBinding
        {
            private static bool Prefix(IntVec3 c, ref int __result)
            {
                if (Active == null || !Active.checkingRoute) return true;
                // RealRaidDest owns target enumeration and filters. For this
                // probe its component equality is answered by native reachability,
                // recording the same first destination it accepts.
                if (Active.firstComponent)
                {
                    Active.firstComponent = false;
                    __result = 1;
                    return false;
                }
                Pawn pawn = Active.Traveller;
                TraverseParms traversal = TraverseParms.For(pawn);
                bool reached = Active.floorOnly
                    ? Active.main.FloorReach(pawn.Position, c, PathEndMode.Touch, traversal)
                    : Active.Map.reachability.CanReach(pawn.Position, c, PathEndMode.Touch, traversal);
                __result = reached ? 1 : 0;
                if (reached) Active.destination = c;
                return false;
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
