using System.Reflection;
using CombatExtended;
using HarmonyLib;
using Verse;

namespace RimSiege.Combat.Aiming
{
    // Opt-in policy on the building def; existing CE building instances keep
    // their ammo, cooldown and forced-target state without a class migration.
    public sealed class ManualTurretControl : DefModExtension
    {
        internal static bool Applies(Building_TurretGunCE turret) => turret.PlayerControlled &&
            turret.def.HasModExtension<ManualTurretControl>();
    }

    [HarmonyPatch]
    internal static class ManualTurretTargetCommandPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.PropertyGetter(typeof(Building_TurretGunCE), "CanSetForcedTarget");

        private static void Postfix(Building_TurretGunCE __instance, ref bool __result)
        {
            if (ManualTurretControl.Applies(__instance)) __result = true;
        }
    }

    [HarmonyPatch(typeof(Building_TurretGunCE), nameof(Building_TurretGunCE.TryFindNewTarget))]
    internal static class ManualTurretAcquisitionPatch
    {
        private static bool Prefix(Building_TurretGunCE __instance, ref LocalTargetInfo __result)
        {
            if (!ManualTurretControl.Applies(__instance)) return true;
            __result = LocalTargetInfo.Invalid;
            return false;
        }
    }

    [HarmonyPatch(typeof(Building_TurretGunCE), nameof(Building_TurretGunCE.BeginBurst))]
    internal static class ManualTurretBurstPatch
    {
        private static bool Prefix(Building_TurretGunCE __instance)
        {
            if (!ManualTurretControl.Applies(__instance) || __instance.ForcedTarget.IsValid ||
                __instance.globalTargetInfo.IsValid) return true;
            // A save may already contain an automatically acquired warmup.
            // Let CE clear it rather than firing an uncommanded first burst.
            __instance.ResetCurrentTarget();
            return false;
        }
    }
}
