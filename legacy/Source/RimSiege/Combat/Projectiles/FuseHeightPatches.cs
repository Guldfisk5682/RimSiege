using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using Verse;

namespace RimSiege.Combat.Projectiles
{
    internal static class FuseFloor
    {
        internal sealed class Reference
        {
            internal int Floor;
        }

        private static readonly ConditionalWeakTable<ProjectileCE_HeightFuse, Reference> references = new();

        internal static Reference Get(ProjectileCE_HeightFuse projectile) => references.GetValue(projectile, p => new Reference
        {
            Floor = IsoFloor.HasFloors(p.Map)
                ? IsoFloor.FloorOf(p.intendedTarget.IsValid ? p.intendedTarget.Cell : p.Position, p.Map) : 0
        });
    }

    [HarmonyPatch]
    internal static class FuseLaunchReferencePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(ProjectileCE), "Launch");

        private static void Prefix(ProjectileCE __instance)
        {
            if (__instance is not ProjectileCE_HeightFuse projectile || !IsoFloor.HasFloors(projectile.Map)) return;
            FuseFloor.Get(projectile).Floor = IsoFloor.FloorOf(
                projectile.intendedTarget.IsValid ? projectile.intendedTarget.Cell : projectile.Position, projectile.Map);
        }
    }

    [HarmonyPatch(typeof(ProjectileCE_HeightFuse), "get_detonationHeight")]
    internal static class FuseHeightReferencePatch
    {
        private static void Postfix(ProjectileCE_HeightFuse __instance, ref float __result)
        {
            if (IsoFloor.HasFloors(__instance.Map))
                __result += FuseFloor.Get(__instance).Floor * CombatHeights.StoreyCe;
        }
    }

    [HarmonyPatch(typeof(ProjectileCE), nameof(ProjectileCE.ExposeData))]
    internal static class FuseReferenceSavePatch
    {
        private static void Postfix(ProjectileCE __instance)
        {
            if (__instance is not ProjectileCE_HeightFuse projectile) return;
            FuseFloor.Reference reference = FuseFloor.Get(projectile);
            Scribe_Values.Look(ref reference.Floor, "rimSiegeFuseTargetFloor", 0);
        }
    }
}
