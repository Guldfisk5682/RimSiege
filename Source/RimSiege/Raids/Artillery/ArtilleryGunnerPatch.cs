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
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(JobGiver_ManTurrets), "TryGiveJob");

        private static bool Prefix(Pawn pawn, ref Job __result)
        {
            if (pawn.Map == null) return true;
            ArtilleryMap artillery = pawn.Map.GetComponent<ArtilleryMap>();
            ArtilleryRaid raid = artillery.ForPawn(pawn);
            if (raid == null || pawn.GetLord() != raid.CampLord) return true;
            __result = AssignedJob(pawn, raid);
            return false;
        }

        internal static Job AssignedJob(Pawn pawn, ArtilleryRaid raid)
        {
            foreach (ArtilleryPlacement slot in raid.Camp.Guns)
            {
                if (slot.Operator != pawn || slot.Building is not Building_TurretGunCE gun ||
                    !gun.Spawned || gun.Faction != raid.Faction ||
                    !pawn.CanReserveAndReach(gun, PathEndMode.InteractionCell, Danger.Deadly)) continue;
                CompAmmoUser ammo = gun.GetAmmo();
                if (ammo != null && ammo.UseAmmo && !ammo.HasAmmoOrMagazine && FindAmmo(pawn, gun) == null) continue;
                Job job = JobMaker.MakeJob(JobDefOf.ManTurret, gun);
                job.expiryInterval = 2000;
                job.checkOverrideOnExpire = true;
                return job;
            }
            return null;
        }

        private static void Postfix(JobGiver_ManTurrets __instance, Pawn pawn, ref Job __result)
        {
            if (pawn.Map == null) return;
            ArtilleryMap artillery = pawn.Map.GetComponent<ArtilleryMap>();
            ArtilleryRaid ownRaid = artillery.ForPawn(pawn);
            if (__result?.def == JobDefOf.ManTurret && __result.targetA.Thing is Building gun)
            {
                ArtilleryRaid gunRaid = artillery.ForGun(gun);
                if (gunRaid != null && (gunRaid != ownRaid || pawn.GetLord() != gunRaid.CampLord ||
                    !gunRaid.Camp.Guns.Exists(slot => slot.Building == gun && slot.Operator == pawn)))
                    __result = null;
            }
        }
    }
}
