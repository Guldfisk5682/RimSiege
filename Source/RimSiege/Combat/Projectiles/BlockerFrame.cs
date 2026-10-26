using CombatExtended.Compatibility;
using CombatExtended;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Projectiles
{
    // CE walks and sorts ground cells. Map-facing callbacks and overlays need
    // real band cells; continuous segment callbacks keep CE-space endpoints.
    internal static class BlockerFrame
    {
        public static Vector3 Center(Thing thing, ProjectileCE projectile)
        {
            if (SiegeLadderGeometry.TryGet(thing, out SiegeLadderShape ladder))
            {
                // CE already accepted this candidate's bounds. Use that actual
                // ray contact for shields, rather than a line down to the foot.
                Ray ray = CeAccess.ProjectileRay(projectile);
                ladder.Bounds.IntersectRay(ray, out float distance);
                return ray.GetPoint(distance);
            }
            Vector3 center = FloorCoordinates.TrueCenter(thing);
            if (IsoFloor.HasFloors(thing.Map))
                center.y += CombatHeights.Base(thing.Position, thing.Map);
            return center;
        }

        public static IntVec3 GroundCell(Vector3 point, ProjectileCE projectile)
        {
            IntVec3 cell = point.ToIntVec3();
            if (IsoFloor.HasFloors(projectile.Map)) cell.y = 0;
            return cell;
        }

        public static bool CheckCell(ProjectileCE projectile, IntVec3 ground, Thing launcher, Vector3 endpoint)
        {
            Map map = projectile.Map;
            if (!IsoFloor.HasFloors(map))
                return BlockerRegistry.CheckCellForCollisionCallback(projectile, ground, launcher);
            if (!ColumnLevels(projectile, ground, endpoint, out int first, out int last)) return false;
            int step = last >= first ? 1 : -1;
            for (int level = first; ; level += step)
            {
                IntVec3 cell = IsoFloor.CellOnFloor(ground, level, map);
                if (BlockerRegistry.CheckCellForCollisionCallback(projectile, cell, launcher)) return true;
                if (level == last) return false;
            }
        }

        public static void FlashCell(DebugCellDrawer drawer, IntVec3 ground, float colorPct, string text, int duration,
            ProjectileCE projectile, Vector3 endpoint)
        {
            Map map = projectile.Map;
            if (!IsoFloor.HasFloors(map))
            {
                drawer.FlashCell(ground, colorPct, text, duration);
                return;
            }
            if (!ColumnLevels(projectile, ground, endpoint, out int first, out int last)) return;
            int step = last >= first ? 1 : -1;
            for (int level = first; ; level += step)
            {
                drawer.FlashCell(IsoFloor.CellOnFloor(ground, level, map), colorPct, text, duration);
                if (level == last) return;
            }
        }

        private static int Level(float height, Map map) => Mathf.Clamp(
            Mathf.FloorToInt(height / CombatHeights.StoreyCe), IsoFloor.MinLevel(map), IsoFloor.MaxLevel(map));

        private static bool ColumnLevels(ProjectileCE projectile, IntVec3 ground, Vector3 endpoint, out int first, out int last)
        {
            Map map = projectile.Map;
            Vector3 from = CeAccess.Last(projectile);
            first = Level(from.y, map);
            last = Level(endpoint.y, map);
            if (first != last)
            {
                // Use the same cell-center footprint as CE's existing IntVec3
                // GenSight walk, with the unrounded CE heights at its endpoints.
                Vector3 a = from.ToIntVec3().ToVector3Shifted();
                Vector3 b = endpoint.ToIntVec3().ToVector3Shifted();
                a.y = from.y;
                b.y = endpoint.y;
                Ray forward = new Ray(a, b - a), backward = new Ray(b, a - b);
                Bounds column = new Bounds(
                    new Vector3(ground.x + 0.5f, (a.y + b.y) * 0.5f, ground.z + 0.5f),
                    new Vector3(1f, Mathf.Abs(b.y - a.y), 1f));
                if (!column.IntersectRay(forward, out float enter) || !column.IntersectRay(backward, out float exit))
                    return false;
                first = Level(forward.GetPoint(enter).y, map);
                last = Level(backward.GetPoint(exit).y, map);
            }

            // As with MapPosition, only the grid representation is limited to
            // existing levels. Continuous CE height is never clamped or changed.
            // A steep/vertical segment may cross two levels in the same column.
            return true;
        }
    }
}
