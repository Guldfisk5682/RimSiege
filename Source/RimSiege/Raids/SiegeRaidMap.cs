using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using ProjectZ;
using RimSiege.Integration;
using RimSiege.Raids.Artillery;
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
        public int LadderQuotaPercent = 10;
        public List<Pawn> Members = new List<Pawn>();
        public List<Pawn> Operators = new List<Pawn>();
        internal IsoSiegePlanner Planner;
        private readonly Dictionary<Pawn, PlanStatus> jobs = new Dictionary<Pawn, PlanStatus>();
        private struct PlanStatus { internal int tick, version; internal bool available, placing; }

        internal Job Plan(Pawn pawn, bool placing = true)
        {
            int now = Find.TickManager.TicksGame, version = Planner.Version;
            if (jobs.TryGetValue(pawn, out PlanStatus cached) && cached.placing == placing && cached.version == version && now - cached.tick < RaidClimb.MemoTicks && !cached.available)
                return null;
            Job job = Planner.JobFor(pawn, placing);
            jobs[pawn] = new PlanStatus { tick = now, version = version, available = job != null, placing = placing };
            return job;
        }

        internal bool CanPlan(Pawn pawn) => jobs.TryGetValue(pawn, out PlanStatus status) &&
            status.placing && status.version == Planner.Version && Find.TickManager.TicksGame - status.tick < RaidClimb.MemoTicks
                ? status.available : Plan(pawn) != null;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref InitialCount, "initialCount");
            Scribe_Values.Look(ref Issued, "issued");
            Scribe_Values.Look(ref LadderQuotaPercent, "ladderQuotaPercent", 10);
            Scribe_Collections.Look(ref Members, "members", LookMode.Reference);
            Scribe_Collections.Look(ref Operators, "operators", LookMode.Reference);
        }
    }

    public sealed class SiegeRaidMap : MapComponent
    {
        private List<SiegeParty> parties = new List<SiegeParty>();
        private readonly Dictionary<Pawn, SiegeParty> membership = new Dictionary<Pawn, SiegeParty>();
        private readonly Dictionary<Pawn, LadderKit> receivers = new Dictionary<Pawn, LadderKit>();
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
            party.Planner = new IsoSiegePlanner(map, () => ReachEpoch, LadderTopClearance.Allows);
            foreach (Pawn pawn in party.Members)
                if (pawn != null) membership[pawn] = party;
        }

        internal SiegeParty Register(List<Pawn> pawns, Faction faction)
        {
            if (!Enabled || !IsoFloor.HasFloors(map)) return null;
            for (int i = parties.Count - 1; i >= 0; i--)
            {
                SiegeParty old = parties[i];
                if (map.GetComponent<ArtilleryMap>().HasActiveRaid(old.Id) ||
                    old.Members.Any(p => p != null && !p.Dead && p.SpawnedOrAnyParentSpawned && SiegeRoles.Ordinary(p.GetLord()))) continue;
                foreach (Pawn former in old.Members)
                    if (former != null) membership.Remove(former);
                parties.RemoveAt(i);
            }
            SiegeParty party = new SiegeParty
            {
                Id = nextId++, InitialCount = pawns.Count, Members = new List<Pawn>(pawns),
                LadderQuotaPercent = faction.def.humanlikeFaction && faction.def.categoryTag == "Tribal" ? 20 : 10
            };
            parties.Add(party);
            Index(party);
            List<Pawn> eligible = pawns.Where(p => SiegeRoles.Eligible(p, party)).ToList();
            eligible.Shuffle();
            foreach (Pawn pawn in eligible) Issue(pawn, party);
            return party;
        }

        internal void Issue(Pawn pawn, SiegeParty party)
        {
            if (party.Issued >= (party.InitialCount * party.LadderQuotaPercent + 99) / 100 ||
                LadderInventory.Get(pawn) != null || !SiegeRoles.Eligible(pawn, party)) return;
            GiveLadder(pawn, party);
        }

        // Artillery Constructoids always carry a ladder after build/interrupt transfer.
        internal void ForceIssue(Pawn pawn, SiegeParty party)
        {
            if (pawn == null || party == null || LadderInventory.Get(pawn) != null) return;
            if (!pawn.Spawned || pawn.Dead || pawn.Downed || pawn.inventory == null) return;
            GiveLadder(pawn, party);
        }

        private void GiveLadder(Pawn pawn, SiegeParty party)
        {
            LadderKit kit = (LadderKit)ThingMaker.MakeThing(SiegeDefOf.RS_LadderKit);
            kit.RaidId = party.Id;
            if (!pawn.inventory.innerContainer.TryAdd(kit, false))
            {
                kit.Destroy(DestroyMode.Vanish);
                return;
            }
            party.Issued++;
        }

        internal Job Recovery(Pawn pawn, SiegeParty party)
        {
            LadderKit kit = RecoveryFor(pawn);
            if (kit == null || kit.RaidId != party.Id) return null;
            if (!CanRecover(pawn, party) || kit.IsForbidden(pawn) ||
                !pawn.CanReserveAndReach(kit, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                Release(kit);
                return null;
            }
            Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, kit);
            job.count = 1;
            return job;
        }

        internal LadderKit RecoveryFor(Pawn pawn) => receivers.TryGetValue(pawn, out LadderKit kit) ? kit : null;

        internal void Release(LadderKit kit)
        {
            if (kit.Receiver == null) return;
            receivers.Remove(kit.Receiver);
            kit.Receiver = null;
        }

        private bool CanRecover(Pawn pawn, SiegeParty party) => pawn.Map == map && Applies(pawn) &&
            Party(pawn) == party && LadderInventory.Get(pawn) == null && SiegeRoles.Eligible(pawn, party) &&
            pawn.TryGetComp<CompSuppressable>()?.isSuppressed != true;

        private bool CanRecover(Pawn pawn, SiegeParty party, LadderKit kit, Dictionary<Pawn, bool> needsRoute)
        {
            if (!CanRecover(pawn, party) || kit.IsForbidden(pawn) ||
                !pawn.CanReserveAndReach(kit, PathEndMode.ClosestTouch, Danger.Deadly)) return false;
            if (!needsRoute.TryGetValue(pawn, out bool needs))
            {
                needs = !party.Planner.Destination(pawn, true).IsValid && !party.Planner.Destination(pawn, false).IsValid;
                needsRoute.Add(pawn, needs);
            }
            return needs;
        }

        private static bool Taking(Pawn pawn, LadderKit kit) => pawn.CurJobDef == JobDefOf.TakeInventory &&
            pawn.CurJob.targetA.Thing == kit;

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            bool recover = Dropped.Count != 0 && now % 60 == 0;
            bool recheckCarriers = Enabled && parties.Count != 0 && now % RaidClimb.MemoTicks == 0;
            if (!recover && !recheckCarriers) return;
            HashSet<Pawn> wake = new HashSet<Pawn>();
            if (recover)
            {
                Dictionary<Pawn, bool> needsRoute = new Dictionary<Pawn, bool>();
                foreach (LadderKit kit in Dropped)
                {
                    SiegeParty party = Party(kit.RaidId);
                    Pawn previous = kit.Receiver;
                    if (previous != null && (party == null || !CanRecover(previous, party, kit, needsRoute)))
                    {
                        Release(kit);
                        if (previous.Map == map && Applies(previous) && Taking(previous, kit)) wake.Add(previous);
                    }
                    if (party == null || !Enabled) continue;
                    if (kit.Receiver == null && now >= kit.RetryAt)
                    {
                        kit.RetryAt = now + 60;
                        Pawn receiver = party.Members.Where(p => p != null && RecoveryFor(p) == null && CanRecover(p, party) &&
                                !(p.CurJobDef == JobDefOf.TakeInventory && p.CurJob.targetA.Thing is LadderKit other && other != kit))
                            .OrderBy(p => IsoFloor.Distance3D(p.Position, kit.Position, map))
                            .FirstOrDefault(p => CanRecover(p, party, kit, needsRoute));
                        if (receiver != null)
                        {
                            kit.Receiver = receiver;
                            receivers.Add(receiver, kit);
                        }
                    }
                    Pawn selected = kit.Receiver;
                    if (selected != null && !Taking(selected, kit)) wake.Add(selected);
                }
            }
            if (recheckCarriers)
                foreach (SiegeParty party in parties)
                    foreach (Pawn pawn in party.Members)
                        if (pawn != null && pawn.Map == map && Party(pawn) == party && LadderInventory.Get(pawn) != null &&
                            Applies(pawn) && SiegeRoles.Eligible(pawn, party) &&
                            (pawn.CurJobDef == JobDefOf.AttackMelee || pawn.CurJobDef == JobDefOf.AttackStatic || pawn.CurJobDef == JobDefOf.Wait_Combat) &&
                            !(pawn.CurJob.jobGiver is JobGiver_SiegeApproach))
                            wake.Add(pawn);
            // Vanilla may forbid a kit after SpawnSetup; wake only after the drop has finished and enumeration has ended.
            foreach (Pawn pawn in wake) pawn.jobs.CheckForJobOverride();
        }

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
