using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege.Raids
{
    public sealed class ThinkNode_ConditionalLadderCarrier : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn) => LadderInventory.Get(pawn) != null;
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
            if (party.Planner.Destination(pawn, true).IsValid) return null;
            IntVec3 existingRoute = party.Planner.Destination(pawn, false);
            if (existingRoute.IsValid)
                return JobMaker.MakeJob(JobDefOf.Goto, existingRoute, 500, checkOverrideOnExpiry: true);

            if (LadderInventory.Get(pawn) != null) return party.Plan(pawn);
            Job recovery = raids.Recovery(pawn, party);
            if (recovery != null) return recovery;

            // Other troops retain combat priority and use the shared route as soon
            // as it exists. Carrier plans run ahead of combat, including after pickup.
            foreach (Pawn carrier in party.Members)
                if (carrier != null && carrier.Map == pawn.Map && LadderInventory.Get(carrier) != null &&
                    raids.Applies(carrier) && SiegeRoles.Eligible(carrier, party) && party.CanPlan(carrier))
                    return JobMaker.MakeJob(JobDefOf.Wait_Wander, 60);
            return null;
        }
    }
}
