using RimSiege.Integration;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Geometry
{
    internal static class FloorCoordinates
    {
        // Remove the floor-band offset from x/z only. CE's continuous ballistic
        // height and the thing's real map cell remain separate representations.
        public static Vector3 Ground(Vector3 point, IntVec3 anchor, Map map)
        {
            IntVec3 g = IsoFloor.GroundCellOf(anchor, map);
            point.z -= anchor.z - g.z;
            return point;
        }

        public static Vector3 Ground(Vector3 point, Map map) => Ground(point, point.ToIntVec3(), map);

        public static Vector3 TrueCenter(Thing thing)
        {
            Vector3 p = thing.TrueCenter();
            return IsoFloor.HasFloors(thing.Map) ? Ground(p, thing.Position, thing.Map) : p;
        }

        public static Vector3 DrawPos(Thing thing)
        {
            Vector3 p = thing.DrawPos;
            return IsoFloor.HasFloors(thing.Map) ? Ground(p, thing.Position, thing.Map) : p;
        }

        public static bool InBounds(Vector3 p, Map map) => IsoFloor.HasFloors(map)
            ? IsoFloor.GroundRect(map).Contains(p.ToIntVec3()) : p.InBounds(map);

        public static bool InBoundsRef(ref Vector3 p, Map map) => InBounds(p, map);
    }
}
