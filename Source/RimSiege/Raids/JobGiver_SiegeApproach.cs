using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege.Raids
{
    public sealed class ThinkNode_ConditionalLadderDuty : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn) => LadderInventory.Get(pawn) != null ||
            pawn.Map?.GetComponent<SiegeRaidMap>().RecoveryFor(pawn) != null;
    }

    public sealed class JobGiver_SiegeApproach : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            SiegeRaidMap raids = pawn.Map.GetComponent<SiegeRaidMap>();
            if (!raids.Applies(pawn)) return null;
            SiegeParty party = raids.Party(pawn);
            if (!SiegeRoles.Eligible(pawn, party))
            {
                LadderInventory.Drop(pawn);
                return null;
            }
            // Kit carriers place ASAP and ignore combat; this JobGiver only
            // runs under ThinkNode_ConditionalLadderDuty (has kit or recovery).
            IntVec3 existingRoute = party.Planner.Destination(pawn, false);
            if (existingRoute.IsValid)
                return JobMaker.MakeJob(JobDefOf.Goto, existingRoute, 500, checkOverrideOnExpiry: true);

            if (LadderInventory.Get(pawn) != null) return party.Plan(pawn);
            Job recovery = raids.Recovery(pawn, party);
            if (recovery != null) return recovery;

            // Other troops (recovery waiters) hold while carriers place; once a
            // shared route exists they Goto it above. Carriers already Plan above.
            foreach (Pawn carrier in party.Members)
                if (carrier != null && carrier.Map == pawn.Map && LadderInventory.Get(carrier) != null &&
                    raids.Applies(carrier) && SiegeRoles.Eligible(carrier, party) && party.CanPlan(carrier))
                    return JobMaker.MakeJob(JobDefOf.Wait_Wander, 60);
            return null;
        }
    }
}
