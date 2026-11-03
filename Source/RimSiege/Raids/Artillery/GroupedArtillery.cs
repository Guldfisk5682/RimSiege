using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    // Exists only during one incident. Native arrival still owns the actual random split and entry cells.
    internal sealed class GroupedArtillery
    {
        private sealed class Group
        {
            internal List<Pawn> Pawns;
            internal IntVec3 Entry;
            internal IncidentParms Parms;
            internal readonly List<ArtilleryPlacement> Guns = new();
            internal ArtilleryRaid Raid;
            internal float Power => Pawns.Sum(p => p.kindDef.combatPower);
        }

        private readonly List<Group> groups;
        private readonly Dictionary<Pawn, Group> membership = new();
        private readonly HashSet<Pawn> reservedCrew = new(), specialists = new();
        private readonly ArtilleryRaid reservation;
        internal ArtilleryIncident Incident;
        internal IEnumerable<ArtilleryRaid> Batteries => groups.Where(g => g.Raid != null).Select(g => g.Raid);

        private GroupedArtillery(ArtilleryRaid reservation, List<Pair<List<Pawn>, IntVec3>> native)
        {
            this.reservation = reservation;
            groups = native.Select(pair => new Group { Pawns = pair.First, Entry = pair.Second }).ToList();
            foreach (Group g in groups)
                specialists.UnionWith(ArtilleryDeployment.Specialists(reservation.Strategy, g.Pawns));
        }

        internal ArtilleryRaid ForRoster(List<Pawn> pawns, out IncidentParms parms)
        {
            parms = null;
            if (pawns.Count == 0 || !membership.TryGetValue(pawns[0], out Group group)) return null;
            parms = group.Parms;
            return group.Raid;
        }

        internal void TrackNativeLord(Lord lord)
        {
            if (Incident == null || lord.ownedPawns.Count == 0 ||
                !membership.TryGetValue(lord.ownedPawns[0], out Group group) || group.Raid != null ||
                lord.ownedPawns.Count != group.Pawns.Count) return;
            Incident.NativeLords.Add(lord);
        }

        internal void CompleteSetup(Map map)
        {
            if (Incident == null) return;
            Incident.SetupComplete = true;
            map.GetComponent<ArtilleryMap>().FinishIncident(Incident);
        }

        private static bool Healthy(Pawn pawn) => !pawn.Dead && !pawn.Downed && !pawn.InMentalState &&
            pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);

        // Native siege's roster eligibility, before a real spawned traveller/lord exists.
        // Its actual CanBeBuilder and reach checks still run at camp initialization.
        private bool Builder(Pawn pawn) => Healthy(pawn) && (reservation.Kind == ArtilleryRaidKind.Mechanoid
            ? MechBattery.IsConstructoid(pawn) : pawn.RaceProps.Humanlike && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction) &&
                !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Firefighter));

        private bool EnsureCrew(Group target, ArtilleryPlacement gun)
        {
            var moves = new List<(Group From, Pawn Pawn)>();
            var lockAfter = new HashSet<Pawn>();
            IEnumerable<Pawn> Here() => target.Pawns.Concat(moves.Select(m => m.Pawn));
            Pawn Take(System.Func<Pawn, bool> eligible)
            {
                Pawn local = Here().FirstOrDefault(p => !lockAfter.Contains(p) && !specialists.Contains(p) && eligible(p));
                if (local != null) return local;
                foreach (Group donor in groups.Where(g => g != target))
                {
                    if (donor.Pawns.Count - moves.Count(m => m.From == donor) <= 1) continue;
                    Pawn pawn = donor.Pawns.FirstOrDefault(p => !reservedCrew.Contains(p) && !specialists.Contains(p) &&
                        !moves.Any(m => m.Pawn == p) && eligible(p));
                    if (pawn != null) { moves.Add((donor, pawn)); return pawn; }
                }
                return null;
            }

            if (gun.NeedsOperator)
            {
                int needed = target.Guns.Count(g => g.NeedsOperator) + 1;
                for (int i = 0; i < needed; i++)
                {
                    Pawn op = Take(ArtilleryCamp.OperatorCandidate);
                    if (op == null) return false;
                    lockAfter.Add(op);
                }
            }
            Pawn builder = Here().FirstOrDefault(p => !specialists.Contains(p) && Builder(p)) ?? Take(Builder);
            if (builder == null) return false;
            lockAfter.Add(builder);
            foreach (var move in moves) { move.From.Pawns.Remove(move.Pawn); target.Pawns.Add(move.Pawn); }
            reservedCrew.UnionWith(lockAfter);
            return true;
        }

        private void Allocate(Map map, List<Pawn> allPawns)
        {
            var pool = reservation.Camp.Guns;
            var supported = pool.Where(g => g.IsSupport).ToList();
            // A first pass gives one piece per staffed group; only surplus uses proportional allocation.
            foreach (ArtilleryPlacement gun in supported)
            {
                Group target = groups.OrderBy(g => g.Guns.Any(p => p.IsSupport) ? 1 : 0)
                    .ThenBy(g => g.Guns.Count / System.Math.Max(1f, g.Power)).ThenByDescending(g => g.Power)
                    .FirstOrDefault(g => EnsureCrew(g, gun));
                if (target != null) target.Guns.Add(gun);
            }
            foreach (ArtilleryPlacement gun in pool.Where(g => !g.IsSupport))
            {
                Group target = groups.Where(g => g.Guns.Any(p => p.IsSupport))
                    .OrderBy(g => g.Guns.Count / System.Math.Max(1f, g.Power)).FirstOrDefault();
                target?.Guns.Add(gun);
            }

            var plans = new List<CampPlan>();
            var catalog = new ArtilleryTargets.Catalog(map);
            var unused = new List<ArtilleryPlacement>();
            foreach (Group group in groups.OrderByDescending(g => g.Guns.Any(p => p.IsSupport)).ThenByDescending(g => g.Power))
            {
                foreach (ArtilleryPlacement gun in unused.ToList())
                    if ((group.Guns.Any(g => g.IsSupport) || gun.IsSupport) && EnsureCrew(group, gun))
                    { group.Guns.Add(gun); unused.Remove(gun); }
                if (!group.Guns.Any(g => g.IsSupport)) continue;
                IncidentParms parms = reservation.OriginalParms.ShallowCopy();
                parms.spawnCenter = group.Entry;
                parms.pawnGroups = null;
                var child = new ArtilleryRaid
                {
                    Kind = reservation.Kind, Phase = ArtilleryPhase.Planned,
                    RaidPoints = reservation.RaidPoints, PawnPoints = reservation.PawnPoints * group.Power /
                        System.Math.Max(1f, allPawns.Sum(p => p.kindDef.combatPower)),
                    Faction = reservation.Faction, Strategy = reservation.Strategy, OriginalParms = parms,
                    Selected = true, Rolled = true, IndependentCrew = reservation.IndependentCrew,
                    ConstructionSpeedMultiplier = reservation.ConstructionSpeedMultiplier,
                    ConstructoidCount = group.Pawns.Count(MechBattery.IsConstructoid)
                };
                parms.points = child.PawnPoints;
                if (!ArtilleryRaidPolicy.Plan(child, map, group.Entry, group.Guns, groups.Count, plans, catalog))
                { unused.AddRange(group.Guns); group.Guns.Clear(); continue; }
                group.Raid = child; plans.Add(child.Camp);
                // Recover pieces removed by layout screening without re-rolling ammo or global equipment caps.
                var retained = child.Camp.Guns.ToList();
                foreach (ArtilleryPlacement gun in group.Guns)
                {
                    ArtilleryPlacement match = retained.FirstOrDefault(p => p.Def == gun.Def && p.Ammo == gun.Ammo && p.Role == gun.Role);
                    if (match == null) unused.Add(gun); else retained.Remove(match);
                }
            }
        }

        private void RefundNativeRoster(List<Pawn> all, List<Pair<List<Pawn>, IntVec3>> native)
        {
            RaidSpawnPatch.Spawn spawn = RaidSpawnPatch.Current;
            foreach (Pawn pawn in spawn.InjectedConstructoids)
            {
                all.Remove(pawn);
                foreach (Group group in groups) group.Pawns.Remove(pawn);
                pawn.Destroy(DestroyMode.Vanish);
            }
            groups.RemoveAll(g => g.Pawns.Count == 0);
            native.RemoveAll(g => g.First.Count == 0);
            IncidentParms parms = reservation.OriginalParms;
            float refund = spawn.NativePawnPoints - reservation.PawnPoints;
            if (refund > 0f)
            {
                PawnGroupMakerParms request = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(parms.pawnGroupKind ?? PawnGroupKindDefOf.Combat, parms);
                request.points = refund;
                foreach (Pawn pawn in PawnGroupMakerUtility.GeneratePawns(request, warnOnZeroResults: false))
                {
                    all.Add(pawn);
                    groups.OrderBy(g => g.Power).First().Pawns.Add(pawn);
                }
            }
            parms.points = spawn.NativePawnPoints;
            spawn.Artillery = null;
        }

        private void Prepare(Map map, List<Pawn> allPawns, List<Pair<List<Pawn>, IntVec3>> native)
        {
            Allocate(map, allPawns);
            if (Batteries.Any())
            {
                if (reservation.Kind == ArtilleryRaidKind.Mechanoid)
                    foreach (Group unarmed in groups.Where(g => g.Raid == null))
                        foreach (Pawn builder in unarmed.Pawns.Where(MechBattery.IsConstructoid).ToList())
                        {
                            if (unarmed.Pawns.Count <= 1) break;
                            Group camp = groups.Where(g => g.Raid != null)
                                .OrderBy(g => g.Pawns.Count(MechBattery.IsConstructoid) /
                                    (float)System.Math.Max(1, g.Raid.Camp.Guns.Count)).First();
                            unarmed.Pawns.Remove(builder); camp.Pawns.Add(builder);
                        }
                Incident = new ArtilleryIncident();
                map.GetComponent<ArtilleryMap>().RegisterIncident(Incident);
                foreach (ArtilleryRaid raid in Batteries) raid.IncidentKey = Incident.Key;
            }
            else RefundNativeRoster(allPawns, native);
            foreach (Group group in groups)
            {
                group.Parms = group.Raid?.OriginalParms ?? reservation.OriginalParms.ShallowCopy();
                group.Parms.spawnCenter = group.Entry;
                group.Parms.pawnGroups = null;
                if (group.Raid == null) group.Parms.points = reservation.OriginalParms.points * group.Power /
                    System.Math.Max(1f, allPawns.Sum(p => p.kindDef.combatPower));
                foreach (Pawn pawn in group.Pawns) membership.Add(pawn, group);
            }
            Log.Message("[RimSiege] Grouped artillery: groups=" + groups.Count + " camps=" + Batteries.Count() +
                " support=" + Batteries.Sum(r => r.Camp.Guns.Count(g => g.IsSupport)) +
                " configuredSupport=" + reservation.Camp.Guns.Count(g => g.IsSupport) + "; " +
                string.Join("; ", groups.Select(g => "entry=" + g.Entry + " pawns=" + g.Pawns.Count +
                    " guns=" + (g.Raid?.Camp.Guns.Count ?? 0))));
        }

        [HarmonyPatch(typeof(PawnsArrivalModeWorkerUtility), nameof(PawnsArrivalModeWorkerUtility.SplitIntoRandomGroupsNearMapEdge))]
        private static class ActualGroups
        {
            private static void Postfix(List<Pawn> pawns, Map map, List<Pair<List<Pawn>, IntVec3>> __result)
            {
                RaidSpawnPatch.Spawn spawn = RaidSpawnPatch.Current;
                if (spawn?.Artillery == null || spawn.Grouped != null || spawn.Pawns != pawns ||
                    !ArtilleryRaidPolicy.IsGrouped(spawn.Artillery.OriginalParms.raidArrivalMode)) return;
                var grouped = new GroupedArtillery(spawn.Artillery, __result);
                spawn.Grouped = grouped;
                grouped.Prepare(map, pawns, __result);
            }
        }

        [HarmonyPatch(typeof(LordMaker), nameof(LordMaker.MakeNewLord))]
        private static class NativeGroupLord
        {
            private static void Postfix(Lord __result) => RaidSpawnPatch.Current?.Grouped?.TrackNativeLord(__result);
        }
    }
}
