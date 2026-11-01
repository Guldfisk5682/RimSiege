using CombatExtended;
using HarmonyLib;
using RimSiege.Integration;
using Verse;

namespace RimSiege.Combat.Aiming
{
    [HarmonyPatch(typeof(Building_TurretGunCE), nameof(Building_TurretGunCE.GetInspectString))]
    internal static class IndirectOverheadNotice
    {
        private static void Postfix(Building_TurretGunCE __instance, ref string __result)
        {
            if (!__instance.Spawned || !IsoFloor.HasFloors(__instance.Map) ||
                !__instance.IsMortarOrProjectileFliesOverhead || __instance.Position.Roofed(__instance.Map)) return;
            foreach (IntVec3 cell in __instance.OccupiedRect())
                if (!ProjectZ.Api.OpenSkyOver(cell, __instance.Map))
                {
                    __result += "\n" + "RS_IndirectOverheadObstruction".Translate();
                    return;
                }
        }
    }
}
