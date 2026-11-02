using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimSiege.Integration;
using RimSiege.Raids.Artillery;
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
        internal sealed class Spawn { internal List<Pawn> Pawns; internal ArtilleryRaid Artillery; }
        [ThreadStatic] internal static Spawn Current;

        private static void Prefix(out Spawn __state)
        {
            __state = Current;
            Current = SiegeRaidMap.Enabled ? new Spawn() : null;
        }

        private static void Postfix(IncidentParms parms, bool __result)
        {
            if (!__result || Current?.Pawns == null || parms.target is not Map map) return;
            if (Current.Artillery != null || parms.raidStrategy.Worker is RaidStrategyWorker_ImmediateAttack ||
                parms.raidStrategy.Worker is RaidStrategyWorker_ImmediateAttackSmart ||
                parms.raidStrategy.Worker is RaidStrategyWorker_StageThenAttack ||
                Current.Pawns.Exists(p => SiegeRoles.Ordinary(p.GetLord())))
            {
                SiegeParty party = map.GetComponent<SiegeRaidMap>().Register(Current.Pawns, parms.faction);
                if (Current.Artillery != null && party != null) Current.Artillery.Id = party.Id;
                if (Current.Artillery != null) ArtilleryDeployment.Begin(Current.Artillery);
            }
        }

        private static void Finalizer(Spawn __state) => Current = __state;
    }

    [HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo))]
    internal static class RaidRosterPatch
    {
        // Vanilla arrives the roster inside TryGenerateRaidInfo before Postfix.
        // Wrap Arrive so Constructoids are on the list before drop-pods / edge walk-in.
        private static void ArriveWithConstructoids(PawnsArrivalModeWorker worker, List<Pawn> pawns,
            IncidentParms parms)
        {
            MechBattery.InjectConstructoids(RaidSpawnPatch.Current?.Artillery, pawns);
            worker.Arrive(pawns, parms);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo arrive = AccessTools.Method(typeof(PawnsArrivalModeWorker), nameof(PawnsArrivalModeWorker.Arrive));
            MethodInfo wrap = AccessTools.Method(typeof(RaidRosterPatch), nameof(ArriveWithConstructoids));
            foreach (CodeInstruction code in instructions)
            {
                if (code.Calls(arrive))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = wrap;
                }
                yield return code;
            }
        }

        private static void Postfix(IncidentParms parms, List<Pawn> pawns, bool __result, bool debugTest)
        {
            if (!__result || RaidSpawnPatch.Current == null) return;
            // SpawnThreats path can skip Arrive; inject then land any still-unspawned.
            int before = pawns.Count;
            if (!debugTest) MechBattery.InjectConstructoids(RaidSpawnPatch.Current.Artillery, pawns);
            if (pawns.Count > before && parms?.raidArrivalMode != null)
            {
                var late = pawns.Skip(before).Where(p => p != null && !p.Spawned).ToList();
                if (late.Count > 0) parms.raidArrivalMode.Worker.Arrive(late, parms);
            }
            RaidSpawnPatch.Current.Pawns = pawns;
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
            ArtilleryDeployment.PawnArrived(__instance, map);
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

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class AssignedGunnerPatch
    {
        private static void Postfix(Pawn ___pawn, Job newJob)
        {
            Pawn pawn = ___pawn;
            if (!SiegeRaidMap.Enabled || pawn.Map == null || pawn.CurJob != newJob || pawn.CurJobDef != JobDefOf.ManTurret) return;
            ArtilleryMap artillery = pawn.Map.GetComponent<ArtilleryMap>();
            ArtilleryRaid raid = artillery.ForPawn(pawn);
            if (raid != null && pawn.GetLord() == raid.CampLord && pawn.CurJob.targetA.Thing is Building gun && artillery.ForGun(gun) == raid)
            {
                foreach (ArtilleryPlacement slot in raid.Camp.Guns)
                    if (slot.Operator == pawn) slot.Operator = null;
                foreach (ArtilleryPlacement slot in raid.Camp.Guns)
                    if (slot.Building == gun) { slot.Operator = pawn; break; }
            }
            SiegeParty party = pawn.Map.GetComponent<SiegeRaidMap>().Party(pawn);
            if (party != null)
            {
                if (raid == null && !party.Operators.Contains(pawn))
                    party.Operators.Add(pawn);
                LadderInventory.Drop(pawn);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), "CleanupCurrentJob")]
    internal static class ReleasedGunnerPatch
    {
        private static void Prefix(Pawn ___pawn)
        {
            if (___pawn.Map != null && ___pawn.CurJobDef == JobDefOf.ManTurret)
                ___pawn.Map.GetComponent<SiegeRaidMap>().Party(___pawn)?.Operators.Remove(___pawn);
        }
    }
}
