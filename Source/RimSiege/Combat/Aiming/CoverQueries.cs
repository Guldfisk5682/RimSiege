using System.Collections.Generic;
using CombatExtended;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Aiming
{
    internal static class CoverQueries
    {
        public static List<IntVec3> Cells(IntVec3 from, IntVec3 to, Verb_LaunchProjectileCE verb, LocalTargetInfo actualTarget)
        {
            Map map = verb.Caster.Map;
            if (!IsoFloor.HasFloors(map))
                return GenSightCE.AllPointsOnLineOfSight(from, to);
            float target = !verb.Projectile.projectile.flyOverhead &&
                SiegeLadderGeometry.TryGet(actualTarget.Thing, out SiegeLadderShape ladder)
                ? ladder.AimPoint(verb.Caster.Position).y
                : CombatHeights.Base(from, map) + new CollisionVertical(actualTarget.Thing).HeightRange.Average;
            return AtHeights(from, to, map, target, verb.ShotHeight);
        }

        public static List<IntVec3> ThrownCells(IntVec3 from, IntVec3 to, Verb_LaunchProjectileCE verb, LocalTargetInfo target)
        {
            Map map = verb.Caster.Map;
            if (!IsoFloor.HasFloors(map)) return GenSightCE.AllPointsOnLineOfSight(from, to);
            float targetHeight = CombatHeights.Base(target.Cell, map) + new CollisionVertical(target.Thing).HeightRange.Average;
            return AtHeights(from, to, map, verb.ShotHeight, targetHeight);
        }

        private static List<IntVec3> AtHeights(IntVec3 from, IntVec3 to, Map map, float start, float end)
        {
            IntVec3 a = IsoFloor.GroundCellOf(from, map), b = IsoFloor.GroundCellOf(to, map);
            var cells = new List<IntVec3>(GenSightCE.AllPointsOnLineOfSight(a, b));
            for (int i = 0; i < cells.Count; i++)
            {
                float t = cells.Count <= 1 ? 0f : (float)i / (cells.Count - 1);
                int floor = Mathf.Clamp(Mathf.FloorToInt(Mathf.Lerp(start, end, t) / CombatHeights.StoreyCe), IsoFloor.MinLevel(map), IsoFloor.MaxLevel(map));
                cells[i] = IsoFloor.CellOnFloor(cells[i], floor, map);
            }
            return cells;
        }
    }
}
