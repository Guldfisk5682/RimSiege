using System.Collections.Generic;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    // Drop-pod the planned crewless battery after the vanilla mechanoid lord exists.
    internal static class MechBattery
    {
        private const int MortarInitiationTicks = 7200;
        private const int OpenDelay = 60;

        internal static void Deploy(ArtilleryRaid raid, Map map)
        {
            if (raid == null || map == null || raid.Kind != ArtilleryRaidKind.Mechanoid || raid.Camp?.Guns == null)
                return;
            if (raid.Members != null && raid.Members.Count > 0)
            {
                Lord lord = raid.Members[0]?.GetLord();
                if (lord != null)
                {
                    raid.AssaultLord = lord;
                    raid.AssaultCreated = true;
                }
            }
            foreach (ArtilleryPlacement placement in raid.Camp.Guns)
            {
                if (placement.Def == null || !placement.Cell.IsValid) continue;
                Thing thing = ThingMaker.MakeThing(placement.Def, placement.Stuff);
                thing.SetFaction(raid.Faction ?? Faction.OfMechanoids);
                thing.Rotation = Rot4.North;
                CompInitiatable initiatable = thing.TryGetComp<CompInitiatable>();
                if (initiatable != null)
                    initiatable.initiationDelayTicksOverride = MortarInitiationTicks;
                if (thing is Building_TurretGunCE turret && placement.Ammo is AmmoDef ammo &&
                    turret.CompAmmo != null)
                {
                    turret.CompAmmo.SelectedAmmo = ammo;
                    turret.CompAmmo.CurrentAmmo = ammo;
                }
                var info = new ActiveTransporterInfo
                {
                    openDelay = OpenDelay,
                    leaveSlag = false,
                    despawnPodBeforeSpawningThing = true,
                    spawnWipeMode = WipeMode.Vanish,
                    setRotation = Rot4.North
                };
                info.innerContainer.TryAdd(thing);
                DropPodUtility.MakeDropPodAt(placement.Cell, map, info, raid.Faction ?? Faction.OfMechanoids);
            }
            raid.Phase = ArtilleryPhase.Supporting;
        }
    }

    // Match landed drop-pod guns to the planned battery without going through ObserveBuilding.
    [HarmonyPatch(typeof(Building), nameof(Building.SpawnSetup))]
    internal static class MechBatterySpawnPatch
    {
        private static void Postfix(Building __instance, Map map)
        {
            if (map == null || __instance.Faction != Faction.OfMechanoids) return;
            if (__instance is not Building_TurretGunCE) return;
            ArtilleryMap artillery = map.GetComponent<ArtilleryMap>();
            if (artillery == null) return;
            foreach (ArtilleryRaid raid in artillery.ActiveRaids)
            {
                if (raid.Kind != ArtilleryRaidKind.Mechanoid || raid.Camp?.Guns == null) continue;
                foreach (ArtilleryPlacement gun in raid.Camp.Guns)
                {
                    if (gun.Building != null || gun.Def != __instance.def || gun.Cell != __instance.Position)
                        continue;
                    gun.Building = __instance;
                    artillery.TrackGun(__instance, raid);
                    return;
                }
            }
        }
    }
}
