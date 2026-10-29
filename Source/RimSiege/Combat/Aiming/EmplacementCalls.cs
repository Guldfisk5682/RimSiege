using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using RimSiege.Integration;
using Verse;

namespace RimSiege.Combat.Aiming
{
    // Bind only CE's sight-query calls; Thing.Map itself remains untouched.
    [HarmonyPatch]
    internal static class EmplacementMapCalls
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new[] { "CanHitTargetFrom", "TryFindCEShootLineFromTo", "CanHitFromCellIgnoringRange",
                "CanHitCellFromCellIgnoringRange", "GetHighestCoverAndSmokeForTarget" };
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Verb_LaunchProjectileCE)).Where(m => names.Contains(m.Name)))
                yield return method;
            yield return AccessTools.PropertyGetter(typeof(Verb_LaunchProjectileCE), "LightingTracker");
            yield return AccessTools.Method(typeof(VerbProperties), nameof(VerbProperties.AdjustedRange));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo map = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Map));
            MethodInfo mapHeld = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.MapHeld));
            foreach (CodeInstruction code in instructions)
            {
                if (code.Calls(map))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(CeArtillerySight), nameof(CeArtillerySight.MapFor));
                }
                else if (code.Calls(mapHeld))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(CeArtillerySight), nameof(CeArtillerySight.MapHeldFor));
                }
                yield return code;
            }
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), nameof(Verb_LaunchProjectileCE.ShooterPawn), MethodType.Getter)]
    internal static class EmplacementOperatorCalls
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(CE_Utility), nameof(CE_Utility.TryGetTurretOperator));
            foreach (CodeInstruction code in instructions)
            {
                if (code.Calls(original)) code.operand = AccessTools.Method(typeof(CeArtillerySight), nameof(CeArtillerySight.OperatorFor));
                yield return code;
            }
        }
    }
}
