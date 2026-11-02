using System.Runtime.CompilerServices;
using CombatExtended;
using UnityEngine;
using Verse;

namespace RimSiege.Integration
{
    internal static class CeProjectileOrigins
    {
        internal sealed class Origin
        {
            public Origin() { }
            public IntVec3 Cell = IntVec3.Invalid;
            public IntVec3 SurfaceCell = IntVec3.Invalid;
            public Vector3 SurfacePoint;
            public IntVec3 ExplosionCell = IntVec3.Invalid;
            public bool SurfaceFromBelow;
        }

        private static readonly ConditionalWeakTable<ProjectileCE, Origin> Origins = new ConditionalWeakTable<ProjectileCE, Origin>();

        internal static bool TryGet(ProjectileCE projectile, out IntVec3 cell)
        {
            cell = Origins.TryGetValue(projectile, out Origin origin) ? origin.Cell : IntVec3.Invalid;
            return cell.IsValid;
        }

        internal static void Capture(ProjectileCE projectile)
        {
            if (IsoFloor.HasFloors(projectile.Map))
            {
                Origin origin = Origins.GetOrCreateValue(projectile);
                if (!origin.Cell.IsValid) origin.Cell = projectile.Position;
            }
        }

        // A plane reached at an edge can belong to Iso's preceding column,
        // rather than the cell obtained by rounding the exact impact point.
        internal static void RecordSurface(ProjectileCE projectile, IntVec3 cell, Vector3 point,
            IntVec3 explosionCell, bool fromBelow)
        {
            Origin origin = Origins.GetOrCreateValue(projectile);
            origin.SurfaceCell = cell;
            origin.SurfacePoint = point;
            origin.ExplosionCell = explosionCell;
            origin.SurfaceFromBelow = fromBelow;
        }

        internal static bool TrySurface(ProjectileCE projectile, Vector3 point, out IntVec3 cell)
        {
            if (Origins.TryGetValue(projectile, out Origin origin) && origin.SurfaceCell.IsValid && point == origin.SurfacePoint)
            {
                cell = origin.SurfaceCell;
                return true;
            }
            cell = IntVec3.Invalid;
            return false;
        }

        internal static Origin SurfaceOf(ProjectileCE projectile) => projectile != null &&
            Origins.TryGetValue(projectile, out Origin origin) && origin.ExplosionCell.IsValid &&
            CeAccess.ProjectilePosition(projectile) == origin.SurfacePoint ? origin : null;

        internal static IntVec3 Of(ProjectileCE projectile)
        {
            Origin origin = Origins.GetOrCreateValue(projectile);
            // Old saves have no launch record. Their surviving launcher is the
            // native source available for that one in-flight projectile.
            if (!origin.Cell.IsValid)
                origin.Cell = projectile.launcher != null && projectile.launcher.Map == projectile.Map
                    ? projectile.launcher.Position : projectile.Position;
            return origin.Cell;
        }

        internal static void Expose(ProjectileCE projectile)
        {
            Origin origin = Origins.GetOrCreateValue(projectile);
            Scribe_Values.Look(ref origin.Cell, "rimSiegeLaunchCell", IntVec3.Invalid);
            Scribe_Values.Look(ref origin.SurfaceCell, "rimSiegeSurfaceCell", IntVec3.Invalid);
            Scribe_Values.Look(ref origin.SurfacePoint, "rimSiegeSurfacePoint");
            Scribe_Values.Look(ref origin.ExplosionCell, "rimSiegeSurfaceExplosionCell", IntVec3.Invalid);
            Scribe_Values.Look(ref origin.SurfaceFromBelow, "rimSiegeSurfaceFromBelow");
        }
    }
}
