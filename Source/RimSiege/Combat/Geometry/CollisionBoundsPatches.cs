using CombatExtended;
using HarmonyLib;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Geometry
{
    [HarmonyPatch(typeof(CE_Utility), nameof(CE_Utility.GetBoundsFor), new[] { typeof(Thing) })]
    internal static class ThingBoundsFramePatch
    {
        private static void Postfix(Thing thing, ref Bounds __result)
        {
            if (thing == null || !IsoFloor.HasFloors(thing.Map)) return;
            if (SiegeLadderGeometry.TryGet(thing, out SiegeLadderShape ladder))
            {
                __result = ladder.Bounds;
                return;
            }
            Vector3 center = FloorCoordinates.Ground(__result.center, thing.Position, thing.Map);
            center.y += CombatHeights.Base(thing.Position, thing.Map);
            __result.center = center;
        }
    }

    [HarmonyPatch(typeof(CE_Utility), nameof(CE_Utility.GetBoundsFor), new[] { typeof(IntVec3), typeof(RoofDef) })]
    internal static class RoofBoundsFramePatch
    {
        [System.ThreadStatic] internal static Map Map;
        private static void Postfix(IntVec3 cell, ref Bounds __result)
        {
            if (!IsoFloor.HasFloors(Map)) return;
            Vector3 center = FloorCoordinates.Ground(__result.center, cell, Map);
            center.y += CombatHeights.Base(cell, Map);
            __result.center = center;
        }
    }

    [HarmonyPatch(typeof(ProjectileCE), "TryCollideWithRoof")]
    internal static class RoofMapContextPatch
    {
        private static void Prefix(ProjectileCE __instance, out Map __state)
        {
            __state = RoofBoundsFramePatch.Map;
            RoofBoundsFramePatch.Map = __instance.Map;
        }
        private static void Postfix(Map __state) => RoofBoundsFramePatch.Map = __state;
    }
}
