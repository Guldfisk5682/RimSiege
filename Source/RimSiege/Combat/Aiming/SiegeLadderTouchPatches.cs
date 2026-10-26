using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimSiege.Integration;
using Verse;
using Verse.AI;

namespace RimSiege.Combat.Aiming
{
    [HarmonyPatch(typeof(ReachabilityImmediate), nameof(ReachabilityImmediate.CanReachImmediate),
        new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(Map), typeof(PathEndMode), typeof(Pawn) })]
    internal static class SiegeLadderHeadTouchPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(IntVec3 __0, ref LocalTargetInfo __1, Map __2, PathEndMode __3) =>
            IsoSiegeLadders.HeadTarget(__0, ref __1, __2, __3);
    }

    [HarmonyPatch(typeof(Reachability), nameof(Reachability.CanReach),
        new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) })]
    internal static class SiegeLadderHeadReachPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(IntVec3 start, ref LocalTargetInfo dest, PathEndMode peMode, Map ___map) =>
            IsoSiegeLadders.HeadTarget(start, ref dest, ___map, peMode);
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    internal static class SiegeLadderHeadDestinationPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ref LocalTargetInfo __0, PathEndMode __1, Pawn ___pawn) =>
            IsoSiegeLadders.HeadTarget(___pawn.Position, ref __0, ___pawn.Map, __1);
    }

    [HarmonyPatch]
    internal static class SiegeLadderHeadPathPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(PathFinder), nameof(PathFinder.FindPathNow));

        [HarmonyPriority(Priority.First)]
        private static void Prefix(IntVec3 start, ref LocalTargetInfo target, PathEndMode peMode, Map ___map) =>
            IsoSiegeLadders.HeadTarget(start, ref target, ___map, peMode);
    }

    [HarmonyPatch]
    internal static class SiegeLadderMeleeDestinationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(Toils_Combat)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(AccessTools.GetDeclaredMethods)
            .Where(m => m.Name.StartsWith("<FollowAndMeleeAttack>") && m.ReturnType == typeof(void) &&
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(int));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo different = AccessTools.Method(typeof(LocalTargetInfo), "op_Inequality");
            foreach (CodeInstruction code in instructions)
            {
                if (code.Calls(different))
                    code.operand = AccessTools.Method(typeof(IsoSiegeLadders), nameof(IsoSiegeLadders.DifferentDestination));
                yield return code;
            }
        }
    }
}
