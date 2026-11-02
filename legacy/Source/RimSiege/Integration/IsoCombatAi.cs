using System;
using ProjectZ.Combat;
using Verse;
using Verse.AI;

namespace RimSiege.Integration
{
    // Keep ProjectZ's target policy and cast-position search while its native
    // projectile patches remain disabled under CE.
    internal static class IsoCombatAi
    {
        internal static void SelectTarget(IAttackTargetSearcher searcher, TargetScanFlags flags,
            Predicate<Thing> validator, float minDist, float maxDist, bool canBashDoors,
            bool canTakeTargetsCloserThanEffectiveMinRange, bool canBashFences, bool onlyRanged,
            ref IAttackTarget result) => AttackTargetFinder_Best_Patch.Postfix(
                searcher, flags, validator, minDist, maxDist, canBashDoors,
                canTakeTargetsCloserThanEffectiveMinRange, canBashFences, onlyRanged, ref result);

        internal static bool CastPosition(CastPositionRequest request, out IntVec3 destination, ref bool result,
            out (bool ran, IntVec3 spot) state) => CastPositionFinder_Patch.Prefix(request, out destination, ref result, out state);

        internal static void CastPositionResult(CastPositionRequest request, ref IntVec3 destination, ref bool result,
            (bool ran, IntVec3 spot) state) => CastPositionFinder_Patch.Postfix(request, ref destination, ref result, state);
    }
}
