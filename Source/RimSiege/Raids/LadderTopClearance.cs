using Verse;

namespace RimSiege.Raids
{
    // Gameplay opt-in inherited by XML children of a marked defensive building.
    public sealed class LadderTopClearance : DefModExtension
    {
        internal static bool Allows(Thing building) => building?.def.HasModExtension<LadderTopClearance>() == true;
    }
}
