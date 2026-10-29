using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimSiege.Raids.Artillery
{
    // Keep the native letter, including faction, leader and relationship text;
    // describe the selected artillery plan in its strategy paragraph and title.
    internal static class ArtilleryRaidLetters
    {
        private static ArtilleryRaid CurrentRaid(IncidentParms parms)
        {
            ArtilleryRaid raid = RaidSpawnPatch.Current?.Artillery;
            return raid?.OriginalParms == parms ? raid : null;
        }

        private static string ArrivalText(RaidStrategyDef strategy, IncidentParms parms)
        {
            ArtilleryRaid raid = CurrentRaid(parms);
            return raid == null ? strategy.arrivalTextEnemy :
                ("RS_ArtilleryRaidArrival_" + raid.Kind).Translate().Resolve();
        }

        [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "GetLetterLabel")]
        private static class Label
        {
            private static void Postfix(IncidentParms parms, ref string __result)
            {
                ArtilleryRaid raid = CurrentRaid(parms);
                if (raid != null)
                    __result = ("RS_ArtilleryRaidLabel_" + raid.Kind).Translate(__result).Resolve();
            }
        }

        [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "GetLetterText")]
        private static class Text
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                FieldInfo arrival = AccessTools.Field(typeof(RaidStrategyDef), nameof(RaidStrategyDef.arrivalTextEnemy));
                foreach (CodeInstruction code in instructions)
                {
                    if (code.LoadsField(arrival))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(code).MoveBlocksFrom(code);
                        code.opcode = OpCodes.Call;
                        code.operand = AccessTools.Method(typeof(ArtilleryRaidLetters), nameof(ArrivalText));
                    }
                    yield return code;
                }
            }
        }
    }
}
