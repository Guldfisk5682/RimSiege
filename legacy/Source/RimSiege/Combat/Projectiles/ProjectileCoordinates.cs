using CombatExtended;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Projectiles
{
    internal static class ProjectileCoordinates
    {
        public static void MapPosition(ProjectileCE p, IntVec3 ignored)
        {
            if (!IsoFloor.HasFloors(p.Map))
            {
                p.Position = ignored;
                return;
            }
            Vector3 exact = CeAccess.ProjectilePosition(p);
            IntVec3 ground = exact.ToIntVec3();
            ground.y = 0;
            if (!IsoFloor.GroundRect(p.Map).Contains(ground))
                return;
            int floor = Mathf.Clamp(Mathf.FloorToInt(exact.y / CombatHeights.StoreyCe), IsoFloor.MinLevel(p.Map), IsoFloor.MaxLevel(p.Map));
            p.Position = IsoFloor.CellOnFloor(ground, floor, p.Map);
        }

        public static bool CheckLevels(ProjectileCE p, IntVec3 ground)
        {
            if (!IsoFloor.HasFloors(p.Map))
                return CeAccess.CheckCell(p, ground);
            ground.y = 0;
            if (!IsoFloor.GroundRect(p.Map).Contains(ground)) return false;
            int top = Mathf.Min(IsoFloor.MaxLevel(p.Map), Mathf.FloorToInt(Mathf.Max(CeAccess.Last(p).y, CeAccess.ProjectilePosition(p).y) / CombatHeights.StoreyCe));
            for (int floor = IsoFloor.MinLevel(p.Map); floor <= top; floor++)
            {
                IntVec3 cell = IsoFloor.CellOnFloor(ground, floor, p.Map);
                // CE still owns candidate selection and bounds/ray intersection.
                if (CeAccess.CheckCell(p, cell))
                    return true;
            }
            return false;
        }
    }
}
