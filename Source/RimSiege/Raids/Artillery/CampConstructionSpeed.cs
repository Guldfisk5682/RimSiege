using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    // Scale only the native frame-construction speed read. Native stat caching,
    // materials, work requirements and completion remain unchanged.
    [HarmonyPatch]
    internal static class CampConstructionSpeed
    {
        private sealed class WorkRate
        {
            internal Lord Camp;
            internal Frame Frame;
            internal float Multiplier = 1f;
        }

        // Resolve the planned worksite once per native job. No raid/map scan is
        // added to the construction tick; entries expire with their job driver.
        private static readonly ConditionalWeakTable<JobDriver_ConstructFinishFrame, WorkRate> Rates = new();

        private static IEnumerable<MethodBase> TargetMethods() =>
            typeof(JobDriver_ConstructFinishFrame).GetNestedTypes(AccessTools.all)
                .SelectMany(AccessTools.GetDeclaredMethods)
                .Where(method => method.Name.Contains("<MakeNewToils>"));

        private static WorkRate Resolve(JobDriver_ConstructFinishFrame driver)
        {
            Pawn pawn = driver.pawn;
            var frame = driver.job.targetA.Thing as Frame;
            if (!CampConstructionMaterials.Applies(pawn, frame)) return new WorkRate();
            Lord camp = pawn.GetLord();
            return new WorkRate
            {
                Camp = camp, Frame = frame,
                Multiplier = ((LordJob_Artillery)camp.LordJob).Raid.ConstructionSpeedMultiplier
            };
        }

        private static float Speed(Thing thing, StatDef stat, bool applyPostProcess, int cacheStaleAfterTicks)
        {
            float native = thing.GetStatValue(stat, applyPostProcess, cacheStaleAfterTicks);
            if (thing is not Pawn pawn || pawn.jobs.curDriver is not JobDriver_ConstructFinishFrame driver ||
                pawn.GetLord()?.LordJob is not LordJob_Artillery) return native;
            WorkRate rate = Rates.GetValue(driver, Resolve);
            return rate.Camp != null && pawn.GetLord() == rate.Camp && rate.Camp.CurLordToil is LordToil_Siege &&
                pawn.Faction == rate.Frame.Faction && pawn.HostileTo(Faction.OfPlayer)
                ? native * rate.Multiplier : native;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo speed = AccessTools.Field(typeof(StatDefOf), nameof(StatDefOf.ConstructionSpeed));
            MethodInfo original = AccessTools.Method(typeof(StatExtension), nameof(StatExtension.GetStatValue),
                new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) });
            MethodInfo replacement = AccessTools.Method(typeof(CampConstructionSpeed), nameof(Speed));
            bool readingSpeed = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.LoadsField(speed)) readingSpeed = true;
                if (readingSpeed && instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    readingSpeed = false;
                }
                yield return instruction;
            }
        }
    }
}
