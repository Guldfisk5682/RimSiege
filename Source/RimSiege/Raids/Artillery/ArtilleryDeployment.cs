using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    // One incident split, with pod landing as the only deferred deployment event.
    // Native duties, construction and paths own all work after this handoff.
    internal static class ArtilleryDeployment
    {
        internal static void Begin(ArtilleryRaid raid)
        {
            if (!raid.IndependentCrew || raid.InfantryReleased) return;
            Lord camp = raid.CampLord;
            var toil = camp.Graph.lordToils.OfType<LordToil_Siege>().Single();
            var specialists = new HashSet<Pawn>();
            if (raid.Breaches)
                specialists.UnionWith(camp.ownedPawns.Where(p => p.kindDef.isGoodBreacher));
            if (raid.Strategy.Worker is RaidStrategyWorker_ImmediateAttackSappers)
            {
                Pawn sapper = camp.ownedPawns.FirstOrDefault(SappersUtility.IsGoodSapper)
                    ?? camp.ownedPawns.FirstOrDefault(SappersUtility.IsGoodBackupSapper);
                if (sapper != null) specialists.Add(sapper);
            }

            var crew = new HashSet<Pawn>();
            if (((LordJob_Artillery)camp.LordJob).RequiresOperators)
            {
                ArtilleryCamp.ReserveOperators(raid, camp.ownedPawns.Where(p =>
                    !specialists.Contains(p) && ArtilleryCamp.OperatorCandidate(p)));
                crew.UnionWith(raid.Camp.Guns.Where(g => g.Operator != null).Select(g => g.Operator));
            }
            else crew.UnionWith(camp.ownedPawns.Where(MechBattery.IsConstructoid));

            bool Builder(Pawn pawn) => ArtilleryCrew.BuilderCandidate(toil, pawn);
            // Gunners build first. Reserve one native-eligible helper only when
            // that crew has no builder; never take the strategy's required lead.
            if (!crew.Any(Builder))
            {
                Pawn helper = camp.ownedPawns.Where(p => !specialists.Contains(p) && Builder(p))
                    .OrderBy(p => p.PositionHeld.DistanceToSquared(raid.Camp.Center)).FirstOrDefault();
                if (helper != null) crew.Add(helper);
            }
            if (!crew.Any(Builder) || !raid.Camp.Guns.Any(g => g.IsSupport))
            {
                crew.Clear();
                raid.Camp.Guns.Clear();
                raid.Camp.Cover.Clear();
            }

            raid.InfantryReleased = true;
            ArtilleryCamp.TransferToAssault(raid, camp, camp.ownedPawns.Where(p => !crew.Contains(p)).ToList());
            TryDeploy(raid);
        }

        internal static void PawnArrived(Pawn pawn, Map map)
        {
            ArtilleryRaid raid = map.GetComponent<ArtilleryMap>().ForPawn(pawn);
            if (raid != null && pawn.GetLord() == raid.CampLord) TryDeploy(raid);
        }

        internal static void TryDeploy(ArtilleryRaid raid)
        {
            if (!raid.IndependentCrew || !raid.InfantryReleased || raid.SuppliesDelivered || raid.CampLord == null ||
                raid.CampLord.CurLordToil is LordToil_Siege or LordToil_ExitMap or LordToil_PanicFlee) return;
            Lord camp = raid.CampLord;
            var siege = camp.Graph.lordToils.OfType<LordToil_Siege>().Single();
            if (!camp.ownedPawns.Any(p => ArtilleryCrew.CanBuild(siege, p))) return;
            // Enter the existing native preparation transition, so Init creates
            // the planned blueprints and native material/ammo drop list exactly once.
            camp.Graph.transitions.First(t => t.target == siege && t.sources.Contains(camp.CurLordToil)).Execute(camp);
        }
    }
}
