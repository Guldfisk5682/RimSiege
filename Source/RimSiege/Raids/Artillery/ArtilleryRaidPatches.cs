using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimSiege.Integration;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    [HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo))]
    internal static class ArtilleryRaidPatches
    {
        [ThreadStatic] private static bool restoring;
        [ThreadStatic] private static ArtilleryRaid creatingAssault;
        [ThreadStatic] private static Map assaultMap;

        internal static Lord MakeOriginalLord(ArtilleryRaid raid, Map map, List<Pawn> pawns)
        {
            ArtilleryRaid previous = creatingAssault;
            Map previousMap = assaultMap;
            creatingAssault = raid;
            assaultMap = map;
            try
            {
                Lord lord = LordMaker.MakeNewLord(raid.Faction, MakeOriginalJob(raid, map, pawns), map, pawns);
                lord.inSignalLeave = raid.OriginalParms.inSignalEnd;
                QuestUtility.AddQuestTag(lord, raid.OriginalParms.questTag);
                return lord;
            }
            finally { creatingAssault = previous; assaultMap = previousMap; }
        }

        internal static LordJob MakeOriginalJob(ArtilleryRaid raid, Map map, List<Pawn> pawns)
        {
            if (raid.Kind == ArtilleryRaidKind.Siege) return new LordJob_AssaultColony(raid.Faction);
            bool previous = restoring;
            restoring = true;
            try
            {
                RaidStrategyWorker worker = raid.Strategy.Worker;
                return (LordJob)AccessTools.Method(worker.GetType(), "MakeLordJob").Invoke(worker,
                    new object[] { raid.OriginalParms, map, pawns, raid.RaidSeed });
            }
            finally { restoring = previous; }
        }

        private static float Adjust(float points, PawnsArrivalModeDef arrival, RaidStrategyDef strategy,
            Faction faction, PawnGroupKindDef group, IIncidentTarget target, RaidAgeRestrictionDef age, IncidentParms parms)
        {
            float adjusted = IncidentWorker_Raid.AdjustedRaidPoints(points, arrival, strategy, faction, group, target, age);
            if (RaidSpawnPatch.Current == null) return adjusted;
            ArtilleryRaid raid = ArtilleryRaidPolicy.Prepare(parms, points, adjusted);
            RaidSpawnPatch.Current.Artillery = raid;
            return raid?.PawnPoints ?? adjusted;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.AdjustedRaidPoints));
            foreach (CodeInstruction code in instructions)
            {
                if (code.Calls(original))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(code).MoveBlocksFrom(code);
                    code.operand = AccessTools.Method(typeof(ArtilleryRaidPatches), nameof(Adjust));
                }
                yield return code;
            }
        }

        // The strategic destination was already chosen by vanilla during this incident.
        // Keep the first real breach grid on that goal; later native replanning is unchanged.
        [HarmonyPatch(typeof(GenAI), nameof(GenAI.RandomRaidDest))]
        private static class InitialBreachDestination
        {
            private static bool Prefix(Map map, ref IntVec3 __result)
            {
                if (creatingAssault?.Kind != ArtilleryRaidKind.Breaching || map != assaultMap ||
                    !creatingAssault.Camp.Goal.InBounds(map)) return true;
                __result = creatingAssault.Camp.Goal;
                return false;
            }
        }

        [HarmonyPatch]
        private static class LordCreation
        {
            private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(RaidStrategyWorker), "MakeLordJob");

            private static bool Prefix(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed, ref LordJob __result)
            {
                ArtilleryRaid raid = RaidSpawnPatch.Current?.Artillery;
                if (restoring || raid == null || raid.OriginalParms != parms ||
                    (parms.pawnGroups != null && parms.pawnGroups.Values.Distinct().Count() > 1)) return true;
                raid.RaidSeed = raidSeed;
                map.GetComponent<ArtilleryMap>().Register(raid, pawns);
                __result = new LordJob_Artillery(raid);
                return false;
            }
        }
    }
}
