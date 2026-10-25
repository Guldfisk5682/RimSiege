using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using Verse;

namespace RimSiege.Combat.Aiming
{
    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), nameof(Verb_LaunchProjectileCE.ShotHeight), MethodType.Getter)]
    internal static class ShotHeightPatch
    {
        private static void Postfix(Verb_LaunchProjectileCE __instance, ref float __result)
        {
            Thing caster = __instance.Caster;
            Map map = caster?.Map;
            if (map == null || !IsoFloor.HasFloors(map))
                return;

            int floor = IsoFloor.FloorOf(caster.Position, map);
            if (floor <= 0)
                return;

            float offset = floor * CombatHeights.StoreyCe;
            __result += offset;
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), nameof(Verb_LaunchProjectileCE.GetTargetHeight))]
    internal static class GetTargetHeightPatch
    {
        private static void Postfix(Verb_LaunchProjectileCE __instance, LocalTargetInfo target, ref float __result)
        {
            Map map = __instance.Caster?.Map;
            if (map == null || !IsoFloor.HasFloors(map))
                return;

            IntVec3 cell = target.HasThing ? target.Thing.Position : target.Cell;
            int floor = IsoFloor.FloorOf(cell, map);
            if (floor <= 0)
                return;

            float offset = floor * CombatHeights.StoreyCe;
            __result += offset;
        }
    }
}
