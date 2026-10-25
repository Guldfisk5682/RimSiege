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
            IntVec3 a = IsoFloor.GroundCellOf(from, map), b = IsoFloor.GroundCellOf(to, map);
            var cells = new List<IntVec3>(GenSightCE.AllPointsOnLineOfSight(a, b));
            float sourceHeight = verb.ShotHeight;
            float target = CombatHeights.Base(from, map) + new CollisionVertical(actualTarget.Thing).HeightRange.Average;
            for (int i = 0; i < cells.Count; i++)
            {
                float t = cells.Count <= 1 ? 0f : (float)i / (cells.Count - 1);
                int floor = Mathf.Clamp(Mathf.FloorToInt(Mathf.Lerp(target, sourceHeight, t) / CombatHeights.StoreyCe), IsoFloor.MinLevel(map), IsoFloor.MaxLevel(map));
                cells[i] = IsoFloor.CellOnFloor(cells[i], floor, map);
            }
            return cells;
        }
    }
}
