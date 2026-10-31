using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using Verse;

namespace RimSiege.Combat.Projectiles
{
    [HarmonyPatch(typeof(ProjectileCE), "CheckForCollisionBetween")]
    internal static class PlaneContactPatch
    {
        private static void Prefix(ProjectileCE __instance, out PlaneContact __state)
        {
            __state = default;
            Map map = __instance.Map;
            if (!IsoFloor.HasFloors(map)) return;
            __state = FloorPlanes.Find(map, CeAccess.Last(__instance), CeAccess.ProjectilePosition(__instance), PlaneQuery.Contact);
            if (__state.Found)
            {
                // Check CE's own thing intersections up to the first solid plane.
                // The integration/velocity step is still entirely CE's.
                CeAccess.SetExact(__instance, __state.Point);
                CeAccess.ShotLineTick(__instance) = -1;
            }
        }

        private static void Postfix(ProjectileCE __instance, PlaneContact __state, ref bool __result)
        {
            if (!__state.Found || __result || __instance.Destroyed) return;
            __instance.Position = __state.Cell;
            __instance.landed = true;
            CeProjectileOrigins.RecordSurface(__instance, __state.Cell, __state.Point, __state.ExplosionCell, __state.FromBelow);

            CeAccess.Impact(__instance, null);
            __result = true;
        }
    }
}
