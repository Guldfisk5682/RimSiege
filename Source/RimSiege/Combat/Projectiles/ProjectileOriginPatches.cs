using System.Collections.Generic;
using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimSiege.Integration;

namespace RimSiege.Combat.Projectiles
{
    [HarmonyPatch]
    internal static class ProjectileOriginCapturePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(ProjectileCE), "Launch", "Throw", "RayCast");

        [HarmonyPriority(Priority.First)]
        private static void Prefix(ProjectileCE __instance) => CeProjectileOrigins.Capture(__instance);
    }

    [HarmonyPatch(typeof(ProjectileCE), nameof(ProjectileCE.ExposeData))]
    internal static class ProjectileOriginSavePatch
    {
        private static void Postfix(ProjectileCE __instance) => CeProjectileOrigins.Expose(__instance);
    }
}
