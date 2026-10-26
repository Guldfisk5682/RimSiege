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
            if (!(__instance is Verb_LaunchProjectileCE ce))
                return true;

            Map map = __instance.Caster?.Map;
            if (map == null || !targ.IsValid || !IsoFloor.HasFloors(map))
                return true;

            IntVec3 targetCell = targ.Cell;
            SiegeLadderShape ladder = default;
            bool ladderTarget = !ce.Projectile.projectile.flyOverhead &&
                SiegeLadderGeometry.TryGet(targ.Thing, out ladder);
            if (ladderTarget) targetCell = ladder.RangeCell(root);
            int ks = IsoFloor.FloorOf(root, map);
            int kt = IsoFloor.FloorOf(targetCell, map);
            if (ks < 0 || kt < 0 || (ks == kt && !ladderTarget))
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

    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), nameof(Verb_LaunchProjectileCE.CanHitTargetFrom),
        new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(string) }, new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
    internal static class CeShotFailureReasonPatch
    {
        private static void Postfix(Verb_LaunchProjectileCE __instance, IntVec3 root, LocalTargetInfo targ,
            ref string report, bool __result)
        {
            Map map = __instance.Caster.Map;
            if (__result || report != "CE_BlockedMaxRange".Translate().ToString() || !IsoFloor.HasFloors(map) ||
                IsoFloor.FloorOf(root, map) == IsoFloor.FloorOf(targ.Cell, map)) return;
            IntVec3 targetCell = !__instance.Projectile.projectile.flyOverhead &&
                SiegeLadderGeometry.TryGet(targ.Thing, out SiegeLadderShape ladder) ? ladder.RangeCell(root) : targ.Cell;
            // The failed-line report still uses raw band distance in CE. Keep
            // its other failure reasons, and correct this misleading range label.
            if (IsoFloor.RangeDistance(root, targetCell, map) <= __instance.EffectiveRange &&
                IsoFloor.Distance3D(root, targetCell, map) >= __instance.verbProps.EffectiveMinRange(targ, __instance.Caster))
                report = "CE_NoLoS".Translate();
        }
    }
}
