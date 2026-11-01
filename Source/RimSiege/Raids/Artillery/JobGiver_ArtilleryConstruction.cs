using System.Linq;
using RimSiege.Integration;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    // Runs before the ordinary Build work node. A failed gun-work query falls
    // through to native cover construction, needs and defense as before.
    public sealed class JobGiver_ArtilleryConstruction : JobGiver_Work
    {
        public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
        {
            if (pawn.GetLord()?.LordJob is not LordJob_Artillery artillery ||
                pawn.GetLord().CurLordToil is not LordToil_Siege ||
                pawn.mindState.duty?.def != DutyDefOf.Build) return ThinkResult.NoJob;
            ArtilleryRaid raid = artillery.Raid;
            if (!raid.SuppliesDelivered) return ThinkResult.NoJob;
            Job manning = ArtilleryGunnerPatch.AssignedJob(pawn, raid);
            if (manning != null) return new ThinkResult(manning, this, JobTag.Misc);
            if (pawn.workSettings == null || !pawn.workSettings.EverWork ||
                pawn.RaceProps.Humanlike && pawn.health.hediffSet.InLabor()) return ThinkResult.NoJob;
            var projects = raid.Camp.Guns.Where(ArtilleryCrew.Pending)
                .OrderBy(g => g.IsSupport ? 0 : 1)
                .ThenBy(g => pawn.Position.DistanceToSquared(g.Cell))
                .GroupBy(g => g.Role).ToList();
            if (projects.Count == 0) return ThinkResult.NoJob;
            // Native workgiver order applies within each role. Hauling to a
            // support blueprint takes priority over finishing a defense frame.
            foreach (var role in projects)
            foreach (WorkGiver worker in pawn.workSettings.WorkGiversInOrderNormal)
            {
                if (worker is not WorkGiver_ConstructFinishFrames && worker is not WorkGiver_ConstructDeliverResources) continue;
                var scanner = (WorkGiver_Scanner)worker;
                foreach (ArtilleryPlacement placement in role)
                {
                    Thing project = placement.Frame?.Spawned == true ? placement.Frame : placement.Blueprint;
                    if (!scanner.PotentialWorkThingRequest.Accepts(project) ||
                        !pawn.CanReach(project, scanner.PathEndMode, scanner.MaxPathDanger(pawn))) continue;
                    Job job = NativeConstruction.At(this, pawn, worker, project.Position);
                    if (job != null) return new ThinkResult(job, this, worker.def.tagToGive);
                }
            }
            return ThinkResult.NoJob;
        }
    }
}
