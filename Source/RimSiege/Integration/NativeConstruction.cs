using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege.Integration
{
    internal static class NativeConstruction
    {
        // The same targeted work path used by vanilla prioritized work. It owns
        // worker eligibility, required capacities, blockers and resource jobs.
        private static readonly Func<JobGiver_Work, Pawn, WorkGiver, IntVec3, Job> Targeted =
            AccessTools.MethodDelegate<Func<JobGiver_Work, Pawn, WorkGiver, IntVec3, Job>>(
                AccessTools.Method(typeof(JobGiver_Work), "GiverTryGiveJobPrioritized"));

        internal static Job At(JobGiver_Work giver, Pawn pawn, WorkGiver worker, IntVec3 cell) =>
            Targeted(giver, pawn, worker, cell);
    }
}
