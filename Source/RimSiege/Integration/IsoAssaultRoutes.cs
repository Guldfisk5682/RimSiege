using ProjectZ;
using ProjectZ.Patches;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege.Integration
{
    internal static class IsoAssaultRoutes
    {
        // This API deliberately requires a real, spawned traveller: Iso's planner uses
        // pawn position, traversal and capabilities throughout its search.
        internal static AssaultRouteSnapshot Build(Map map, Pawn traveller, IntVec3 goal, RouteSegmentSnapshot actual = null, bool noVerticalWork = false)
        {
            if (traveller == null || !traveller.Spawned || traveller.Map != map || !goal.InBounds(map)) return null;
            RouteGraph graph = RouteGraph.For(map);
            if (graph == null)
            {
                var groundRoute = new AssaultRouteSnapshot(map, goal);
                if (actual != null && actual.End == goal) groundRoute.Segments.Add(actual);
                else if (actual != null || !AddLeg(map, groundRoute, traveller.Position, goal)) return null;
                return groundRoute;
            }
            int startComp = ClimbPlanner.CompAt(graph, traveller.Position, map);
            int goalComp = ClimbPlanner.CompAt(graph, goal, map);
            var route = new AssaultRouteSnapshot(map, goal);
            if (startComp == int.MinValue || goalComp == int.MinValue)
            {
                if (actual == null) return null;
                route.Segments.Add(actual);
                return route;
            }
            IntVec3 start = traveller.Position;
            ClimbPlanner.Plan plan = null;
            if (startComp != goalComp)
            {
                bool previous = ClimbPlanner.NoVertical;
                ClimbPlanner.NoVertical = noVerticalWork;
                try { plan = ClimbPlanner.Find(map, graph, traveller, startComp, false, goal); }
                finally { ClimbPlanner.NoVertical = previous; }
            }
            if (startComp != goalComp && plan == null)
            {
                if (actual == null) return null;
                route.Segments.Add(actual);
                return route;
            }
            if (actual != null && actual.End != goal &&
                (plan == null || plan.hops.Count == 0 || actual.End != plan.hops[0].Stand))
            {
                // The native lead can have chosen a different first hop. Keep its
                // actual segment; do not attach predictions based on another lead.
                route.Segments.Add(actual);
                return route;
            }
            if (plan != null)
                foreach (ClimbPlanner.Hop hop in plan.hops)
                {
                    if (actual != null && actual.End == hop.Stand)
                    { route.Segments.Add(actual); actual = null; }
                    else if (!AddLeg(map, route, start, hop.Stand)) return null;
                    start = hop.up ? hop.top : hop.foot;
                }
            if (actual != null && actual.End == goal) route.Segments.Add(actual);
            else if (actual != null) return null;
            else if (!AddLeg(map, route, start, goal)) return null;
            return route;
        }

        private static bool AddLeg(Map map, AssaultRouteSnapshot route, IntVec3 start, IntVec3 end)
        {
            if (start == end) return true;
            using (PawnPath path = map.pathFinder.FindPathNow(start, end,
                TraverseParms.For(TraverseMode.PassAllDestroyableThings, Danger.Deadly), null, PathEndMode.OnCell))
            {
                RouteSegmentSnapshot segment = RouteSegmentSnapshot.FromPath(start, end, true, path);
                if (segment == null) return false;
                route.Segments.Add(segment);
                return true;
            }
        }
    }
}
