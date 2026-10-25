using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using UnityEngine;

namespace RimSiege.Combat.Projectiles
{
    [HarmonyPatch(typeof(ProjectileCE), nameof(ProjectileCE.DrawAt))]
    internal static class ProjectileDrawPatch
    {
        private static void Prefix(ProjectileCE __instance, ref Vector3 drawLoc)
        {
            if (!IsoView.Ready) return;
            drawLoc = VisualCoordinates.Projectile(CeAccess.ProjectilePosition(__instance), __instance.def.Altitude, __instance.Map);
        }
    }
}
