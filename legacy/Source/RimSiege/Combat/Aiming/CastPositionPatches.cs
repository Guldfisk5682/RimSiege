using HarmonyLib;
using RimSiege.Integration;
using Verse;
using Verse.AI;

namespace RimSiege.Combat.Aiming
{
    [HarmonyPatch(typeof(CastPositionFinder), nameof(CastPositionFinder.TryFindCastPosition))]
    internal static class IsoCastPositionPatch
    {
        // ProjectZ chooses and ranks cells on the caster's floor. Its final
        // Verb.CanHitTargetFrom call dispatches to CE for CE weapons.
        private static bool Prefix(CastPositionRequest newReq, out IntVec3 dest, ref bool __result,
            out (bool ran, IntVec3 spot) __state)
        {
            dest = IntVec3.Invalid;
            __state = (false, IntVec3.Invalid);
            return !AcquisitionContext.Supported(newReq.verb) ||
                IsoCombatAi.CastPosition(newReq, out dest, ref __result, out __state);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(CastPositionRequest newReq, ref IntVec3 dest, ref bool __result,
            (bool ran, IntVec3 spot) __state)
        {
            if (AcquisitionContext.Supported(newReq.verb))
                IsoCombatAi.CastPositionResult(newReq, ref dest, ref __result, __state);
        }
    }
}
