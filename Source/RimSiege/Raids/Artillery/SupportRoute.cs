using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimSiege.Integration;
using RimWorld;
using Verse;

namespace RimSiege.Raids.Artillery
{
    // Ephemeral snapshots: these are rebuilt from the native path after load.
    internal sealed class SupportRoute
    {
        private static readonly ConditionalWeakTable<ArtilleryRaid, SupportRoute> Routes = new ConditionalWeakTable<ArtilleryRaid, SupportRoute>();
        internal readonly Map Map;
        internal readonly IntVec3 Goal;
        internal readonly List<RouteSegmentSnapshot> Segments;
        private readonly HashSet<IntVec3> cells = new();
        private List<Thing> targets;

        private SupportRoute(AssaultRouteSnapshot snapshot)
        {
            Map = snapshot.Map; Goal = snapshot.Goal; Segments = snapshot.Segments;
            foreach (RouteSegmentSnapshot segment in Segments) cells.UnionWith(segment.Nodes);
        }

        private static SupportRoute Wrap(AssaultRouteSnapshot snapshot) => snapshot == null ? null : new SupportRoute(snapshot);

        internal static SupportRoute For(ArtilleryRaid raid) =>
            raid != null && Routes.TryGetValue(raid, out SupportRoute route) ? route : null;

        internal static SupportRoute Refresh(Map map, ArtilleryRaid raid)
        {
            SupportRoute route = For(raid);
            if (route == null) return Restore(map, raid);
            if (raid.Kind != ArtilleryRaidKind.Breaching) return route;
            var data = BreachingUtility.LordDataFor(raid.AssaultLord);
            RouteSegmentSnapshot actual = VanillaBreachRoutes.Snapshot(data?.breachingGrid);
            if (data != null && data.breachDest.IsValid && data.breachDest != route.Goal) return Restore(map, raid);
            if (actual != null && !route.Segments.Contains(actual)) return Restore(map, raid);
            return route;
        }
        internal static void Set(ArtilleryRaid raid, SupportRoute route)
        {
            Routes.Remove(raid);
            if (route != null)
            {
                route.CacheTargets();
                Routes.Add(raid, route);
            }
        }

        internal static void Invalidate(ArtilleryRaid raid)
        {
            if (raid != null) Routes.Remove(raid);
        }

        internal static bool Affects(ArtilleryRaid raid, IntVec3 cell)
        {
            return raid != null && Routes.TryGetValue(raid, out SupportRoute route) && route.Affects(cell);
        }

        internal bool Affects(IntVec3 cell) => cells.Contains(cell);

        internal static SupportRoute Restore(Map map, ArtilleryRaid raid)
        {
            if (raid?.Camp == null || !raid.Camp.Goal.InBounds(map)) return null;
            Pawn traveller = null;
            // Once the assault detaches, its real members are the route source,
            // not an operator who remains behind at the camp.
            IEnumerable<Pawn> source = raid.AssaultLord != null ? raid.AssaultLord.ownedPawns : raid.Members;
            foreach (Pawn pawn in source)
                if (pawn != null && pawn.Spawned && pawn.Map == map && !pawn.Downed) { traveller = pawn; break; }
            var data = BreachingUtility.LordDataFor(raid.AssaultLord);
            RouteSegmentSnapshot actual = VanillaBreachRoutes.Snapshot(data?.breachingGrid);
            // A breach lord chooses its own native destination after the provisional
            // camp plan. Once available it owns the actual assault route.
            IntVec3 goal = data != null && data.breachDest.IsValid ? data.breachDest : raid.Camp.Goal;
            raid.Camp.Goal = goal;
            SupportRoute route = Wrap(IsoAssaultRoutes.Build(map, traveller, goal, actual, raid.Kind == ArtilleryRaidKind.Ordinary));
            Set(raid, route);
            return route;
        }

        internal static SupportRoute Build(Map map, IntVec3 entry, IntVec3 goal, Pawn traveller = null)
        {
            if (traveller != null) return Wrap(IsoAssaultRoutes.Build(map, traveller, goal, noVerticalWork: true));
            RouteSegmentSnapshot segment = VanillaBreachRoutes.Preplan(map, entry, goal);
            if (segment == null) return null;
            var snapshot = new AssaultRouteSnapshot(map, goal);
            snapshot.Segments.Add(segment);
            return Wrap(snapshot);
        }

        private void CacheTargets()
        {
            if (targets != null) return;
            targets = new List<Thing>();
            var seen = new HashSet<Thing>();
            foreach (RouteSegmentSnapshot segment in Segments)
                foreach (IntVec3 cell in segment.Nodes)
                {
                    if (!cell.InBounds(Map)) continue;
                    Building building = cell.GetEdifice(Map);
                    if (building != null && building.Spawned && !building.Destroyed &&
                        building.Faction == Faction.OfPlayer &&
                        (building.def.IsDoor || building.def.passability == Traversability.Impassable) &&
                        BreachingUtility.ShouldBreachBuilding(building) && seen.Add(building))
                        targets.Add(building);
                }
        }

        internal IEnumerable<Thing> Targets()
        {
            CacheTargets();
            foreach (Thing target in targets)
                if (target.Spawned && !target.Destroyed && target.Map == Map && BreachingUtility.ShouldBreachBuilding(target))
                    yield return target;
        }

        internal CampSiting PlanningTargets() => new CampSiting(new ArtilleryTargets(Map, Goal, this), this);
    }

}
