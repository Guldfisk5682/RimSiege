using Verse;

namespace RimSiege
{
    public sealed class SiegeSettings : ModSettings
    {
        public bool EnhancedAssault;

        public override void ExposeData() => Scribe_Values.Look(ref EnhancedAssault, "enhancedAssault", false);
    }
}
