using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimSiege.Integration;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.Raids
{
    // Capture the whole incident before MakeLords splits it into squads. Issue
    // kits after duties exist, once for the raid, rather than once per lord.
    [HarmonyPatch(typeof(IncidentWorker_Raid), "TryExecuteWorker")]
    internal static class RaidSpawnPatch
    {
        internal sealed class Spawn { internal List<Pawn> Pawns; }
        [ThreadStatic] internal static Spawn Current;

        private static void Prefix(out Spawn __state)
        {
            __state = Current;
            Current = SiegeRaidMap.Enabled ? new Spawn() : null;
        }

        private static void Postfix(IncidentParms parms, bool __result)
        {
            if (__result && Current?.Pawns != null && parms.target is Map map &&
                (parms.raidStrategy.Worker is RaidStrategyWorker_ImmediateAttack ||
                 parms.raidStrategy.Worker is RaidStrategyWorker_ImmediateAttackSmart ||
                 Current.Pawns.Exists(p => SiegeRoles.Ordinary(p.GetLord()))))
                map.GetComponent<SiegeRaidMap>().Register(Current.Pawns);
        }

        private static void Finalizer(Spawn __state) => Current = __state;
    }

    [HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo))]
    internal static class RaidRosterPatch
    {
        private static void Postfix(List<Pawn> pawns, bool __result)
        {
            if (__result && RaidSpawnPatch.Current != null) RaidSpawnPatch.Current.Pawns = pawns;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    internal static class CarrierSpawnPatch
    {
        private static void Postfix(Pawn __instance, Map map)
        {
            LadderInventory.Restore(__instance);
            SiegeRaidMap raids = map.GetComponent<SiegeRaidMap>();
            SiegeParty party = raids.Party(__instance);
            if (SiegeRaidMap.Enabled && party != null) raids.Issue(__instance, party);
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    internal static class CarrierDownedPatch
    {
        private static void Prefix(Pawn ___pawn, out LadderKit __state) => __state = LadderInventory.Get(___pawn);

        private static void Postfix(Pawn ___pawn, LadderKit __state)
        {
            LadderInventory.Drop(___pawn);
            if (__state?.Spawned == true) __state.SetForbidden(false);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class CarrierDeathPatch
    {
        private static void Prefix(Pawn __instance) => LadderInventory.Drop(__instance);
    }

    [HarmonyPatch(typeof(Pawn), "get_LabelPrefix")]
    internal static class CarrierLabelPatch
    {
        private static void Postfix(Pawn __instance, ref string __result)
        {
            if (SiegeRaidMap.Enabled && Current.ProgramState == ProgramState.Playing && LadderInventory.Get(__instance) != null &&
                __instance.Faction?.HostileTo(Faction.OfPlayer) == true)
                __result += "RS_LadderCarrier".Translate() + " ";
        }
    }

    [HarmonyPatch(typeof(ThingOwner), "NotifyAdded")]
    internal static class CarrierInventoryAddedPatch
    {
        private static void Postfix(ThingOwner __instance, Thing item)
        {
            if (item is LadderKit kit && __instance.Owner is Pawn_InventoryTracker inventory)
                LadderInventory.Added(inventory.pawn, kit);
        }
    }

    [HarmonyPatch(typeof(ThingOwner), "NotifyRemoved")]
    internal static class CarrierInventoryRemovedPatch
    {
        private static void Postfix(ThingOwner __instance, Thing item)
        {
            if (item is LadderKit && __instance.Owner is Pawn_InventoryTracker inventory)
                LadderInventory.Removed(inventory.pawn);
        }
    }

    [HarmonyPatch]
    internal static class AssignedGunnerPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(JobGiver_ManTurrets), "TryGiveJob");

        private static void Postfix(Pawn pawn, Job __result)
        {
            if (__result == null || !SiegeRaidMap.Enabled) return;
            SiegeParty party = pawn.Map.GetComponent<SiegeRaidMap>().Party(pawn);
            if (party != null)
            {
                if (!party.Operators.Contains(pawn)) party.Operators.Add(pawn);
                LadderInventory.Drop(pawn);
            }
        }
    }
}
