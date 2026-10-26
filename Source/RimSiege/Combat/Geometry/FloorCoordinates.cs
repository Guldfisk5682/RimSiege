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
            return Center(p, thing);
        }

        public static Vector3 DrawPos(Thing thing)
        {
            Vector3 p = thing.DrawPos;
            return Center(p, thing);
        }

        private static Vector3 Center(Vector3 point, Thing thing)
        {
            if (!IsoFloor.HasFloors(thing.Map)) return point;
            point = Ground(point, thing.Position, thing.Map);
            if (SiegeLadderGeometry.TryGet(thing, out SiegeLadderShape ladder))
            {
                point.x = ladder.Bounds.center.x;
                point.z = ladder.Bounds.center.z;
            }
            return point;
        }

        public static bool InBounds(Vector3 p, Map map) => IsoFloor.HasFloors(map)
            ? IsoFloor.GroundRect(map).Contains(p.ToIntVec3()) : p.InBounds(map);

        public static bool InBoundsRef(ref Vector3 p, Map map) => InBounds(p, map);

        // A continuous CE point queried in the map band of an actual contact.
        public static IntVec3 ContactCell(Vector3 point, IntVec3 anchor, Map map)
        {
            if (!IsoFloor.HasFloors(map)) return point.ToIntVec3();
            IntVec3 ground = IsoFloor.GroundCellOf(point.ToIntVec3(), map);
            if (!ground.IsValid) return IntVec3.Invalid;
            ground.y = 0;
            return IsoFloor.CellOnFloor(ground, IsoFloor.FloorOf(anchor, map), map);
        }

        public static Vector3 OnBand(Vector3 ground, IntVec3 anchor, Map map)
        {
            IntVec3 cell = IsoFloor.GroundCellOf(anchor, map);
            ground.z += anchor.z - cell.z;
            ground.y = 0f;
            return ground;
        }

        public static IntVec3 GroundPosition(Thing thing) => IsoFloor.HasFloors(thing.Map)
            ? IsoFloor.GroundCellOf(thing.Position, thing.Map) : thing.Position;

        public static float HorizontalDistance(IntVec3 from, IntVec3 to, Map map)
        {
            if (!IsoFloor.HasFloors(map)) return from.DistanceTo(to);
            return IsoFloor.GroundCellOf(from, map).DistanceTo(IsoFloor.GroundCellOf(to, map));
        }
    }
}
