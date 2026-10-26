using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using ProjectZ;
using RimSiege.Integration;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.Raids
{
    public sealed class SiegeParty : IExposable
    {
        public int Id;
        public int InitialCount;
        public int Issued;
        public List<Pawn> Members = new List<Pawn>();
        public List<Pawn> Operators = new List<Pawn>();
        internal IsoSiegePlanner Planner;
        private readonly Dictionary<Pawn, PlanStatus> jobs = new Dictionary<Pawn, PlanStatus>();
        private struct PlanStatus { internal int tick, version; internal bool available; }

        internal Job Plan(Pawn pawn)
        {
            int now = Find.TickManager.TicksGame, version = Planner.Version;
            if (jobs.TryGetValue(pawn, out PlanStatus cached) && cached.version == version && now - cached.tick < RaidClimb.MemoTicks && !cached.available)
                return null;
            Job job = Planner.JobFor(pawn);
            jobs[pawn] = new PlanStatus { tick = now, version = version, available = job != null };
            return job;
        }

        internal bool CanPlan(Pawn pawn) => jobs.TryGetValue(pawn, out PlanStatus status) &&
            status.version == Planner.Version && Find.TickManager.TicksGame - status.tick < RaidClimb.MemoTicks
                ? status.available : Plan(pawn) != null;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref InitialCount, "initialCount");
            Scribe_Values.Look(ref Issued, "issued");
            Scribe_Collections.Look(ref Members, "members", LookMode.Reference);
            Scribe_Collections.Look(ref Operators, "operators", LookMode.Reference);
        }
    }

    public sealed class SiegeRaidMap : MapComponent
    {
        private List<SiegeParty> parties = new List<SiegeParty>();
        private readonly Dictionary<Pawn, SiegeParty> membership = new Dictionary<Pawn, SiegeParty>();
        internal readonly HashSet<LadderKit> Dropped = new HashSet<LadderKit>();
        private int nextId = 1;
        internal int ReachEpoch;

        public SiegeRaidMap(Map map) : base(map) { }

        internal static bool Enabled => RimSiegeMod.Settings?.EnhancedAssault == true;
        internal SiegeParty Party(Pawn pawn) => membership.TryGetValue(pawn, out SiegeParty party) ? party : null;
        internal SiegeParty Party(int id) => parties.FirstOrDefault(p => p.Id == id);

        internal bool Applies(Pawn pawn) => Enabled && Party(pawn) != null && SiegeRoles.Ordinary(pawn.GetLord()) &&
            pawn.mindState?.duty?.def == DutyDefOf.AssaultColony && !pawn.Dead && !pawn.Downed;

        private void Index(SiegeParty party)
        {
            party.Planner = new IsoSiegePlanner(map, () => ReachEpoch);
            foreach (Pawn pawn in party.Members)
                if (pawn != null) membership[pawn] = party;
        }

        internal void Register(List<Pawn> pawns)
        {
            if (!Enabled || !IsoFloor.HasFloors(map)) return;
            for (int i = parties.Count - 1; i >= 0; i--)
            {
                SiegeParty old = parties[i];
                if (old.Members.Any(p => p != null && !p.Dead && p.SpawnedOrAnyParentSpawned && SiegeRoles.Ordinary(p.GetLord()))) continue;
                foreach (Pawn former in old.Members)
                    if (former != null) membership.Remove(former);
                parties.RemoveAt(i);
            }
            SiegeParty party = new SiegeParty { Id = nextId++, InitialCount = pawns.Count, Members = new List<Pawn>(pawns) };
            parties.Add(party);
            Index(party);
            List<Pawn> eligible = pawns.Where(p => SiegeRoles.Eligible(p, party)).ToList();
            eligible.Shuffle();
            foreach (Pawn pawn in eligible) Issue(pawn, party);
        }

        internal void Issue(Pawn pawn, SiegeParty party)
        {
            if (party.Issued >= (party.InitialCount + 9) / 10 || LadderInventory.Get(pawn) != null || !SiegeRoles.Eligible(pawn, party)) return;
            LadderKit kit = (LadderKit)ThingMaker.MakeThing(SiegeDefOf.RS_LadderKit);
            kit.RaidId = party.Id;
            pawn.inventory.innerContainer.TryAdd(kit, false);
            party.Issued++;
        }

        internal Job Recovery(Pawn pawn, SiegeParty party)
        {
            foreach (LadderKit kit in Dropped)
            {
                if (kit.RaidId != party.Id) continue;
                if (kit.Receiver != null && !CanRecover(kit.Receiver, party)) kit.Receiver = null;
                int now = Find.TickManager.TicksGame;
                if (kit.Receiver == null && now >= kit.RetryAt)
                {
                    kit.RetryAt = now + 60;
                    kit.Receiver = party.Members.Where(p => p != null && CanRecover(p, party))
                        .OrderBy(p => IsoFloor.Distance3D(p.Position, kit.Position, map))
                        .FirstOrDefault(p => p.CanReserveAndReach(kit, PathEndMode.ClosestTouch, Danger.Deadly));
                }
                if (kit.Receiver != pawn) continue;
                Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, kit);
                job.count = 1;
                return job;
            }
            return null;
        }

        private bool CanRecover(Pawn pawn, SiegeParty party) => pawn.Map == map && Applies(pawn) &&
            LadderInventory.Get(pawn) == null && SiegeRoles.Eligible(pawn, party) && !Fighting(pawn) &&
            pawn.TryGetComp<CompSuppressable>()?.isSuppressed != true;

        internal static bool Fighting(Pawn pawn) => pawn.CurJobDef == JobDefOf.AttackStatic || pawn.CurJobDef == JobDefOf.AttackMelee;

        public override void FinalizeInit()
        {
            foreach (SiegeParty party in parties)
            {
                Index(party);
                foreach (Pawn pawn in party.Members)
                    if (pawn != null) LadderInventory.Restore(pawn);
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref parties, "rimSiegeParties", LookMode.Deep);
            parties ??= new List<SiegeParty>(); // Saves made before this optional feature have no party records.
            Scribe_Values.Look(ref nextId, "rimSiegeNextParty", 1);
        }
    }
}
