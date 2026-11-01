using CombatExtended;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Aiming
{
    internal enum FixedObstruction { Unavailable, None, MapEdge, Building, FloorPlane }

    internal readonly record struct FixedCorridorResult(FixedObstruction Obstruction, IntVec3 Cell)
    {
        internal bool Clear => Obstruction == FixedObstruction.None;
    }

    // Deployment asks only about fixed geometry. Ammunition readiness, pawns,
    // suppression, partial cover and final shot eligibility remain CE's decision.
    internal static class FixedShotCorridor
    {
        internal static FixedCorridorResult Trace(Building_TurretGunCE gun, Thing target)
        {
            var verb = (Verb_LaunchProjectileCE)gun.AttackVerb;
            Map map = CeArtillerySight.MapFor(gun);
            Vector3 from = FloorCoordinates.TrueCenter(gun);
            from.y = verb.ShotHeight;
            Vector3 to = FloorCoordinates.DrawPos(target);
            CollisionVertical vertical = new CollisionVertical(target);
            to.y = CombatHeights.TargetTop(ref vertical, target);
            var sight = new SightRay(from, to, verb, target.Position);
            if (sight.HasFloors)
            {
                PlaneContact plane = FloorPlanes.Find(map, from, to);
                if (plane.Found) return new(FixedObstruction.FloorPlane, plane.Cell);
            }
            Vector3 delta = to - from;
            float length = delta.magnitude;
            var ray = new Ray(from, delta);
            foreach (IntVec3 cell in SightQueries.Cells(from, target.Position.ToVector3Shifted(), sight))
            {
                if (!cell.InBounds(map)) return new(FixedObstruction.MapEdge, cell);
                Building obstacle = cell.GetEdifice(map);
                if (obstacle == null || obstacle == target || obstacle == gun ||
                    obstacle.def.Fillage != FillCategory.Full || obstacle is Building_Door { Open: true }) continue;
                if (CeAccess.BoundsFor(obstacle).IntersectRay(ray, out float distance) && distance < length)
                    return new(FixedObstruction.Building, cell);
            }
            return new(FixedObstruction.None, IntVec3.Invalid);
        }
    }
}
