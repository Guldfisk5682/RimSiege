using CombatExtended;
using IsoRimCEPackZ;
using UnityEngine;
using Verse;

namespace RimSiege.Integration
{
    // Ladder exposure is RimSiege policy; CEPack converts these virtual cells into each combat caller's frame.
    internal sealed class CeLadderGeometry : ITargetGeometryProvider
    {
        private const float Width = 0.7f, Depth = 0.16f, EdgeOffset = 0.4f;
        private static float Exposure => 0.7f * ProjectZ.Api.WallHeight / CollisionVertical.WallCollisionHeight;
        public bool TryGetVolume(Thing target, Map map, out Bounds volumeVirtual)
        {
            volumeVirtual = default;
            if (!IsoSiegeLadders.Ends(target, out var foot, out var top)) return false;
            int bottomLevel = IsoFloor.FloorOf(foot, map), topLevel = IsoFloor.FloorOf(top, map);
            IntVec3 ground = IsoFloor.GroundCellOf(foot, map);
            IntVec3 direction = IsoFloor.GroundCellOf(top, map) - ground;
            float bottom = bottomLevel * IsoFloor.StoreyHeight;
            float height = (topLevel - bottomLevel) * IsoFloor.StoreyHeight + Exposure;
            Vector3 center = ground.ToVector3Shifted() + direction.ToVector3() * EdgeOffset;
            center.y = bottom + height * 0.5f;
            volumeVirtual = new(center, new Vector3(direction.x == 0 ? Width : Depth,
                height, direction.z == 0 ? Width : Depth));
            return true;
        }
        public bool TryGetAim(Thing target, TargetQuery query, out TargetAim aim)
        {
            aim = default;
            if (query.Projectile?.projectile.flyOverhead == true ||
                !TryGetVolume(target, query.Map, out var volume) || !IsoSiegeLadders.Ends(target, out var foot, out var top)) return false;
            int level = Mathf.Clamp(IsoFloor.FloorOf(query.Root, query.Map), IsoFloor.FloorOf(foot, query.Map), IsoFloor.FloorOf(top, query.Map));
            Vector3 point = volume.center;
            point.y = level * IsoFloor.StoreyHeight + Exposure - 0.05f * ProjectZ.Api.WallHeight / CollisionVertical.WallCollisionHeight;
            aim = new(point, IsoFloor.CellOnFloor(IsoFloor.GroundCellOf(foot, query.Map), level, query.Map));
            return true;
        }
    }
}
