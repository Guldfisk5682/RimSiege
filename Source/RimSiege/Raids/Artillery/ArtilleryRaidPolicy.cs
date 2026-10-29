using System.Collections.Generic;
using System.Linq;
using RimSiege.Integration;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege.Raids.Artillery
{
    internal static class ArtilleryRaidPolicy
    {
        private const int CampCenterAttempts = 200;

        internal static ArtilleryRaid Prepare(IncidentParms parms, float raidPoints, float pawnPoints)
        {
            if (!SiegeRaidMap.Enabled || RimSiegeMod.Settings.ArtilleryChance == 0 ||
                !(parms.target is Map map) || parms.faction == null || !parms.faction.HostileTo(Faction.OfPlayer) || !parms.faction.def.humanlikeFaction ||
                parms.faction.def.techLevel < TechLevel.Medieval ||
                parms.raidArrivalMode != PawnsArrivalModeDefOf.EdgeWalkIn || !parms.spawnCenter.IsValid ||
                parms.attackTargets?.Count > 0 || parms.controllerPawn?.Faction == Faction.OfPlayer ||
                (parms.pawnGroups != null && parms.pawnGroups.Values.Distinct().Count() > 1)) return null;

            string kind;
            if (parms.raidStrategy.Worker is RaidStrategyWorker_ImmediateAttackBreaching) kind = "Breaching";
            else if (parms.raidStrategy.Worker is RaidStrategyWorker_Siege) kind = "Siege";
            else if (parms.raidStrategy.Worker is RaidStrategyWorker_ImmediateAttack ||
                parms.raidStrategy.Worker is RaidStrategyWorker_ImmediateAttackSmart) kind = "Ordinary";
            else return null;
            ArtilleryProfileDef profile = DefDatabase<ArtilleryProfileDef>.AllDefsListForReading.First(p => p.kind == kind);
            if (profile.totalPieces.Evaluate(raidPoints) < 1f ||
                !Rand.Chance(RimSiegeMod.Settings.ArtilleryChance / 10f)) return null;

            float personnel = pawnPoints * profile.personnelFactor;
            List<ArtilleryPlacement> guns = profile.Configure(raidPoints, personnel, parms.faction);
            if (guns.Count == 0) return null;
            IntVec3 goal = GenAI.RandomRaidDest(parms.spawnCenter, map);
            SupportRoute route = SupportRoute.Build(map, parms.spawnCenter, goal);
            if (route == null) return null;
            var siting = route.PlanningTargets();
            if (siting.Targets.Count == 0) return null;
            Dictionary<ArtilleryPlacement, float> ranges = null;
            if (guns.Any(g => CeArtillerySight.IsDirectFire(g.Def, g.Ammo)))
                ranges = guns.ToDictionary(g => g, g => CeArtillerySight.EffectiveRange(map, parms.faction,
                    new EmplacementSpec(g.Def, g.Stuff, g.Ammo, parms.spawnCenter, Rot4.North, null)));
            if (!CampPlanner.TryPlan(map, parms.faction, parms.spawnCenter, goal, guns,
                Centers(map, parms.spawnCenter, siting, guns, ranges), siting, ranges, out CampPlan camp)) return null;

            var raid = new ArtilleryRaid
            {
                Kind = (ArtilleryRaidKind)System.Enum.Parse(typeof(ArtilleryRaidKind), kind),
                Phase = ArtilleryPhase.Planned, RaidPoints = raidPoints, PawnPoints = personnel,
                Faction = parms.faction, Strategy = parms.raidStrategy, OriginalParms = parms,
                Camp = camp, Rolled = true, Selected = true
            };
            SupportRoute.Set(raid, route);
            return raid;
        }

        private static IEnumerable<IntVec3> Centers(Map map, IntVec3 entry, CampSiting siting,
            List<ArtilleryPlacement> guns, IReadOnlyDictionary<ArtilleryPlacement, float> ranges)
        {
            IntVec3 native = RCellFinder.FindSiegePositionFrom(entry, map);
            var direct = guns.Where(g => CeArtillerySight.IsDirectFire(g.Def, g.Ammo)).ToList();
            if (direct.Count == 0)
            {
                yield return native;
                yield break;
            }
            IntVec3 searchTarget = siting.BreachAnchor;
            IntVec3 center = IsoFloor.HasFloors(map)
                ? IsoFloor.CellOnFloor(IsoFloor.GroundCellOf(searchTarget, map), IsoFloor.FloorOf(entry, map), map) : searchTarget;
            float range = direct.Min(g => ranges[g]) + UnityEngine.Mathf.Sqrt(2f) * CampPlanner.GunAreaRadius;
            CellRect search = CellRect.CenteredOn(center, UnityEngine.Mathf.CeilToInt(range));
            search.ClipInsideMap(map);
            if (IsoFloor.HasFloors(map))
            {
                search.minZ = System.Math.Max(search.minZ, IsoFloor.FirstRow(map, IsoFloor.FloorOf(entry, map)));
                search.maxZ = System.Math.Min(search.maxZ, IsoFloor.LastRow(map, IsoFloor.FloorOf(entry, map)));
            }
            // Preserve the native 200-centre budget; direct-fire centres first
            // satisfy the minimum retreat and prefer its neighbourhood.
            var candidates = new List<IntVec3>();
            if (native.InBounds(map))
            {
                IntVec3 projected = IsoFloor.HasFloors(map)
                    ? IsoFloor.CellOnFloor(IsoFloor.GroundCellOf(native, map), IsoFloor.FloorOf(entry, map), map) : native;
                if (projected.InBounds(map)) candidates.Add(projected);
            }
            for (int i = 0; i < CampCenterAttempts; i++) candidates.Add(search.RandomCell);
            float preferred = direct.Min(g => ranges[g]) - CampSiting.RangeMargin;
            var ordered = candidates.Select(cell =>
            {
                float deviation = UnityEngine.Mathf.Abs(siting.Distance(cell, siting.BreachAnchor) - preferred);
                return (Cell: cell, EntryDistance: siting.Distance(entry, cell), Deviation: deviation);
            }).OrderBy(candidate => candidate.Deviation).ThenBy(candidate => candidate.EntryDistance);
            // Keep group alternatives without letting a distant representative
            // jump ahead of a closer-to-retreat alternative from another group.
            var groups = new Dictionary<(int, int, int), Queue<(IntVec3 Cell, float EntryDistance, float Deviation)>>();
            var seen = new HashSet<IntVec3>();
            foreach (var candidate in ordered)
            {
                if (!seen.Add(candidate.Cell)) continue;
                IntVec3 ground = IsoFloor.HasFloors(map) ? IsoFloor.GroundCellOf(candidate.Cell, map) : candidate.Cell;
                int floor = IsoFloor.HasFloors(map) ? IsoFloor.FloorOf(candidate.Cell, map) : 0;
                var key = (ground.x / 3, ground.z / 3, floor);
                if (!groups.TryGetValue(key, out var group)) groups[key] = group = new();
                group.Enqueue(candidate);
            }
            var frontier = new PriorityQueue<Queue<(IntVec3 Cell, float EntryDistance, float Deviation)>, (float, float, int)>();
            int order = 0;
            foreach (var group in groups.Values) Enqueue(group);
            while (frontier.Count > 0)
            {
                var group = frontier.Dequeue();
                yield return group.Dequeue().Cell;
                if (group.Count > 0) Enqueue(group);
            }

            void Enqueue(Queue<(IntVec3 Cell, float EntryDistance, float Deviation)> group)
            {
                var next = group.Peek();
                frontier.Enqueue(group, (next.Deviation, next.EntryDistance, order++));
            }
        }
    }
}
