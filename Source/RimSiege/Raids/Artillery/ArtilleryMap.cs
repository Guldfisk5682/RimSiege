using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    public sealed class ArtilleryMap : MapComponent
    {
        private List<ArtilleryRaid> raids = new List<ArtilleryRaid>();
        private readonly Dictionary<Pawn, ArtilleryRaid> members = new Dictionary<Pawn, ArtilleryRaid>();
        private readonly record struct GunOwner(ArtilleryRaid Raid, ArtilleryPlacement Placement);
        private readonly Dictionary<Thing, GunOwner> guns = new();
        private List<Building> orphanGuns = new List<Building>();
        private int nextKey = 1;

        public ArtilleryMap(Map map) : base(map) { }

        internal void Register(ArtilleryRaid raid, IEnumerable<Pawn> pawns)
        {
            if (raid.Key == 0) raid.Key = nextKey++;
            raid.Members = pawns.ToList();
            raids.Add(raid);
            Index(raid);
        }

        private void Index(ArtilleryRaid raid)
        {
            foreach (Pawn pawn in raid.Members)
                if (pawn != null) members[pawn] = raid;
            raid.RegisterBuildings();
        }

        internal ArtilleryRaid ForKey(int key) => raids.FirstOrDefault(r => r.Key == key);
        internal IEnumerable<ArtilleryRaid> ActiveRaids => raids.Where(r => r.Phase != ArtilleryPhase.Abandoned);
        internal ArtilleryRaid ForPawn(Pawn pawn) => pawn != null && members.TryGetValue(pawn, out ArtilleryRaid raid) ? raid : null;
        internal ArtilleryRaid ForGun(Thing gun) => gun != null && guns.TryGetValue(gun, out GunOwner owner) ? owner.Raid : null;
        internal ArtilleryPlacement ForGunPlacement(Thing gun) => gun != null && guns.TryGetValue(gun, out GunOwner owner) ? owner.Placement : null;
        internal ArtilleryRaid ForLord(Lord lord)
        {
            if (lord == null) return null;
            if (lord.LordJob is LordJob_Artillery camp) return ForKey(camp.RaidKey);
            return raids.FirstOrDefault(r => r.CampLord == lord || r.AssaultLord == lord);
        }
        internal bool HasActiveRaid(int id) => raids.Any(r => r.Id == id && r.Selected && r.Phase != ArtilleryPhase.Abandoned);
        internal bool IsOperator(Pawn pawn)
        {
            ArtilleryRaid raid = ForPawn(pawn);
            return raid?.Camp != null && raid.Phase != ArtilleryPhase.Abandoned &&
                raid.Camp.Guns.Any(g => g.Operator == pawn);
        }

        internal void TrackGun(Thing gun, ArtilleryRaid raid, ArtilleryPlacement placement) => guns[gun] = new GunOwner(raid, placement);
        internal void ForgetGun(Thing gun)
        {
            guns.Remove(gun);
            ArtilleryTargeting.ForgetGun(gun);
        }

        internal void Remove(ArtilleryRaid raid)
        {
            ArtilleryTargeting.ForgetRaid(raid);
            foreach (Pawn pawn in raid.Members)
                if (pawn != null && ForPawn(pawn) == raid) members.Remove(pawn);
            if (raid.Camp != null)
                foreach (ArtilleryPlacement gun in raid.Camp.Guns)
                    if (gun.Building != null) ForgetGun(gun.Building);
            raids.Remove(raid);
        }

        internal void NotifyLordRemoved(Lord removed)
        {
            ArtilleryRaid raid = ForLord(removed);
            if (raid == null) return;
            if (raid.AssaultLord == removed)
            {
                raid.AssaultLord = null;
                NotifyAssaultEnded(raid);
            }
            if (raid.CampLord == removed || removed.LordJob is LordJob_Artillery job && job.RaidKey == raid.Key)
                raid.CampLord = null;
            if (!ArtilleryCompletion.InProgress(raid)) FinishIfEnded(raid);
        }

        internal void NotifyAssaultEnded(ArtilleryRaid raid)
        {
            if (raid.AssaultEnded) return;
            raid.AssaultEnded = true;
            Lord camp = raid.CampLord;
            if (camp != null && map.lordManager.lords.Contains(camp)) camp.ReceiveMemo("ArtilleryRaidEnded");
            else ArtilleryCamp.Abandon(raid, map);
        }

        internal void FinishIfEnded(ArtilleryRaid raid)
        {
            if (raid == null || !raids.Contains(raid) ||
                (raid.CampLord != null && map.lordManager.lords.Contains(raid.CampLord)) ||
                (raid.AssaultLord != null && map.lordManager.lords.Contains(raid.AssaultLord))) return;
            if (raid.CompletionPending && !raid.CompletionSent)
            {
                raid.CompletionSent = true;
                QuestUtility.SendQuestTargetSignals(raid.CompletionTags, "AllEnemiesDefeated");
            }
            if (raid.Kind == ArtilleryRaidKind.Mechanoid)
                HandOffOrphanGuns(raid);
            SupportRoute.Invalidate(raid);
            Remove(raid);
        }

        private void HandOffOrphanGuns(ArtilleryRaid raid)
        {
            if (raid.Camp?.Guns == null) return;
            foreach (ArtilleryPlacement gun in raid.Camp.Guns)
            {
                Building building = gun.Building;
                if (building == null || building.Destroyed || !building.Spawned ||
                    building.Faction != Faction.OfMechanoids) continue;
                CompAmmoUser ammo = (building as Building_TurretGunCE)?.CompAmmo;
                if (ammo == null || ammo.EmptyMagazine)
                    building.TryGetComp<CompExplosive>()?.StartWick();
                else if (!orphanGuns.Contains(building))
                    orphanGuns.Add(building);
            }
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 60 != 0 || orphanGuns.Count == 0) return;
            for (int i = orphanGuns.Count - 1; i >= 0; i--)
            {
                Building building = orphanGuns[i];
                if (building.DestroyedOrNull() || building.Faction != Faction.OfMechanoids)
                {
                    orphanGuns.RemoveAt(i);
                    continue;
                }
                CompAmmoUser ammo = (building as Building_TurretGunCE)?.CompAmmo;
                if (ammo == null || ammo.EmptyMagazine)
                {
                    building.TryGetComp<CompExplosive>()?.StartWick();
                    orphanGuns.RemoveAt(i);
                }
            }
        }

        public override void FinalizeInit()
        {
            for (int i = raids.Count - 1; i >= 0; i--)
            {
                ArtilleryRaid raid = raids[i];
                Index(raid);
                if (raid.AssaultLord != null) raid.AssaultCreated = true;
                if (raid.AssaultCreated && !raid.AssaultEnded &&
                    (raid.AssaultLord == null || !map.lordManager.lords.Contains(raid.AssaultLord) ||
                     raid.AssaultLord.CurLordToil is LordToil_ExitMap || raid.AssaultLord.CurLordToil is LordToil_PanicFlee))
                    NotifyAssaultEnded(raid);
                FinishIfEnded(raid);
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref raids, "rimSiegeArtilleryRaids", LookMode.Deep);
            raids ??= new List<ArtilleryRaid>();
            Scribe_Collections.Look(ref orphanGuns, "rimSiegeArtilleryOrphanGuns", LookMode.Reference);
            orphanGuns ??= new List<Building>();
            orphanGuns.RemoveAll(b => b.DestroyedOrNull());
            Scribe_Values.Look(ref nextKey, "rimSiegeArtilleryNextKey", 1);
        }
    }

    [HarmonyPatch(typeof(LordManager), nameof(LordManager.RemoveLord))]
    internal static class ArtilleryLordRemovedPatch
    {
        private static void Postfix(Lord oldLord) => oldLord.Map.GetComponent<ArtilleryMap>().NotifyLordRemoved(oldLord);
    }

    [HarmonyPatch(typeof(Lord), nameof(Lord.GotoToil))]
    internal static class ArtilleryAssaultExitPatch
    {
        private static void Postfix(Lord __instance, LordToil newLordToil)
        {
            if (newLordToil is not LordToil_ExitMap && newLordToil is not LordToil_PanicFlee) return;
            ArtilleryMap raids = __instance.Map?.GetComponent<ArtilleryMap>();
            ArtilleryRaid raid = raids?.ForLord(__instance);
            if (raid != null && raid.AssaultLord == __instance && raid.Kind != ArtilleryRaidKind.Siege)
                raids.NotifyAssaultEnded(raid);
        }
    }
}
