using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimSiege.Raids.Artillery
{
    // Constructoid builders for mechanoid artillery; drop-pod battery removed.
    internal static class MechBattery
    {
        internal const float ConstructoidCombatPower = 10f;
        internal const int ConstructoidMin = 1;
        internal const int ConstructoidMax = 4;

        internal static int RollConstructoids() => Rand.RangeInclusive(ConstructoidMin, ConstructoidMax);

        // Must run before PawnsArrivalModeWorker.Arrive — TryGenerateRaidInfo
        // arrives the roster before its Postfix, so a postfix inject never lands.
        internal static void InjectConstructoids(ArtilleryRaid raid, List<Pawn> pawns)
        {
            if (raid == null || pawns == null || raid.Kind != ArtilleryRaidKind.Mechanoid || raid.ConstructoidCount <= 0)
                return;
            int have = pawns.Count(IsConstructoid);
            if (have >= raid.ConstructoidCount) return;
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed("Mech_Constructoid");
            for (int i = have; i < raid.ConstructoidCount; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kind, raid.Faction, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false, mustBeCapableOfViolence: false,
                    allowFood: false, allowAddictions: false));
                if (pawn.needs?.energy != null)
                    pawn.needs.energy.CurLevel = pawn.needs.energy.MaxLevel;
                pawns.Add(pawn);
            }
        }

        internal static bool IsConstructoid(Pawn pawn) =>
            pawn?.def == ThingDefOf.Mech_Constructoid;
    }
}
