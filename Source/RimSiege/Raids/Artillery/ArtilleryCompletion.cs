using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    // Preserve the native signal's timing across both lords of one incident.
    // The scope survives RemoveLord, which occurs before Lord.Destroy emits it.
    internal static class ArtilleryCompletion
    {
        internal sealed class Scope
        {
            internal Scope Previous;
            internal ArtilleryRaid Raid;
            internal ArtilleryMap Map;
        }

        [ThreadStatic] private static Scope current;

        internal static bool InProgress(ArtilleryRaid raid)
        {
            for (Scope scope = current; scope != null; scope = scope.Previous)
                if (scope.Raid == raid) return true;
            return false;
        }

        private static void SendOrDefer(List<string> tags, string signal)
        {
            ArtilleryRaid raid = current?.Raid;
            if (raid == null || signal != "AllEnemiesDefeated")
            {
                QuestUtility.SendQuestTargetSignals(tags, signal);
                return;
            }
            raid.CompletionPending = true;
            if (tags == null) return;
            foreach (string tag in tags)
                if (tag != null && !raid.CompletionTags.Contains(tag)) raid.CompletionTags.Add(tag);
        }

        [HarmonyPatch(typeof(Lord), "Destroy")]
        private static class LordDestroy
        {
            private static void Prefix(Lord __instance, out Scope __state)
            {
                ArtilleryMap map = __instance.Map?.GetComponent<ArtilleryMap>();
                __state = new Scope { Previous = current, Map = map, Raid = map?.ForLord(__instance) };
                current = __state;
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo original = AccessTools.Method(typeof(QuestUtility), nameof(QuestUtility.SendQuestTargetSignals),
                    new[] { typeof(List<string>), typeof(string) });
                MethodInfo replacement = AccessTools.Method(typeof(ArtilleryCompletion), nameof(SendOrDefer));
                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.Calls(original)) instruction.operand = replacement;
                    yield return instruction;
                }
            }

            private static Exception Finalizer(Exception __exception, Scope __state)
            {
                if (__state == null) return __exception;
                current = __state.Previous;
                if (__state.Raid != null && !InProgress(__state.Raid)) __state.Map.FinishIfEnded(__state.Raid);
                return __exception;
            }
        }
    }
}
