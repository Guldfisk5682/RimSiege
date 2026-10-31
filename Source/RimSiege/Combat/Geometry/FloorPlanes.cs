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

    internal enum PlaneQuery { Sight, Contact }

    internal static class FloorPlanes
    {
        private const float GridEdgeTolerance = 0.0001f;
        private const float ProbeSpan = 0.25f;

        // Aim and planning rays may end on the visible top of a floor. A real
        // projectile still makes contact there; its callers opt into Contact.
        internal static PlaneContact Find(Map map, Vector3 from, Vector3 to, PlaneQuery query = PlaneQuery.Sight)
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
                // Iso's directional endpoints: departing a plane downward and
                // arriving from below cross it; leaving upward does not.
                if (t < 0f || t > 1f || t == 0f && ascending ||
                    t == 1f && !ascending && query == PlaneQuery.Sight) continue;
                Vector3 point = from + delta * t;
                point.y = height;
                IntVec3 ground = CrossingCell(map, delta, t, point, k);
                if (!ground.IsValid) continue;
                bool slab = IsoFloor.HasLevel(map, k) && IsoFloor.FloorAt(ground, k, map);
                IntVec3 hitCell = IsoFloor.CellOnFloor(ground, slab ? k : k - 1, map);
                return new PlaneContact { Found = true, Point = point, Cell = hitCell };
            }
            return contact;
        }

        private static IntVec3 CrossingCell(Map map, Vector3 delta, float t, Vector3 point, int plane)
        {
            CellRect bounds = IsoFloor.GroundRect(map);
            bool edge = Mathf.Abs(point.x - Mathf.Round(point.x)) <= GridEdgeTolerance ||
                Mathf.Abs(point.z - Mathf.Round(point.z)) <= GridEdgeTolerance;
            if (edge && IsoPlaneQueries.Available)
            {
                // At most half a cell per axis and half a storey vertically.
                // The common case remains O(floors crossed), without a second
                // walk along the entire CE aim line or projectile tick segment.
                float span = ProbeSpan / Mathf.Max(Mathf.Abs(delta.x),
                    Mathf.Max(Mathf.Abs(delta.z), Mathf.Abs(delta.y) / CombatHeights.StoreyCe));
                float before = Mathf.Min(t, span);
                Vector3 a = point - delta * before;
                Vector3 b = point + delta * span;
                if (bounds.Contains(a.ToIntVec3()) && bounds.Contains(b.ToIntVec3()))
                {
                    // Only this plane is queried. A local linear height change
                    // keeps the exact CE crossing parameter while switching to
                    // Iso cell heights; it does not alter CE's physical ray.
                    float isoPlane = plane * IsoFloor.StoreyHeight;
                    float scale = IsoFloor.StoreyHeight / CombatHeights.StoreyCe;
                    a.y = isoPlane - delta.y * before * scale;
                    b.y = isoPlane + delta.y * span * scale;
                    return IsoPlaneQueries.CrossingCell(map, a, b, plane);
                }
            }
            IntVec3 ground = point.ToIntVec3();
            ground.y = 0;
            return bounds.Contains(ground) && IsoFloor.PlaneSolid(ground, plane, map) ? ground : IntVec3.Invalid;
        }
    }
}
