using System.Collections.Generic;
using System.Linq;
using RimSiege.Integration;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege.Raids.Artillery
{
    // Uses the native drop-cell predicate and pod contents, with camp-local constraints.
    internal static class CampSupplies
    {
        internal const int Radius = 16;

        private static HashSet<IntVec3> Reserved(Map map, CampPlan camp)
        {
            var cells = new HashSet<IntVec3>();
            foreach (ArtilleryPlacement item in camp.Guns.Concat(camp.Cover))
            {
                if (item.Def == null || !item.Cell.IsValid) continue;
                foreach (IntVec3 cell in GenAdj.OccupiedRect(item.Cell, item.Rotation, item.Def.Size)) cells.Add(cell);
            }
            foreach (ArtilleryPlacement gun in camp.Guns)
            {
                if (gun.Def == null || !gun.Cell.IsValid) continue;
                IntVec3 interaction = ThingUtility.InteractionCellWhenAt(gun.Def, gun.Cell, gun.Rotation, map);
                cells.Add(interaction);
                foreach (IntVec3 direction in GenAdj.CardinalDirections) cells.Add(interaction + direction);
            }
            return cells;
        }

        private static bool FindSpot(Map map, CampPlan camp, HashSet<IntVec3> reserved, bool allowFogged,
            bool canRoofPunch, out IntVec3 spot)
        {
            IntVec3 center = camp.Center;
            bool floors = IsoFloor.HasFloors(map);
            int floor = floors ? IsoFloor.FloorOf(center, map) : 0;
            return CellFinder.TryFindRandomCellNear(center, map, Radius, cell =>
                cell.InBounds(map) && (cell - center).LengthHorizontalSquared <= Radius * Radius &&
                (!floors || IsoFloor.FloorOf(cell, map) == floor) && !reserved.Contains(cell) &&
                DropCellFinder.IsGoodDropSpot(cell, map, allowFogged, canRoofPunch) &&
                map.reachability.CanReach(center, cell, PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly),
                out spot);
        }

        internal static bool CanSupply(Map map, CampPlan camp) => camp != null && camp.Center.InBounds(map) &&
            FindSpot(map, camp, Reserved(map, camp), allowFogged: true, canRoofPunch: true, out _);

        internal static bool DropGroups(Map map, CampPlan camp, List<List<Thing>> groups, int openDelay,
            bool instaDrop, bool leaveSlag, bool canRoofPunch, bool forbid, bool allowFogged, Faction faction)
        {
            var reserved = Reserved(map, camp);
            var spots = new List<IntVec3>(groups.Count);
            foreach (List<Thing> group in groups)
            {
                if (!FindSpot(map, camp, reserved, allowFogged, canRoofPunch, out IntVec3 spot)) return false;
                spots.Add(spot);
                reserved.Add(spot);
            }
            for (int i = 0; i < groups.Count; i++)
            {
                List<Thing> group = groups[i];
                if (forbid)
                    foreach (Thing thing in group) thing.SetForbidden(true, warnOnFail: false);
                if (instaDrop)
                {
                    foreach (Thing thing in group) GenPlace.TryPlaceThing(thing, spots[i], map, ThingPlaceMode.Near);
                    continue;
                }
                var info = new ActiveTransporterInfo { openDelay = openDelay, leaveSlag = leaveSlag };
                foreach (Thing thing in group) info.innerContainer.TryAdd(thing);
                DropPodUtility.MakeDropPodAt(spots[i], map, info, faction);
            }
            return true;
        }

        internal static void Discard(List<List<Thing>> groups)
        {
            foreach (List<Thing> group in groups)
                foreach (Thing thing in group) thing.Destroy(DestroyMode.Vanish);
        }
    }
}
