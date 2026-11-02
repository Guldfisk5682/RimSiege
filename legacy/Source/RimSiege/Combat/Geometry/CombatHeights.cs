using CombatExtended;
using RimSiege.Integration;
using Verse;

namespace RimSiege.Combat.Geometry
{
    internal static class CombatHeights
    {
        // CE ballistic units; IsoRim's visual storey height is a separate scale.
        public const float SlabClearanceCe = 0.02f;
        public const float StoreyCe = CollisionVertical.WallCollisionHeight + SlabClearanceCe;

        public static float Base(IntVec3 cell, Map map) => IsoFloor.FloorOf(cell, map) * StoreyCe;

        public static float TargetTop(ref CollisionVertical vertical, Thing target) => vertical.Max +
            (target != null && IsoFloor.HasFloors(target.Map) ? Base(target.Position, target.Map) : 0f);

        // ProjectZ's occlusion helpers use wall=1, storey=1.02. Preserve their
        // wall/slab proportions while CE owns the physical trajectory units.
        public static float ToIsoOcclusion(float height)
        {
            int floor = UnityEngine.Mathf.FloorToInt(height / StoreyCe);
            float local = height - floor * StoreyCe;
            float wall = CollisionVertical.WallCollisionHeight;
            return floor * ProjectZ.Combat.Heights.Storey + (local <= wall
                ? local / wall : ProjectZ.Combat.Heights.Wall + local - wall);
        }
    }
}
