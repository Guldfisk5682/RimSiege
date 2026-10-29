using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    [HarmonyPatch(typeof(Lord), nameof(Lord.Notify_BuildingDamaged))]
    internal static class ArtilleryInterference
    {
        private static void Prefix(Lord __instance, Building building, DamageInfo dinfo)
        {
            if (__instance.CurLordToil is not LordToil_Siege || __instance.LordJob is not LordJob_Artillery job ||
                dinfo.Instigator?.Faction != Faction.OfPlayer || !dinfo.Def.ExternalViolenceFor(building)) return;
            ArtilleryRaid raid = job.Raid;
            if (ArtilleryCamp.Placements(raid.Camp).Any(p => p.Frame == building || p.Building == building))
                job.NotifyInterference();
        }
    }
}
