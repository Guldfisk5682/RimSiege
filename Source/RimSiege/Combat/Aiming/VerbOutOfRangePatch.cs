using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using Verse;

namespace RimSiege.Combat.Aiming
{
    /// <summary>
    /// Mirror IsoRimZ Verb_OutOfRange_Patch while CombatGate is off under CE.
    /// Reuses ProjectZ.Api.RangeDistance / Distance3D; does not reimplement Range3D math.
    /// </summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.OutOfRange))]
    internal static class VerbOutOfRangePatch
    {
        private static bool Prefix(Verb __instance, IntVec3 root, LocalTargetInfo targ, CellRect occupiedRect, ref bool __result)
        {
            // Only CE ranged verbs use this floor-aware range adapter.
            if (!(__instance is Verb_LaunchProjectileCE))
                return true;

            Map map = __instance.Caster?.Map;
            if (map == null || !targ.IsValid || !IsoFloor.HasFloors(map))
                return true;

            IntVec3 targetCell = targ.Cell;
            int ks = IsoFloor.FloorOf(root, map);
            int kt = IsoFloor.FloorOf(targetCell, map);
            if (ks < 0 || kt < 0 || ks == kt)
                return true;

            float min = __instance.verbProps.EffectiveMinRange(targ, __instance.Caster);
            float range = __instance.EffectiveRange;
            float d3 = IsoFloor.Distance3D(root, targetCell, map);
            // ProjectZ compares RangeDistance to weapon range; Distance3D to min range.
            float rangeDist = IsoFloor.RangeDistance(root, targetCell, map);
            bool outOfRange = rangeDist > range || d3 < min;
            __result = outOfRange;

            return false;
        }
    }
}
