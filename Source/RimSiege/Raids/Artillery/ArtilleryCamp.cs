using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using CombatExtended.Compatibility;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    internal static class ArtilleryCamp
    {
        internal static IEnumerable<ArtilleryPlacement> Placements(CampPlan camp) => camp.Guns.Concat(camp.Cover);

        private static bool CanOperate(Pawn pawn) => pawn.Spawned && !pawn.Dead && !pawn.Downed && !pawn.InMentalState &&
            pawn.RaceProps.Humanlike && !pawn.WorkTagIsDisabled(WorkTags.Violent) &&
            pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) && LadderInventory.Get(pawn) == null &&
            pawn.CurJobDef != JobDefOf.ManTurret && !SiegeRoles.HasDemolitionWeapon(pawn);

        internal static bool AssignOperators(ArtilleryRaid raid, Lord lord)
        {
            var available = lord.ownedPawns.Where(CanOperate).ToList();
            for (int i = 0; i < raid.Camp.Guns.Count; i++)
            {
                ArtilleryPlacement gun = raid.Camp.Guns[i];
                Pawn pawn = available.OrderBy(p => p.Position.DistanceToSquared(gun.Cell)).FirstOrDefault();
                if (pawn == null)
                {
                    raid.Camp.Guns.RemoveRange(i, raid.Camp.Guns.Count - i);
                    break;
                }
                gun.Operator = pawn;
                available.Remove(pawn);
            }
            return raid.Camp.Guns.Count != 0;
        }

        internal static bool Prepare(ArtilleryRaid raid, Lord lord)
        {
            Map map = lord.Map;
            SupportRoute route = SupportRoute.Restore(map, raid);
            return route != null && AssignOperators(raid, lord) && CampPlanner.Revalidate(map, raid, raid.Camp, route.PlanningTargets());
        }

        internal static List<Blueprint_Build> Blueprints(ArtilleryRaid raid, Lord lord)
        {
            var blueprints = new List<Blueprint_Build>();
            foreach (ArtilleryPlacement item in Placements(raid.Camp))
            {
                item.Blueprint = GenConstruct.PlaceBlueprintForBuild(item.Def, item.Cell, lord.Map, item.Rotation, raid.Faction, item.Stuff);
                blueprints.Add(item.Blueprint);
            }
            return blueprints;
        }

        internal static bool ObserveBuilding(ArtilleryRaid raid, Building building)
        {
            if (building.Faction != raid.Faction) return false;
            foreach (ArtilleryPlacement item in Placements(raid.Camp))
            {
                if (building.Position != item.Cell) continue;
                if (building is Frame frame && frame.def.entityDefToBuild == item.Def)
                {
                    item.Frame = frame;
                    return true;
                }
                if (building.def != item.Def) continue;
                bool newlyObserved = item.Building != building;
                item.Building = building;
                item.Frame = null;
                if (raid.Camp.Guns.Contains(item))
                {
                    building.Map.GetComponent<ArtilleryMap>().TrackGun(building, raid);
                    if (newlyObserved && building is Building_TurretGunCE turret && item.Ammo is AmmoDef ammo && turret.GetAmmo() is CompAmmoUser user)
                    {
                        // A freshly built turret starts loaded with its default shell;
                        // switch both the selection and the chamber to the planned ammo.
                        user.SelectedAmmo = ammo;
                        user.CurrentAmmo = ammo;
                    }
                }
                return true;
            }
            return false;
        }

        internal static void ObserveFailure(ArtilleryRaid raid, Frame frame, Blueprint_Build blueprint)
        {
            foreach (ArtilleryPlacement item in Placements(raid.Camp))
                if (item.Frame == frame) { item.Frame = null; item.Blueprint = blueprint; break; }
        }

        internal static void RefreshConstruction(ArtilleryRaid raid, Map map)
        {
            var released = new List<Pawn>();
            foreach (ArtilleryPlacement item in Placements(raid.Camp))
            {
                if (item.Blueprint != null && item.Blueprint.Destroyed) item.Blueprint = null;
                if (item.Frame != null && item.Frame.Destroyed) item.Frame = null;
                if (item.Building != null && (!item.Building.Spawned || item.Building.Faction != raid.Faction))
                {
                    map.GetComponent<ArtilleryMap>().ForgetGun(item.Building);
                    item.Building = null;
                }
                foreach (Thing thing in item.Cell.GetThingList(map))
                {
                    if (thing.Faction != raid.Faction) continue;
                    if (thing is Blueprint_Build blueprint && blueprint.def.entityDefToBuild == item.Def)
                        item.Blueprint = blueprint;
                    else if (thing is Building building) ObserveBuilding(raid, building);
                }
                if (item.Building == null && item.Frame == null && item.Blueprint == null && item.Operator != null)
                {
                    Pawn pawn = item.Operator;
                    item.Operator = null;
                    if (pawn.Spawned && !pawn.Dead && pawn.Faction == raid.Faction && pawn.GetLord() == raid.CampLord)
                        released.Add(pawn);
                }
            }
            raid.Camp.Guns.RemoveAll(item => item.Building == null && item.Frame == null && item.Blueprint == null);
            raid.Camp.Cover.RemoveAll(item => item.Building == null && item.Frame == null && item.Blueprint == null);
            if (raid.InfantryReleased && raid.Kind != ArtilleryRaidKind.Siege && raid.CampLord != null)
                TransferToAssault(raid, raid.CampLord, released);
        }

        internal static void CancelUnfinished(ArtilleryRaid raid)
        {
            foreach (ArtilleryPlacement item in Placements(raid.Camp))
            {
                if (item.Blueprint != null && !item.Blueprint.Destroyed) item.Blueprint.Destroy(DestroyMode.Cancel);
                if (item.Frame != null && !item.Frame.Destroyed) item.Frame.Destroy(DestroyMode.Cancel);
                item.Blueprint = null;
                item.Frame = null;
            }
        }

        internal static void TransferToAssault(ArtilleryRaid raid, Lord camp, List<Pawn> pawns)
        {
            if (pawns.Count == 0 || raid.AssaultEnded) return;
            if (raid.AssaultLord != null && !camp.Map.lordManager.lords.Contains(raid.AssaultLord)) raid.AssaultLord = null;
            camp.RemovePawns(pawns);
            foreach (Pawn pawn in pawns)
                if (pawn.jobs?.curJob?.lord == camp) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            if (raid.AssaultLord == null) raid.AssaultLord = ArtilleryRaidPatches.MakeOriginalLord(raid, camp.Map, pawns);
            else raid.AssaultLord.AddPawns(pawns);
            raid.AssaultCreated = true;
            SiegeRaidMap raids = camp.Map.GetComponent<SiegeRaidMap>();
            foreach (Pawn pawn in pawns)
            {
                SiegeParty party = raids.Party(pawn);
                if (party != null) raids.Issue(pawn, party);
            }
        }

        internal static void ReleaseInfantry(ArtilleryRaid raid, Lord camp)
        {
            if (raid.InfantryReleased || raid.Kind == ArtilleryRaidKind.Siege) return;
            RefreshConstruction(raid, camp.Map);
            RefreshOperators(raid, camp);
            var operators = new HashSet<Pawn>(raid.Camp.Guns.Where(g => g.Operator != null).Select(g => g.Operator));
            var attackers = camp.ownedPawns.Where(p => !operators.Contains(p)).ToList();
            raid.InfantryReleased = true;
            raid.Phase = operators.Count == 0 ? ArtilleryPhase.Assaulting : ArtilleryPhase.Supporting;
            foreach (Pawn pawn in attackers) RestUtility.WakeUp(pawn);
            TransferToAssault(raid, camp, attackers);
        }

        internal static void RefreshOperators(ArtilleryRaid raid, Lord camp)
        {
            var released = new List<Pawn>();
            foreach (ArtilleryPlacement gun in raid.Camp.Guns)
            {
                Pawn pawn = gun.Operator;
                if (gun.Building == null || !gun.Building.Spawned || gun.Building.Faction != raid.Faction ||
                    pawn == null || !camp.ownedPawns.Contains(pawn) || !pawn.Spawned || pawn.Map != camp.Map ||
                    pawn.Faction != raid.Faction || pawn.Dead || pawn.Downed || pawn.InMentalState ||
                    !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                {
                    gun.Operator = null;
                    if (pawn != null && camp.ownedPawns.Contains(pawn) && pawn.Spawned && !pawn.Dead && pawn.Faction == raid.Faction)
                        released.Add(pawn);
                }
            }
            ReassignOperators(raid, camp);
            if (raid.InfantryReleased && raid.Kind != ArtilleryRaidKind.Siege)
                TransferToAssault(raid, camp, released.Where(p => !raid.Camp.Guns.Any(g => g.Operator == p)).ToList());
        }

        internal static void ActivateNativeAssault(ArtilleryRaid raid, Lord lord)
        {
            foreach (ArtilleryPlacement gun in raid.Camp.Guns) gun.Operator = null;
            raid.InfantryReleased = true;
            raid.Phase = ArtilleryPhase.Assaulting;
            SiegeRaidMap raids = lord.Map.GetComponent<SiegeRaidMap>();
            foreach (Pawn pawn in lord.ownedPawns)
            {
                SiegeParty party = raids.Party(pawn);
                if (party != null) raids.Issue(pawn, party);
            }
        }

        internal static void ReassignOperators(ArtilleryRaid raid, Lord camp, Pawn excluded = null)
        {
            if (raid.AssaultEnded || camp.CurLordToil is not LordToil_Siege toil) return;
            Lord assault = raid.AssaultLord;
            if (assault != null && !camp.Map.lordManager.lords.Contains(assault)) assault = null;
            foreach (ArtilleryPlacement gun in raid.Camp.Guns)
            {
                if (gun.Operator != null || gun.Building == null || !gun.Building.Spawned || gun.Building.Faction != raid.Faction) continue;
                Pawn pawn = raid.Members.Where(p => p != null && p != excluded && (p.GetLord() == camp || p.GetLord() == assault && assault != null) &&
                        p.Map == camp.Map && p.Faction == raid.Faction && CanOperate(p) &&
                        !raid.Camp.Guns.Any(g => g.Operator == p) &&
                        p.CanReserveAndReach(gun.Building, PathEndMode.InteractionCell, Danger.Deadly))
                    .OrderBy(p => p.Position.DistanceToSquared(gun.Cell)).FirstOrDefault();
                if (pawn == null) continue;
                gun.Operator = pawn;
                if (pawn.GetLord() == assault && assault != null)
                {
                    assault.RemovePawn(pawn);
                    if (pawn.jobs?.curJob?.lord == assault) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    // RemovePawn clears the old duty. Native Siege remembers
                    // existing duties before assigning them, so seed its role first.
                    NativeSiege.AssignGunnerDuty(toil, pawn);
                    camp.AddPawns(new[] { pawn }, updateDuties: false);
                }
            }
        }

        internal static void Abandon(ArtilleryRaid raid, Map map)
        {
            ArtilleryTargeting.ForgetRaid(raid);
            if (raid.Camp == null || raid.Phase == ArtilleryPhase.Abandoned) return;
            foreach (ArtilleryPlacement item in Placements(raid.Camp))
            {
                item.Operator = null;
                if (item.Building == null) continue;
                map.GetComponent<ArtilleryMap>().ForgetGun(item.Building);
                if (item.Building.Spawned && item.Building.Faction == raid.Faction) item.Building.SetFaction(null);
            }
            raid.Phase = ArtilleryPhase.Abandoned;
        }
    }
}
