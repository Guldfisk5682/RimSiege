using System.Runtime.CompilerServices;
using CombatExtended;
using Verse;

namespace RimSiege.Integration
{
    internal static class CeProjectileOrigins
    {
        internal sealed class Origin
        {
            public Origin() { }
            public IntVec3 Cell = IntVec3.Invalid;
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
        }
    }
}
