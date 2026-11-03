using System.Collections.Generic;
using RimSiege.Integration;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    internal enum ArtilleryRaidKind { Ordinary, Breaching, Siege, Mechanoid }
    internal enum ArtilleryPhase { Planned, Travelling, Building, Supporting, Assaulting, Abandoned }
    // Raid policy is separate from CE's physical direct/indirect firing mode.
    public enum ArtilleryGunRole { Support, CampDefense }

    // One record belongs to the original incident, even when its pawns later use different lords.
    internal sealed class ArtilleryRaid : IExposable
    {
        internal int Id, Key, RaidSeed;
        internal int IncidentKey;
        internal ArtilleryRaidKind Kind;
        internal ArtilleryPhase Phase;
        internal float RaidPoints, PawnPoints;
        internal float ConstructionSpeedMultiplier = 1f;
        internal Faction Faction;
        internal RaidStrategyDef Strategy;
        internal bool Breaches => Strategy?.Worker is RaidStrategyWorker_ImmediateAttackBreaching;
        internal IncidentParms OriginalParms;
        internal List<Pawn> Members = new List<Pawn>();
        internal CampPlan Camp;
        internal bool Rolled, Selected, SuppliesDelivered;
        internal bool InfantryReleased, AssaultCreated, AssaultEnded, CompletionSent, CompletionPending;
        internal bool IndependentCrew;
        internal int ConstructoidCount;
        internal List<string> CompletionTags = new List<string>();
        internal Lord CampLord, AssaultLord;

        public ArtilleryRaid() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref Key, "key");
            Scribe_Values.Look(ref IncidentKey, "incidentKey");
            Scribe_Values.Look(ref RaidSeed, "raidSeed");
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Phase, "phase");
            Scribe_Values.Look(ref RaidPoints, "raidPoints");
            Scribe_Values.Look(ref PawnPoints, "pawnPoints");
            Scribe_Values.Look(ref ConstructionSpeedMultiplier, "constructionSpeedMultiplier", 1f);
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Defs.Look(ref Strategy, "strategy");
            Scribe_Deep.Look(ref OriginalParms, "originalParms");
            Scribe_Collections.Look(ref Members, "members", LookMode.Reference);
            Scribe_Deep.Look(ref Camp, "camp");
            Scribe_Values.Look(ref Rolled, "rolled");
            Scribe_Values.Look(ref Selected, "selected");
            Scribe_Values.Look(ref SuppliesDelivered, "suppliesDelivered");
            Scribe_Values.Look(ref InfantryReleased, "infantryReleased");
            Scribe_Values.Look(ref IndependentCrew, "independentCrew");
            Scribe_Values.Look(ref AssaultCreated, "assaultCreated");
            Scribe_Values.Look(ref AssaultEnded, "assaultEnded");
            Scribe_Values.Look(ref CompletionSent, "completionSent");
            Scribe_Values.Look(ref CompletionPending, "completionPending");
            Scribe_Values.Look(ref ConstructoidCount, "constructoidCount");
            Scribe_Collections.Look(ref CompletionTags, "completionTags", LookMode.Value);
            CompletionTags ??= new List<string>();
            Scribe_References.Look(ref CampLord, "campLord");
            Scribe_References.Look(ref AssaultLord, "assaultLord");
        }

        internal void RegisterBuildings()
        {
            if (Camp == null || Phase == ArtilleryPhase.Abandoned) return;
            foreach (ArtilleryPlacement gun in Camp.Guns)
                if (gun.Building != null && gun.Building.Spawned && !gun.Building.Destroyed && gun.Building.Faction == Faction)
                    gun.Building.Map.GetComponent<ArtilleryMap>().TrackGun(gun.Building, this, gun);
        }
    }

    internal sealed class CampPlan : IExposable
    {
        internal IntVec3 Center = IntVec3.Invalid, Goal = IntVec3.Invalid;
        // The selected tactical objective, not a retained wall/query snapshot.
        internal IntVec3 BreachAnchor = IntVec3.Invalid;
        internal List<ArtilleryPlacement> Guns = new List<ArtilleryPlacement>();
        internal List<ArtilleryPlacement> Cover = new List<ArtilleryPlacement>();

        public CampPlan() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Center, "center");
            Scribe_Values.Look(ref Goal, "goal");
            Scribe_Values.Look(ref BreachAnchor, "breachAnchor", IntVec3.Invalid);
            Scribe_Collections.Look(ref Guns, "guns", LookMode.Deep);
            Scribe_Collections.Look(ref Cover, "cover", LookMode.Deep);
        }
    }

    internal sealed class ArtilleryPlacement : IExposable
    {
        internal ArtilleryGunRole Role;
        internal bool IsSupport => Role == ArtilleryGunRole.Support;
        internal bool IsDirectSupport => IsSupport && CeArtillerySight.IsDirectFire(Def, Ammo);
        internal bool NeedsOperator => Def.HasComp(typeof(CompMannable));
        internal int FormationGroup => !IsSupport ? 2 : IsDirectSupport ? 0 : 1;
        internal ThingDef Def, Stuff, Ammo;
        internal IntVec3 Cell = IntVec3.Invalid;
        internal Rot4 Rotation = Rot4.North;
        internal Pawn Operator;
        internal Blueprint_Build Blueprint;
        internal Frame Frame;
        internal Building Building;

        public ArtilleryPlacement() { }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref Def, "def");
            Scribe_Defs.Look(ref Stuff, "stuff");
            Scribe_Defs.Look(ref Ammo, "ammo");
            Scribe_Values.Look(ref Role, "role", ArtilleryGunRole.Support);
            Scribe_Values.Look(ref Cell, "cell");
            Scribe_Values.Look(ref Rotation, "rotation", Rot4.North);
            Scribe_References.Look(ref Operator, "operator");
            Scribe_References.Look(ref Blueprint, "blueprint");
            Scribe_References.Look(ref Frame, "frame");
            Scribe_References.Look(ref Building, "building");
        }
    }

    internal static class ArtilleryRegistry
    {
        internal static ArtilleryRaid Find(Building gun) => gun?.Map?.GetComponent<ArtilleryMap>().ForGun(gun);
        internal static ArtilleryPlacement Placement(Building gun) => gun?.Map?.GetComponent<ArtilleryMap>().ForGunPlacement(gun);
    }
}
