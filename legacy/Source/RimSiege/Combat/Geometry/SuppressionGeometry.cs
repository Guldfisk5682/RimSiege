using CombatExtended;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Geometry
{
    internal static class SuppressionGeometry
    {
        public static float HeightLimit(float nativeRadius, ProjectileCE projectile, Map map) =>
            nativeRadius + (IsoFloor.HasFloors(map) ? CombatHeights.Base(CeProjectileOrigins.Of(projectile), map) : 0f);

        public static IntVec3 Origin(ProjectileCE projectile) => CeProjectileOrigins.TryGet(projectile, out IntVec3 cell)
            ? cell : IsoFloor.HasFloors(projectile.Map) ? CeProjectileOrigins.Of(projectile) : projectile.OriginIV3;

        internal static bool SameFloor(ProjectileCE projectile, Pawn pawn)
        {
            Map map = projectile.Map ?? pawn?.Map;
            return pawn == null || !IsoFloor.HasFloors(map) ||
                IsoFloor.FloorOf(CeProjectileOrigins.Of(projectile), map) == IsoFloor.FloorOf(pawn.Position, map);
        }

        // Clip only the local suppression segment. CE keeps its own radius,
        // recipient query, armour reduction and buildup/decay calculations.
        internal static bool Segment(ProjectileCE projectile, Map map, ref IntVec3 from, ref IntVec3 to)
        {
            if (!IsoFloor.HasFloors(map)) return true;
            int level = IsoFloor.FloorOf(CeProjectileOrigins.Of(projectile), map);
            Vector3 a = CeAccess.Last(projectile), b = CeAccess.ProjectilePosition(projectile);
            float low = level * CombatHeights.StoreyCe, high = low + CombatHeights.StoreyCe;
            float enter = 0f, leave = 1f, dy = b.y - a.y;
            if (dy == 0f)
            {
                if (a.y < low || a.y >= high) return false;
            }
            else
            {
                float atLow = (low - a.y) / dy, atHigh = (high - a.y) / dy;
                enter = Mathf.Max(0f, Mathf.Min(atLow, atHigh));
                leave = Mathf.Min(1f, Mathf.Max(atLow, atHigh));
                if (leave <= enter) return false;
            }
            IntVec3 ga = Vector3.Lerp(a, b, enter).ToIntVec3(), gb = Vector3.Lerp(a, b, leave).ToIntVec3();
            ga.y = gb.y = 0;
            from = IsoFloor.CellOnFloor(ga, level, map);
            to = IsoFloor.CellOnFloor(gb, level, map);
            return true;
        }
    }
}
