using System.Collections.Generic;
using Verse;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    // Shared completion and a transient target window; child raids own all camp/pawn/gun state.
    internal sealed class ArtilleryIncident : IExposable
    {
        internal int Key;
        internal List<Lord> NativeLords = new();
        internal List<string> CompletionTags = new();
        internal bool CompletionPending, CompletionSent;
        // Native lord creation is one atomic operation; loaded incidents have completed it.
        internal bool SetupComplete = true;
        private ArtilleryTargets.Catalog catalog;
        private int nextCatalogTick;

        internal ArtilleryTargets.Catalog Targets(Map map, int tick)
        {
            if (catalog == null || tick >= nextCatalogTick)
            {
                catalog = new(map);
                nextCatalogTick = tick + ArtilleryTargets.SearchInterval;
            }
            return catalog;
        }

        public ArtilleryIncident() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Key, "key");
            Scribe_Collections.Look(ref NativeLords, "nativeLords", LookMode.Reference);
            Scribe_Collections.Look(ref CompletionTags, "completionTags", LookMode.Value);
            Scribe_Values.Look(ref CompletionPending, "completionPending");
            Scribe_Values.Look(ref CompletionSent, "completionSent");
            NativeLords ??= new(); CompletionTags ??= new();
        }
    }
}
