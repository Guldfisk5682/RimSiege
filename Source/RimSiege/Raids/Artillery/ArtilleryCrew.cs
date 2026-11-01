using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    // The camp lord keeps construction/manning; the original strategy owns the
    // assault lord. No second builder roster or tick scheduler is introduced.
    internal static class ArtilleryCrew
    {
        internal static bool Pending(ArtilleryPlacement gun) => gun.Building == null &&
            (gun.Frame?.Spawned == true || gun.Blueprint?.Spawned == true);

        internal static bool HasConstruction(ArtilleryRaid raid) => raid.Camp.Guns.Any(Pending);
        internal static bool HasSupportConstruction(ArtilleryRaid raid) => raid.Camp.Guns.Any(g => g.IsSupport && Pending(g));
        internal static bool SupportReady(ArtilleryRaid raid) => raid.Camp.Guns.Any(g => g.IsSupport) &&
            raid.Camp.Guns.Where(g => g.IsSupport).All(g => g.Building?.Spawned == true);

        internal static bool CanBuild(LordToil_Siege toil, Pawn pawn) => pawn.Spawned && !pawn.Dead &&
            !pawn.Downed && !pawn.InMentalState && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
            NativeSiege.CanBuild(toil, pawn);

        internal static bool HasBuilder(Lord camp) => camp.CurLordToil is LordToil_Siege toil &&
            camp.ownedPawns.Any(p => CanBuild(toil, p));

        internal static HashSet<Pawn> Retained(ArtilleryRaid raid, Lord camp)
        {
            var crew = new HashSet<Pawn>(raid.Camp.Guns.Where(g => g.Operator != null).Select(g => g.Operator));
            if (!HasConstruction(raid)) return crew;
            var toil = (LordToil_Siege)camp.CurLordToil;
            bool autonomous = !((LordJob_Artillery)camp.LordJob).RequiresOperators;
            foreach (Pawn pawn in camp.ownedPawns)
                if (CanBuild(toil, pawn) && (autonomous || pawn.mindState.duty?.def == DutyDefOf.Build)) crew.Add(pawn);
            // Before vanilla's initial builder assignment, the reserved human
            // gunners can build; retain one native-eligible helper if none can.
            if (!crew.Any(p => CanBuild(toil, p)))
            {
                Pawn builder = camp.ownedPawns.Where(p => CanBuild(toil, p))
                    .OrderBy(p => p.Position.DistanceToSquared(raid.Camp.Center)).FirstOrDefault();
                if (builder != null) crew.Add(builder);
            }
            foreach (Pawn pawn in crew)
                if (CanBuild(toil, pawn)) NativeSiege.AssignBuilderDuty(toil, pawn);
            return crew;
        }

        internal static void RestoreBuilders(ArtilleryRaid raid, LordToil_Siege toil)
        {
            if (!raid.InfantryReleased || !HasConstruction(raid)) return;
            foreach (Pawn pawn in toil.lord.ownedPawns)
                if (CanBuild(toil, pawn)) NativeSiege.AssignBuilderDuty(toil, pawn);
        }

        // Called by the existing siege refresh, after construction/operator state
        // is refreshed. Completed guns release spare builders, not their gunners.
        internal static void Update(ArtilleryRaid raid, Lord camp)
        {
            if (!raid.InfantryReleased) return;
            if (HasConstruction(raid) && !HasBuilder(camp))
            {
                ArtilleryCamp.CancelUnfinished(raid);
                ArtilleryCamp.RefreshConstruction(raid, camp.Map);
                ArtilleryCamp.RefreshOperators(raid, camp);
            }
            if (HasConstruction(raid)) return;
            var operators = new HashSet<Pawn>(raid.Camp.Guns.Where(g => g.Operator != null).Select(g => g.Operator));
            raid.Phase = operators.Count != 0 ? ArtilleryPhase.Supporting : ArtilleryPhase.Assaulting;
            var attackers = camp.ownedPawns.Where(p => !operators.Contains(p)).ToList();
            foreach (Pawn pawn in attackers) RestUtility.WakeUp(pawn);
            ArtilleryCamp.TransferToAssault(raid, camp, attackers);
        }
    }
}
