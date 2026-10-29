using System;
using System.Collections.Generic;
using System.Reflection;
using CombatExtended;
using CombatExtended.Compatibility;
using HarmonyLib;
using RimSiege.Integration;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    // Keep the native manning job, while restricting this raid's operators to their assigned guns.
    [HarmonyPatch]
    internal static class ArtilleryGunnerPatch
    {
        private static readonly Func<Pawn, Thing, Thing> FindAmmo = AccessTools.MethodDelegate<Func<Pawn, Thing, Thing>>(
            AccessTools.Method(typeof(JobGiver_ManTurretsNearPointCE), "FindAmmoForTurret"));
        private static readonly Func<JobGiver_ManTurrets, Pawn, IntVec3> GetRoot =
            AccessTools.MethodDelegate<Func<JobGiver_ManTurrets, Pawn, IntVec3>>(AccessTools.Method(typeof(JobGiver_ManTurrets), "GetRoot"));

        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(JobGiver_ManTurrets), "TryGiveJob");

        private static bool Prefix(Pawn pawn, ref Job __result)
        {
            if (pawn.Map == null) return true;
            ArtilleryMap artillery = pawn.Map.GetComponent<ArtilleryMap>();
            ArtilleryRaid raid = artillery.ForPawn(pawn);
            if (raid == null || raid.Kind == ArtilleryRaidKind.Siege || !raid.InfantryReleased ||
                pawn.GetLord() != raid.CampLord || !artillery.IsOperator(pawn)) return true;
            __result = null;
            foreach (ArtilleryPlacement slot in raid.Camp.Guns)
            {
                if (slot.Operator != pawn || slot.Building is not Building_TurretGunCE gun ||
                    !gun.Spawned || gun.Faction != raid.Faction || artillery.ForGun(gun) != raid ||
                    !pawn.CanReserveAndReach(gun, PathEndMode.InteractionCell, Danger.Deadly)) continue;
                CompAmmoUser ammo = gun.GetAmmo();
                if (ammo != null && ammo.UseAmmo && !ammo.HasAmmoOrMagazine && FindAmmo(pawn, gun) == null) continue;
                __result = JobMaker.MakeJob(JobDefOf.ManTurret, gun);
                __result.expiryInterval = 2000;
                __result.checkOverrideOnExpire = true;
                break;
            }
            return false;
        }

        private static void Postfix(JobGiver_ManTurrets __instance, Pawn pawn, ref Job __result)
        {
            if (pawn.Map == null) return;
            ArtilleryMap artillery = pawn.Map.GetComponent<ArtilleryMap>();
            ArtilleryRaid ownRaid = artillery.ForPawn(pawn);
            bool fixedCrew = ownRaid != null && ownRaid.Kind != ArtilleryRaidKind.Siege && ownRaid.InfantryReleased;
            if (__result?.def == JobDefOf.ManTurret && __result.targetA.Thing is Building gun)
            {
                ArtilleryRaid gunRaid = artillery.ForGun(gun);
                if (gunRaid != null && (gunRaid != ownRaid || pawn.GetLord() != gunRaid.CampLord ||
                    (fixedCrew && !gunRaid.Camp.Guns.Exists(slot => slot.Building == gun && slot.Operator == pawn))))
                    __result = null;
            }

            // CE's native node requires loose ammo even for a loaded turret or
            // disabled ammo system. Reuse its search only for these managed guns.
            if (__result != null || ownRaid == null || fixedCrew || pawn.GetLord() != ownRaid.CampLord) return;
            Thing found = pawn.Map.GetComponent<TurretTracker>().ClosestTurret(GetRoot(__instance, pawn),
                PathEndMode.InteractionCell, TraverseParms.For(pawn), __instance.maxDistFromPoint, thing =>
                {
                    if (thing is not Building_TurretGunCE turret || artillery.ForGun(turret) != ownRaid ||
                        turret.Faction != ownRaid.Faction || !turret.def.hasInteractionCell ||
                        !turret.def.HasComp(typeof(CompMannable)) || !pawn.CanReserve(turret)) return false;
                    CompAmmoUser ammo = turret.GetAmmo();
                    return ammo != null && (!ammo.UseAmmo || ammo.HasAmmoOrMagazine);
                });
            if (found == null) return;
            __result = JobMaker.MakeJob(JobDefOf.ManTurret, found);
            __result.expiryInterval = 2000;
            __result.checkOverrideOnExpire = true;
        }
    }
}
