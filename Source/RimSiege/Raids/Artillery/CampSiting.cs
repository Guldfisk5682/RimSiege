using System;
using System.Collections.Generic;
using System.Linq;
using RimSiege.Integration;
using RimWorld;
using Verse;

namespace RimSiege.Raids.Artillery
{
    // One planning or arrival-time snapshot of the native siege target set.
    internal sealed class CampSiting
    {
        internal const float RangeMargin = 10f;
        // Match the existing three-cell centre grouping; this is a deployment
        // band around the reference, not a ten-cell-wide maximum-range fallback.
        internal const float CenterTolerance = 3f;
        private readonly Map map;
        private readonly bool floors;
        internal readonly List<LocalTargetInfo> Targets;
        internal readonly List<Thing> BreachTargets;
        internal readonly ArtilleryTargets Candidates;
        internal readonly IntVec3 BreachAnchor;

        internal CampSiting(ArtilleryTargets candidates, SupportRoute route, IntVec3? breachAnchor = null)
        {
            Candidates = candidates;
            Map map = candidates.Map;
            this.map = map;
            floors = IsoFloor.HasFloors(map);
            List<LocalTargetInfo> colony = ColonyTargets(map);
            Targets = candidates.All.ToList();
            BreachAnchor = breachAnchor.HasValue && breachAnchor.Value.IsValid
                ? breachAnchor.Value : FindAnchor(route, candidates, colony);
            BreachTargets = candidates.FrontAt(BreachAnchor);
        }

        // These are exactly the colonist and player combat-building collections
        // used by vanilla RCellFinder.TryFindSiegePosition.
        internal static List<LocalTargetInfo> ColonyTargets(Map map)
        {
            var result = new List<LocalTargetInfo>();
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned) result.Add(pawn);
            foreach (Building building in map.listerBuildings.allBuildingsColonistCombatTargets) result.Add(building);
            return result;
        }

        internal float Distance(IntVec3 from, IntVec3 to) =>
            floors ? IsoFloor.RangeDistance(from, to, map) : (from - to).LengthHorizontal;

        private IntVec3 Ground(IntVec3 cell) => floors ? IsoFloor.GroundCellOf(cell, map) : cell;

        internal float Bearing(IntVec3 cell)
        {
            IntVec3 from = Ground(BreachAnchor), to = Ground(cell);
            return UnityEngine.Mathf.Atan2(to.x - from.x, to.z - from.z) * UnityEngine.Mathf.Rad2Deg;
        }

        private IntVec3 FindAnchor(SupportRoute route, ArtilleryTargets candidates, List<LocalTargetInfo> colony)
        {
            var walls = new HashSet<Thing>(candidates.Walls);
            foreach (var segment in route.Segments)
                foreach (IntVec3 cell in segment.Nodes)
                {
                    if (!cell.InBounds(map)) continue;
                    Thing wall = cell.GetEdifice(map);
                    if (wall != null && walls.Contains(wall) && candidates.Valid(wall)) return wall.Position;
                }

            if (walls.Count != 0)
            {
                IntVec3 entry = route.Segments.Count != 0 ? route.Segments[0].Start : route.Goal;
                IntVec3 from = Ground(entry), to = Ground(route.Goal);
                double dx = to.x - from.x, dz = to.z - from.z;
                double lengthSquared = dx * dx + dz * dz;
                return walls.Where(candidates.Valid).Select(wall =>
                {
                    IntVec3 point = Ground(wall.Position);
                    double px = point.x - from.x, pz = point.z - from.z;
                    double along = lengthSquared == 0 ? 0 : (px * dx + pz * dz) / lengthSquared;
                    double progress = Math.Max(0, Math.Min(1, along));
                    double sideX = px - progress * dx, sideZ = pz - progress * dz;
                    // Direction comes first: a nearby off-axis shed must not
                    // outrank a wall along the approach just because it is closer.
                    return (Wall: wall, Forward: along >= 0 && along <= 1,
                        LateralSquared: sideX * sideX + sideZ * sideZ, Progress: progress);
                }).OrderByDescending(item => item.Forward).ThenBy(item => item.LateralSquared).ThenBy(item => item.Progress)
                  .ThenBy(item => item.Wall.thingIDNumber).First().Wall.Position;
            }
            return route.Goal.InBounds(map) ? route.Goal : colony.Count != 0 ? colony[0].Cell : candidates.Goal;
        }

        internal bool Allows(IntVec3 center, float retreat)
        {
            return Math.Abs(Distance(center, BreachAnchor) - retreat) <= CenterTolerance;
        }

        internal IEnumerable<LocalTargetInfo> TargetsFor(bool direct) => direct
            ? BreachTargets.Select(t => (LocalTargetInfo)t) : Targets;

        internal ArtilleryTargetResult DeploymentTarget(Faction faction, EmplacementSpec placement, float? range)
        {
            bool direct = CeArtillerySight.IsDirectFire(placement.Def, placement.Ammo);
            // Direct-piece ranges are resolved for every template before site
            // search and again at arrival. A nearer incidental wall may not
            // substitute for range to the selected breach reference itself.
            if (direct && Distance(placement.Cell, BreachAnchor) > range.Value - RangeMargin)
                return new ArtilleryTargetResult(LocalTargetInfo.Invalid,
                    ArtilleryTargetReason.InsufficientDeploymentReserve, false, false);
            return Candidates.FindGeometry(faction, placement, range,
                direct ? BreachTargets : null, direct ? RangeMargin : 0f);
        }

    }
}
