using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimSiege.Integration;
using Verse;
using Verse.AI;

namespace RimSiege.Combat.Aiming
{
    [HarmonyPatch]
    internal static class PsychicLanceSightPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Verb), nameof(Verb.TryFindShootLineFromTo));
            yield return AccessTools.Method(typeof(Verb), "CanHitCellFromCellIgnoringRange");
        }

        public static bool RequiresSight(VerbProperties props, Verb verb) => props.requireLineOfSight ||
            verb is Verb_CastTargetEffectLances && IsoFloor.HasFloors(verb.Caster.Map);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo sight = AccessTools.Field(typeof(VerbProperties), nameof(VerbProperties.requireLineOfSight));
            foreach (CodeInstruction code in instructions)
            {
                if (code.opcode == OpCodes.Ldfld && Equals(code.operand, sight))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code).MoveBlocksFrom(code);
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(PsychicLanceSightPatch), nameof(RequiresSight));
                }
                yield return code;
            }
        }
    }

    [HarmonyPatch(typeof(Verb_CastTargetEffect), "TryCastShot")]
    internal static class PsychicLanceCastPatch
    {
        private static bool Prefix(Verb_CastTargetEffect __instance, ref bool __result)
        {
            if (!(__instance is Verb_CastTargetEffectLances) || !IsoFloor.HasFloors(__instance.Caster.Map)) return true;
            if (__instance.CanHitTarget(__instance.CurrentTarget)) return true;
            // Recheck after warmup, before effects or the native charge use.
            __result = false;
            return false;
        }
    }

    // ProjectZ's native decision honours requireLineOfSight=false on lances.
    // Keep RimSiege's sight policy after that decision without introducing a projectile or interception.
    [HarmonyPatch(typeof(Verb), "CanHitCellFromCellIgnoringRange")]
    internal static class PsychicLanceFloorSightPatch
    {
        [HarmonyPriority(int.MinValue)]
        private static void Postfix(Verb __instance, IntVec3 __0, IntVec3 __1, ref bool __result)
        {
            if (__instance is not Verb_CastTargetEffectLances || !__result) return;
            Map map = __instance.Caster.Map;
            if (IsoFloor.HasFloors(map) && !IsoFloor.SameFloor(__0, __1, map))
                __result = ProjectZ.Api.CanSeeBetweenFloors(__0, __1, map);
        }
    }
}
