using RimWorld;
using ProjectZ;
using Verse;
using Verse.AI;

namespace RimSiege.Raids
{
    public sealed class ThinkNode_ConditionalLadderDuty : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn) => LadderInventory.Get(pawn) != null || LadderPerch.Get(pawn) != null ||
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
            // Carriers and perched clearers take priority over ordinary combat;
            // the later assault node also lets other troops reuse built routes.
            LadderPerch.Rec perch = LadderPerch.Get(pawn);
            if (perch?.atTop == true) return RaidVertical.ClearJob(pawn, perch);
            IntVec3 existingRoute = party.Planner.Destination(pawn, false);
            if (existingRoute.IsValid)
                return JobMaker.MakeJob(JobDefOf.Goto, existingRoute, 500, checkOverrideOnExpiry: true);

            if (LadderInventory.Get(pawn) != null) return party.Plan(pawn);
            if (party.Planner.HasClearance)
            {
                Job clearing = party.Plan(pawn, placing: false);
                if (clearing != null) return clearing;
            }
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
