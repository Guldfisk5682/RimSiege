using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using Verse;

namespace RimSiege.Combat.Projectiles
{
    [HarmonyPatch(typeof(ProjectileCE), "Impact")]
    internal static class ImpactFloorMapPatch
    {
        private static void Prefix(ProjectileCE __instance, Thing hitThing)
        {
            Map map = __instance.Map;
            if (!IsoFloor.HasFloors(map)) return;
            IntVec3 pos = __instance.Position;
            if (hitThing != null && hitThing.Map == map && IsoFloor.FloorOf(pos, map) != IsoFloor.FloorOf(hitThing.Position, map))
                __instance.Position = hitThing.Position;
        }
    }
}
