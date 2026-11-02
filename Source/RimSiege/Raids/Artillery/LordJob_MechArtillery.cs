using System;
using System.Linq;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    // The native siege graph owns preparation and interruption. Only crew rules
    // differ: constructoids build, autonomous guns retain no operators.
    public sealed class LordJob_MechArtillery : LordJob_Artillery
    {
        public LordJob_MechArtillery() { }
        internal LordJob_MechArtillery(ArtilleryRaid raid) : base(raid) { }

        internal override bool RequiresOperators => false;
        internal override Pawn ConstructionWorker => lord.ownedPawns.FirstOrDefault(p => p.Spawned && CanConstruct(p));
        internal override bool PrepareCrew() => ConstructionWorker != null;
        internal override bool CanBeBuilder(Pawn pawn, bool nativeResult) => CanConstruct(pawn);

        private static bool CanConstruct(Pawn pawn) => MechBattery.IsConstructoid(pawn) &&
            !pawn.Dead && !pawn.Downed && !pawn.InMentalState &&
            pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);

        internal override bool AssignBuilder(LordToil_Siege toil, Pawn pawn)
        {
            if (!CanConstruct(pawn))
            {
                NativeSiege.AssignDefenderDuty(toil, pawn);
                return true;
            }
            LordToilData_Siege data = NativeSiege.Data(toil);
            pawn.mindState.duty = new PawnDuty(DutyDefOf.Build, data.siegeCenter) { radius = data.baseRadius };
            pawn.workSettings ??= new Pawn_WorkSettings(pawn);
            pawn.workSettings.EnableAndInitialize();
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                if (work != WorkTypeDefOf.Construction) pawn.workSettings.Disable(work);
            pawn.workSettings.SetPriority(WorkTypeDefOf.Construction, 1);
            return true;
        }

        internal override void PrepareCleanup(LordToil_Siege toil)
        {
            // Native Cleanup neutralizes its owned buildings. Detach only our
            // completed guns before that step; do not reclaim captured buildings.
            foreach (ArtilleryPlacement gun in Raid.Camp.Guns)
                if (gun.Building != null && gun.Building.Spawned && gun.Building.Faction == Raid.Faction)
                    toil.lord.ownedBuildings.Remove(gun.Building);
        }

        internal override void IssueAssaultKit(Pawn pawn, SiegeParty party)
        {
            if (MechBattery.IsConstructoid(pawn)) Map.GetComponent<SiegeRaidMap>().ForceIssue(pawn, party);
            else base.IssueAssaultKit(pawn, party);
        }
    }

    // Earlier construction candidates saved the mechanical camp as the base
    // job. Reuse its serialized fields and graph with the specialized duties.
    [HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.GetBackCompatibleType))]
    internal static class MechArtillerySaveMigration
    {
        private static void Postfix(XmlNode node, ref Type __result)
        {
            if (__result == typeof(LordJob_Artillery) && node?["kind"]?.InnerText == nameof(ArtilleryRaidKind.Mechanoid))
                __result = typeof(LordJob_MechArtillery);
        }
    }
}
