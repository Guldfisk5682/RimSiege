using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    public class LordJob_Artillery : LordJob_Siege
    {
        private int raidKey;
        private ArtilleryRaidKind kind;
        private bool pendingInterference;

        public LordJob_Artillery() { }

        internal LordJob_Artillery(ArtilleryRaid raid)
            : base(raid.Faction, raid.Camp.Center, raid.PawnPoints *
                DefDatabase<ArtilleryProfileDef>.AllDefsListForReading.First(p => p.kind == raid.Kind.ToString()).equipmentFraction)
        {
            raidKey = raid.Key;
            kind = raid.Kind;
        }

        internal int RaidKey => raidKey;
        internal ArtilleryRaid Raid => Map.GetComponent<ArtilleryMap>().ForKey(raidKey);
        internal bool IsNativeAssault => kind == ArtilleryRaidKind.Siege && lord?.CurLordToil is LordToil_AssaultColony;

        internal virtual bool RequiresOperators => true;
        internal virtual Pawn ConstructionWorker => null;
        internal virtual bool PrepareCrew() => ArtilleryCamp.AssignOperators(Raid, lord);
        internal virtual bool CanBeBuilder(Pawn pawn, bool nativeResult) => nativeResult;
        // Return true when the subclass has assigned the duty itself.
        internal virtual bool AssignBuilder(LordToil_Siege toil, Pawn pawn) => false;
        internal virtual void PrepareCleanup(LordToil_Siege toil) { }

        internal virtual void IssueAssaultKit(Pawn pawn, SiegeParty party) =>
            Map.GetComponent<SiegeRaidMap>().Issue(pawn, party);

        public override StateGraph CreateGraph()
        {
            StateGraph graph = base.CreateGraph();
            LordToil travel = graph.StartingToil;
            LordToil siege = graph.lordToils.OfType<LordToil_Siege>().Single();
            LordToil defend = graph.transitions.First(t => t.sources.Contains(travel) && t.target is LordToil_DefendPoint).target;
            // Native trigger data survives switches between its sources, so
            // local defense shares the preparation deadline with travel.
            graph.transitions.First(t => t.sources.Contains(travel) && t.target == siege).AddSource(defend);
            LordToil assault = graph.lordToils.OfType<LordToil_AssaultColony>().First();
            LordToil exit = graph.lordToils.OfType<LordToil_ExitMap>().First();
            graph.transitions.First(t => t.target == exit &&
                t.triggers.Any(trigger => trigger is Trigger_BecameNonHostileToPlayer)).AddSource(defend);
            siege.AddFailCondition(() => lord.ownedPawns.Count == 0);
            exit.AddFailCondition(() => lord.ownedPawns.Count == 0);
            if (kind != ArtilleryRaidKind.Siege)
            {
                LordToil failed = new LordToil_ArtilleryTransfer();
                graph.AddToil(failed);
                Transition siegeAssault = graph.transitions.First(t => t.target == assault && t.sources.Contains(siege));
                siegeAssault.target = failed;
                for (int i = 0; i < siegeAssault.triggers.Count; i++)
                    if (siegeAssault.triggers[i] is Trigger_PawnHarmed harmed)
                        siegeAssault.triggers[i] = new Trigger_ArtilleryInterference(harmed);
                // The native wake action runs after the transition; transfer empties this lord.
                siegeAssault.AddPreAction(new TransitionAction_WakeAll());
            }

            var raidEnded = new Transition(siege, exit);
            raidEnded.AddTrigger(new Trigger_Memo("ArtilleryRaidEnded"));
            graph.AddTransition(raidEnded);
            return graph;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref raidKey, "raidKey");
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref pendingInterference, "pendingInterference");
        }

        internal void NotifyInterference()
        {
            if (kind != ArtilleryRaidKind.Siege && Raid.Phase == ArtilleryPhase.Building && !Raid.InfantryReleased)
                pendingInterference = true;
        }

        public override void Notify_AddedToLord()
        {
            base.Notify_AddedToLord();
            Raid.CampLord = lord;
            Raid.Phase = ArtilleryPhase.Travelling;
        }

        public override void LordJobTick()
        {
            base.LordJobTick();
            ArtilleryRaid raid = Raid;
            if (pendingInterference && lord.CurLordToil is LordToil_Siege && !raid.InfantryReleased)
            {
                pendingInterference = false;
                ArtilleryCamp.CancelUnfinished(raid);
                ArtilleryCamp.ReleaseInfantry(raid, lord);
            }
            if (lord.CurLordToil is LordToil_Siege && raid.Phase == ArtilleryPhase.Assaulting && !raid.SuppliesDelivered)
                lord.ReceiveMemo("NoBuilders");
            else if (IsNativeAssault && !raid.InfantryReleased)
                ArtilleryCamp.ActivateNativeAssault(raid, lord);
        }

        public override void Notify_LordDestroyed()
        {
            base.Notify_LordDestroyed();
            ArtilleryRaid raid = Raid;
            if (raid != null && raid.CampLord == lord) ArtilleryCamp.Abandon(raid, Map);
        }
    }

    internal sealed class Trigger_ArtilleryInterference : Trigger
    {
        private readonly Trigger_PawnHarmed original;

        internal Trigger_ArtilleryInterference(Trigger_PawnHarmed original)
        {
            this.original = original;
            data = original.data;
            filters = original.filters;
        }

        public override void SourceToilBecameActive(Transition transition, LordToil previousToil)
        {
            original.data = data;
            original.SourceToilBecameActive(transition, previousToil);
        }

        public override bool ActivateOn(Lord lord, TriggerSignal signal)
        {
            original.data = data;
            if (!original.ActivateOn(lord, signal)) return false;
            var job = (LordJob_Artillery)lord.LordJob;
            ArtilleryRaid raid = job.Raid;
            if (raid.Phase == ArtilleryPhase.Building && !raid.InfantryReleased)
            {
                job.NotifyInterference();
                return false;
            }
            return true;
        }
    }

    internal sealed class LordToil_ArtilleryTransfer : LordToil
    {
        public override bool ShouldFail => lord.ownedPawns.Count == 0;
        public override void UpdateAllDuties() { }

        public override void Init()
        {
            base.Init();
            ArtilleryRaid raid = ((LordJob_Artillery)lord.LordJob).Raid;
            if (raid.Phase != ArtilleryPhase.Abandoned) raid.Phase = ArtilleryPhase.Assaulting;
            foreach (ArtilleryPlacement gun in raid.Camp.Guns) gun.Operator = null;
            ArtilleryCamp.TransferToAssault(raid, lord, lord.ownedPawns.ToList());
        }
    }
}
