using System;
using System.Collections.Generic;
using CombatExtended;
using CombatExtended.Lasers;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Projectiles
{
    // One invocation of CE's native RayCast. CE still owns its stepping,
    // spread, interception chances, damage falloff and Impact calls.
    internal sealed class LaserRayState
    {
        internal readonly ProjectileCE Projectile;
        internal readonly Map Map;
        internal readonly float Range;
        internal readonly List<Thing> Candidates = new List<Thing>();
        internal readonly Comparison<Thing> Order;
        internal Ray Ray;
        internal PlaneContact Surface;
        internal float Limit, SampleDistance;
        internal bool Initialized, CandidateHit, HasEnd, NativeHit;
        internal Vector3 Contact, End, Muzzle;

        internal LaserRayState(ProjectileCE projectile, float range)
        {
            Projectile = projectile;
            Map = projectile.Map;
            Range = range;
            if (projectile.intendedTarget.IsValid)
            {
                IntVec3 source = CeProjectileOrigins.Of(projectile);
                IntVec3 target = SiegeLadderGeometry.TryGet(projectile.intendedTarget.Thing, out SiegeLadderShape ladder)
                    ? ladder.RangeCell(source) : projectile.intendedTarget.Cell;
                Range = IsoFloor.Reach(range, IsoFloor.FloorOf(source, Map), IsoFloor.FloorOf(target, Map));
            }
            Order = Compare;
        }

        internal void Initialize(Ray ray)
        {
            Initialized = true;
            Ray = ray;
            Limit = Range;
            Surface = FloorPlanes.Find(Map, ray.origin, ray.GetPoint(Range));
            float bottom = IsoFloor.MinLevel(Map) * CombatHeights.StoreyCe;
            if (ray.direction.y < 0f)
            {
                float distance = (bottom - ray.origin.y) / ray.direction.y;
                Vector3 point = ray.GetPoint(distance);
                IntVec3 ground = point.ToIntVec3();
                ground.y = 0;
                if (distance > 0f && distance <= Range && IsoFloor.GroundRect(Map).Contains(ground) &&
                    (!Surface.Found || distance < (Surface.Point - ray.origin).magnitude))
                    Surface = new PlaneContact { Found = true, Point = point,
                        Cell = IsoFloor.CellOnFloor(ground, IsoFloor.MinLevel(Map), Map) };
            }
            if (Surface.Found) Limit = (Surface.Point - ray.origin).magnitude;
        }

        internal int Compare(Thing a, Thing b)
        {
            CeAccess.BoundsFor(a).IntersectRay(Ray, out float da);
            CeAccess.BoundsFor(b).IntersectRay(Ray, out float db);
            return da.CompareTo(db);
        }
    }

    internal static class LaserRayFrame
    {
        [ThreadStatic] internal static LaserRayState Current;

        public static float Range(VerbProperties props, ProjectileCE projectile)
        {
            LaserRayState shot = Current;
            if (shot?.Projectile != projectile) return props.range;
            return shot.Initialized && shot.Surface.Found ? Mathf.Min(shot.Range, Mathf.Ceil(shot.Limit) + 1f) : shot.Range;
        }

        public static Vector3 Point(ref Ray ray, float distance, ProjectileCE projectile)
        {
            LaserRayState shot = Current;
            if (shot?.Projectile != projectile) return ray.GetPoint(distance);
            bool first = !shot.Initialized;
            if (first) shot.Initialize(ray);
            shot.CandidateHit = false;
            shot.SampleDistance = Mathf.Min(distance, shot.Limit);
            Vector3 point = ray.GetPoint(shot.SampleDistance);
            if (first) shot.Muzzle = point;
            return point;
        }

        public static bool InBounds(IntVec3 cell, Map map) => IsoFloor.HasFloors(map)
            ? IsoFloor.GroundRect(map).Contains(new IntVec3(cell.x, 0, cell.z)) : cell.InBounds(map);

        public static bool InBoundsRef(ref IntVec3 cell, Map map) => InBounds(cell, map);

        public static List<Thing> Things(ThingGrid grid, IntVec3 ground, ProjectileCE projectile)
        {
            LaserRayState shot = Current;
            if (shot?.Projectile != projectile) return grid.ThingsListAtFast(ground);
            shot.Candidates.Clear();
            Vector3 start = shot.Ray.GetPoint(Mathf.Max(0f, shot.SampleDistance - 1f));
            Vector3 end = shot.Ray.GetPoint(shot.SampleDistance);
            // CE accepts an intersection up to one step beyond this sample.
            // Include that height interval: an ascending ray can intersect an
            // upper-floor body while the sampled point is still below its slab.
            float height = Mathf.Max(start.y, shot.Ray.GetPoint(Mathf.Min(shot.SampleDistance + 1f, shot.Limit)).y);
            int high = Mathf.Min(IsoFloor.MaxLevel(shot.Map), Mathf.FloorToInt(height / CombatHeights.StoreyCe));
            // A floor contact can shorten the last native step. Reuse CE's cell
            // walk so its intervening columns are still queried before Impact.
            ground.y = 0;
            IntVec3 previous = start.ToIntVec3();
            previous.y = 0;
            if (previous != ground)
                foreach (IntVec3 column in CeAccess.Points(start, end))
                    AddColumn(grid, new IntVec3(column.x, 0, column.z), high, shot);
            AddColumn(grid, ground, high, shot);
            shot.Candidates.Sort(shot.Order);
            return shot.Candidates;
        }

        private static void AddColumn(ThingGrid grid, IntVec3 ground, int high, LaserRayState shot)
        {
            if (!IsoFloor.GroundRect(shot.Map).Contains(ground)) return;
            for (int level = IsoFloor.MinLevel(shot.Map); level <= high; level++)
                foreach (Thing thing in grid.ThingsListAtFast(IsoFloor.CellOnFloor(ground, level, shot.Map)))
                    if (!shot.Candidates.Contains(thing) &&
                        CeAccess.BoundsFor(thing).IntersectRay(shot.Ray, out float distance) &&
                        distance <= shot.SampleDistance + 1f && distance <= shot.Limit)
                        shot.Candidates.Add(thing);
        }

        public static bool Intersects(ref Bounds bounds, Ray ray, out float distance, ProjectileCE projectile)
        {
            bool hit = bounds.IntersectRay(ray, out distance);
            LaserRayState shot = Current;
            if (shot?.Projectile != projectile) return hit;
            hit = hit && distance <= shot.SampleDistance + 1f && distance <= shot.Limit;
            shot.CandidateHit = hit;
            if (hit) shot.Contact = ray.GetPoint(distance);
            return hit;
        }

        public static void SetPosition(ProjectileCE projectile, Vector3 point)
        {
            LaserRayState shot = Current;
            if (shot?.Projectile == projectile)
            {
                shot.NativeHit = shot.CandidateHit;
                point = shot.CandidateHit ? shot.Contact : point;
                shot.End = point;
                shot.HasEnd = true;
            }
            CeAccess.SetExact(projectile, point);
        }

        public static void SetLast(ProjectileCE projectile, Vector3 point) => CeAccess.Last(projectile) =
            Current?.Projectile == projectile && Current.HasEnd ? Current.End : point;

        public static void SpawnBeam(LaserBeamCE beam, Vector3 source, Vector3 end)
        {
            LaserRayState shot = Current;
            if (shot?.Projectile == beam)
            {
                end = shot.HasEnd ? shot.End : shot.Surface.Found ? shot.Surface.Point : end;
                shot.End = end;
                shot.HasEnd = true;
                CeAccess.SetExact(beam, end);
                CeAccess.Last(beam) = source;
                if (shot.Surface.Found && !shot.NativeHit && (end - shot.Surface.Point).sqrMagnitude < 0.0001f)
                {
                    beam.Position = shot.Surface.Cell;
                    beam.landed = true;
                }
            }
            CeAccess.SpawnLaser(beam, source, end);
        }

        public static void Finish(ProjectileCE projectile, DestroyMode mode)
        {
            LaserRayState shot = Current;
            if (shot?.Projectile == projectile && shot.Surface.Found && !shot.NativeHit && projectile is LaserBeamCE beam)
                CeAccess.LaserImpact(beam, null, shot.Muzzle);
            else projectile.Destroy(mode);
        }
    }
}
