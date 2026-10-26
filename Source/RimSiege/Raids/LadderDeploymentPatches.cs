using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ProjectZ;
using RimSiege.Integration;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege.Raids
{
    [HarmonyPatch(typeof(RaidClimb), nameof(RaidClimb.TryGive))]
    internal static class OrdinaryLadderSupplyPatch
    {
        private static bool Prefix(Pawn pawn, bool exit, ref Job __result)
        {
            if (exit || IsoSiegePlanner.Active != null || !SiegeRaidMap.Enabled || pawn?.Map == null ||
                !pawn.Map.GetComponent<SiegeRaidMap>().Applies(pawn)) return true;
            // Only the kit-backed planner opens new routes for these ordinary raids.
            // The original sapper jobs remain available as the final fallback.
            __result = null;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class LadderDeploymentPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(JobDriver_PlaceSiegeLadder)
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
            .SelectMany(AccessTools.GetDeclaredMethods)
            .Where(m => m.Name.StartsWith("<MakeNewToils>"));

        private static Building_SiegeLadder Deploy(IntVec3 foot, IntVec3 top, Faction faction, Map map,
            bool lift, bool allowBlocker, JobDriver_PlaceSiegeLadder driver)
        {
            Pawn pawn = driver.pawn;
            SiegeRaidMap raids = map.GetComponent<SiegeRaidMap>();
            LadderKit kit = LadderInventory.Get(pawn);
            if (raids.Applies(pawn) && kit == null) return null;
            Building_SiegeLadder building = LadderSite.Spawn(foot, top, faction, map, lift, allowBlocker);
            if (building != null && kit != null) LadderInventory.Consume(pawn, kit);
            return building;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo spawn = AccessTools.Method(typeof(LadderSite), nameof(LadderSite.Spawn));
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(spawn))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(__originalMethod.DeclaringType, "<>4__this"));
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(LadderDeploymentPatch), nameof(Deploy));
                }
                yield return c;
            }
        }
    }

    [HarmonyPatch]
    internal static class SiegeReachInvalidationPatch
    {
        private static readonly AccessTools.FieldRef<Reachability, Map> MapOf = AccessTools.FieldRefAccess<Reachability, Map>("map");
        private static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(Reachability))
            .Where(m => m.Name == nameof(Reachability.ClearCache) || m.Name == nameof(Reachability.ClearCacheFor) || m.Name == nameof(Reachability.ClearCacheForHostile));

        private static void Postfix(Reachability __instance)
        {
            Map map = MapOf(__instance);
            if (__instance == map.reachability)
            {
                SiegeRaidMap raids = map.GetComponent<SiegeRaidMap>();
                if (raids != null) raids.ReachEpoch++;
            }
        }
    }
}
