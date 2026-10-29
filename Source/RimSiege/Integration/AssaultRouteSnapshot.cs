using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimSiege.Integration
{
    // Native route data has no raid ownership or target-selection policy.
    internal sealed class AssaultRouteSnapshot
    {
        internal readonly Map Map;
        internal readonly IntVec3 Goal;
        internal readonly List<RouteSegmentSnapshot> Segments = new List<RouteSegmentSnapshot>();

        internal AssaultRouteSnapshot(Map map, IntVec3 goal) { Map = map; Goal = goal; }
    }

    internal sealed class RouteSegmentSnapshot
    {
        internal readonly IntVec3 Start, End;
        internal readonly bool Provisional;
        internal readonly List<IntVec3> Nodes;

        internal RouteSegmentSnapshot(IntVec3 start, IntVec3 end, bool provisional, List<IntVec3> nodes)
        {
            Start = start; End = end; Provisional = provisional; Nodes = nodes;
        }

        internal static RouteSegmentSnapshot FromPath(IntVec3 start, IntVec3 end, bool provisional, PawnPath path)
        {
            if (path == null || !path.Found) return null;
            var nodes = new List<IntVec3>(path.NodesReversed);
            nodes.Reverse();
            return new RouteSegmentSnapshot(start, end, provisional, nodes);
        }
    }
}
