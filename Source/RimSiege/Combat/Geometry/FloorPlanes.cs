using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Geometry
{
    internal struct PlaneContact
    {
        public bool Found;
        public Vector3 Point;
        public IntVec3 Cell;
    }

    internal static class FloorPlanes
    {
        internal static PlaneContact Find(Map map, Vector3 from, Vector3 to)
        {
            PlaneContact contact = default;
            Vector3 delta = to - from;
            if (delta.y == 0f) return contact;
            int low = Mathf.Max(IsoFloor.MinLevel(map) + 1, Mathf.CeilToInt(Mathf.Min(from.y, to.y) / CombatHeights.StoreyCe));
            int high = Mathf.Min(IsoFloor.MaxLevel(map) + 1, Mathf.FloorToInt(Mathf.Max(from.y, to.y) / CombatHeights.StoreyCe));
            bool ascending = delta.y > 0f;
            int step = ascending ? 1 : -1;
            for (int k = ascending ? low : high; k >= low && k <= high; k += step)
            {
                float height = k * CombatHeights.StoreyCe;
                float t = (height - from.y) / delta.y;
                if (t <= 0f || t > 1f) continue;
                Vector3 point = from + delta * t;
                point.y = height;
                IntVec3 ground = point.ToIntVec3();
                ground.y = 0;
                if (!IsoFloor.GroundRect(map).Contains(ground) || !IsoFloor.PlaneSolid(ground, k, map)) continue;
                bool slab = IsoFloor.HasLevel(map, k) && IsoFloor.FloorAt(ground, k, map);
                return new PlaneContact { Found = true, Point = point, Cell = IsoFloor.CellOnFloor(ground, slab ? k : k - 1, map) };
            }
            return contact;
        }
    }
}
