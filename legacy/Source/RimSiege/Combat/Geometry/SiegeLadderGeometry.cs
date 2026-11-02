using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Geometry
{
    internal readonly struct SiegeLadderShape
    {
        // CE units. Keep the collision prism inside the open column, near the
        // edge the ladder leans against; it never occupies the landing's grid cell.
        internal const float Width = 0.7f;
        internal const float Depth = 0.16f;
        internal const float EdgeOffset = 0.4f;
        internal const float HeadExposure = 0.7f;
        private const float AimInset = 0.05f;

        internal readonly Map Map;
        internal readonly IntVec3 Foot, Top;
        internal readonly Bounds Bounds;
        private readonly int bottomLevel, topLevel;

        internal SiegeLadderShape(Map map, IntVec3 foot, IntVec3 top)
        {
            Map = map;
            Foot = foot;
            Top = top;
            bottomLevel = IsoFloor.FloorOf(foot, map);
            topLevel = IsoFloor.FloorOf(top, map);
            IntVec3 ground = IsoFloor.GroundCellOf(foot, map);
            IntVec3 facing = IsoFloor.GroundCellOf(top, map) - ground;
            float bottom = bottomLevel * CombatHeights.StoreyCe;
            float height = (topLevel - bottomLevel) * CombatHeights.StoreyCe + HeadExposure;
            Vector3 center = ground.ToVector3Shifted() + facing.ToVector3() * EdgeOffset;
            center.y = bottom + height * 0.5f;
            Bounds = new Bounds(center, new Vector3(facing.x == 0 ? Width : Depth,
                height, facing.z == 0 ? Width : Depth));
        }

        internal Vector3 AimPoint(IntVec3 source)
        {
            int level = Mathf.Clamp(IsoFloor.FloorOf(source, Map), bottomLevel, topLevel);
            Vector3 point = Bounds.center;
            point.y = level * CombatHeights.StoreyCe + HeadExposure - AimInset;
            return point;
        }

        internal IntVec3 RangeCell(IntVec3 source) => IsoFloor.CellOnFloor(
            IsoFloor.GroundCellOf(Foot, Map),
            Mathf.Clamp(IsoFloor.FloorOf(source, Map), bottomLevel, topLevel), Map);
    }

    internal static class SiegeLadderGeometry
    {
        internal static bool TryGet(Thing thing, out SiegeLadderShape shape)
        {
            shape = default;
            if (!IsoSiegeLadders.Ends(thing, out IntVec3 foot, out IntVec3 top)) return false;
            shape = new SiegeLadderShape(thing.Map, foot, top);
            return true;
        }
    }
}
