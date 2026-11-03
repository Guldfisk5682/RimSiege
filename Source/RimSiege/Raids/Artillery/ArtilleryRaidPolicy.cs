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
                !(parms.target is Map map) || parms.faction == null || !parms.faction.HostileTo(Faction.OfPlayer))
                return null;

            bool grouped = IsGrouped(parms.raidArrivalMode);
            if ((!grouped && parms.raidArrivalMode != PawnsArrivalModeDefOf.EdgeWalkIn &&
                 parms.raidArrivalMode != PawnsArrivalModeDefOf.EdgeDrop) || (!grouped && !parms.spawnCenter.IsValid) ||
                parms.attackTargets?.Count > 0 || parms.controllerPawn?.Faction == Faction.OfPlayer ||
                // Explicit grouping may omit pawns: vanilla puts unlabelled
                // members in group -1. Decline before reducing the pawn budget.
                (parms.pawnGroups?.Count ?? 0) > 0) return null;

            RaidStrategyWorker worker = parms.raidStrategy.Worker;
            // This worker spawns a mech cluster and never creates a normal raid lord.
            // It inherits Siege, so exclude it before classifying ordinary strategies.
            if (worker is RaidStrategyWorker_SiegeMechanoid) return null;

            string kind;
            if (parms.faction == Faction.OfMechanoids)
            {
                if (!ModsConfig.BiotechActive) return null;
                kind = "Mechanoid";
            }
            else
            {
                if (!parms.faction.def.humanlikeFaction || parms.faction.def.techLevel < TechLevel.Industrial) return null;
                kind = worker is RaidStrategyWorker_ImmediateAttackBreaching ? "Breaching"
                    : worker is RaidStrategyWorker_Siege ? "Siege" : "Ordinary";
            }
            return BuildRaid(parms, raidPoints, pawnPoints, map, kind);
        }

        private static ArtilleryRaid BuildRaid(IncidentParms parms, float raidPoints, float pawnPoints, Map map,
            string kind)
        {
            ArtilleryProfileDef profile = DefDatabase<ArtilleryProfileDef>.AllDefsListForReading
                .FirstOrDefault(p => p.kind == kind);
            if (profile == null || profile.BelowGate(raidPoints) ||
                !Rand.Chance(RimSiegeMod.Settings.ArtilleryChance / 10f)) return null;

            float personnel = profile.PersonnelPoints(raidPoints, pawnPoints);
            int constructoids = 0;
            if (kind == "Mechanoid")
            {
                constructoids = MechBattery.RollConstructoids();
                personnel = UnityEngine.Mathf.Max(0f, personnel - MechBattery.ConstructoidCombatPower * constructoids);
            }
            List<ArtilleryPlacement> guns = profile.Configure(raidPoints, personnel, parms.faction, out float share);
            if (IsGrouped(parms.raidArrivalMode) && kind == "Mechanoid")
            {
                // Reserve enough of the existing global builder budget for every possible armed group.
                int minimum = System.Math.Min(3, guns.Count(g => g.IsSupport));
                if (constructoids < minimum)
                {
                    personnel -= MechBattery.ConstructoidCombatPower * (minimum - constructoids);
                    constructoids = minimum;
                    float Cost(ArtilleryPlacement gun) => profile.guns.First(rule => rule.turret == gun.Def).ResolvedCost;
                    while (guns.Count > 0 && guns.Sum(Cost) > personnel * share) guns.RemoveAt(guns.Count - 1);
                }
            }
            if (!guns.Any(g => g.IsSupport)) return null;
            var raid = new ArtilleryRaid
            {
                Kind = (ArtilleryRaidKind)System.Enum.Parse(typeof(ArtilleryRaidKind), kind),
                Phase = ArtilleryPhase.Planned, RaidPoints = raidPoints, PawnPoints = personnel,
                Faction = parms.faction, Strategy = parms.raidStrategy, OriginalParms = parms,
                Camp = new CampPlan { Guns = guns }, Rolled = true, Selected = true,
                IndependentCrew = kind != "Siege", ConstructoidCount = constructoids,
                ConstructionSpeedMultiplier = profile.ConstructionSpeed(raidPoints)
            };
            if (IsGrouped(parms.raidArrivalMode)) return raid;
            return Plan(raid, map, parms.spawnCenter, guns) ? raid : null;
        }

        internal static bool IsGrouped(PawnsArrivalModeDef arrival) =>
            arrival == PawnsArrivalModeDefOf.EdgeWalkInGroups || arrival?.defName == "EdgeDropGroups";

        internal static bool Plan(ArtilleryRaid raid, Map map, IntVec3 entry, List<ArtilleryPlacement> guns,
            int divisions = 1, IReadOnlyList<CampPlan> otherCamps = null, ArtilleryTargets.Catalog catalog = null)
        {
            IntVec3 goal = GenAI.RandomRaidDest(entry, map);
            SupportRoute route = SupportRoute.Build(map, entry, goal);
            if (route == null) return false;
            var siting = route.PlanningTargets(catalog: catalog);
            if (siting.Targets.Count == 0) return false;
            Dictionary<ArtilleryPlacement, float> ranges = null;
            if (guns.Any(g => g.IsDirectSupport))
                ranges = guns.ToDictionary(g => g, g => !g.IsSupport ? 0f : CeArtillerySight.EffectiveRange(map, raid.Faction,
                    new EmplacementSpec(g.Def, g.Stuff, g.Ammo, entry, Rot4.North, null)));
            IEnumerable<IntVec3> centers = Centers(map, entry, siting, guns, ranges,
                System.Math.Max(1, CampCenterAttempts / divisions));
            if (otherCamps != null)
                centers = centers.Where(center => otherCamps.All(other => IsoFloor.Distance3D(center, other.Center, map) >
                    CampSupplies.Radius * 2));
            if (!CampPlanner.TryPlan(map, raid.Faction, entry, goal, guns, centers, siting, ranges, out CampPlan camp,
                fortified: raid.Kind == ArtilleryRaidKind.Siege, divisions: divisions)) return false;
            raid.Camp = camp;
            SupportRoute.Set(raid, route);
            return true;
        }

        private static IEnumerable<IntVec3> Centers(Map map, IntVec3 entry, CampSiting siting,
            List<ArtilleryPlacement> guns, IReadOnlyDictionary<ArtilleryPlacement, float> ranges, int attempts)
        {
            IntVec3 native = CampEntrySearch.Find(entry, map);
            var direct = guns.Where(g => g.IsDirectSupport).ToList();
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
            // Preserve the 200-centre budget. Deployment feasibility and firepower
            // are ranked by CampPlanner; approach distance orders equivalent sites.
            var candidates = new List<IntVec3>();
            if (native.InBounds(map))
            {
                IntVec3 projected = IsoFloor.HasFloors(map)
                    ? IsoFloor.CellOnFloor(IsoFloor.GroundCellOf(native, map), IsoFloor.FloorOf(entry, map), map) : native;
                if (projected.InBounds(map)) candidates.Add(projected);
            }
            float preferred = direct.Min(g => ranges[g]) - CampSiting.RangeMargin;
            bool floors = IsoFloor.HasFloors(map);
            int sourceFloor = floors ? IsoFloor.FloorOf(entry, map) : 0;
            int targetFloor = floors ? IsoFloor.FloorOf(searchTarget, map) : 0;
            float inner = UnityEngine.Mathf.Max(0f, preferred - CampSiting.CenterTolerance);
            float outer = preferred + CampSiting.CenterTolerance;
            if (floors)
            {
                inner = UnityEngine.Mathf.Max(0f, IsoFloor.HorizontalReach(inner, sourceFloor, targetFloor));
                outer = IsoFloor.HorizontalReach(outer, sourceFloor, targetFloor);
            }
            // Spend the same sample budget in the actual engagement band,
            // rather than relying on a few lucky cells in a large square.
            if (outer >= 0f)
                for (int i = 0; i < attempts; i++)
                {
                    var offset = UnityEngine.Vector3.forward.RotatedBy(Rand.Range(0f, 360f)) * Rand.Range(inner, outer);
                    IntVec3 cell = (center.ToVector3() + offset).ToIntVec3();
                    cell.y = 0;
                    if (search.Contains(cell)) candidates.Add(cell);
                }
            var ordered = candidates.Select(cell =>
            {
                float deviation = UnityEngine.Mathf.Abs(siting.Distance(cell, siting.BreachAnchor) - preferred);
                return (Cell: cell, EntryDistance: siting.Distance(entry, cell), Deviation: deviation);
            }).OrderBy(candidate => candidate.EntryDistance).ThenBy(candidate => candidate.Deviation);
            // Keep the closer alternative first across spatial groups as well.
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
                frontier.Enqueue(group, (next.EntryDistance, next.Deviation, order++));
            }
        }
    }
}
