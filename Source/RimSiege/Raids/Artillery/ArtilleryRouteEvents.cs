using HarmonyLib;
using ProjectZ;
using RimWorld;
using Verse;

namespace RimSiege.Raids.Artillery
{
    [StaticConstructorOnStartup]
    internal static class ArtilleryRouteEvents
    {
        static ArtilleryRouteEvents() => Api.FloorChanged += FloorChanged;

        private static void FloorChanged(Map map, IntVec3 cell, bool built)
        {
            foreach (ArtilleryRaid raid in map.GetComponent<ArtilleryMap>().ActiveRaids)
                SupportRoute.Invalidate(raid);
        }

        internal static void BuildingChanged(Building building, Map map)
        {
            if (map == null) return;
            bool connection = building is Building_SiegeLadder || building.def.IsDoor ||
                building.Faction == Faction.OfPlayer && building.def.passability == Traversability.Impassable;
            foreach (ArtilleryRaid raid in map.GetComponent<ArtilleryMap>().ActiveRaids)
            {
                if (connection) SupportRoute.Invalidate(raid);
                else
                    foreach (IntVec3 cell in building.OccupiedRect())
                        if (SupportRoute.Affects(raid, cell))
                        {
                            SupportRoute.Invalidate(raid);
                            break;
                        }
            }
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.SpawnSetup))]
    internal static class ArtilleryRouteBuildingSpawned
    {
        private static void Postfix(Building __instance, Map map) => ArtilleryRouteEvents.BuildingChanged(__instance, map);
    }

    [HarmonyPatch(typeof(Building), nameof(Building.DeSpawn))]
    internal static class ArtilleryRouteBuildingDespawned
    {
        private static void Prefix(Building __instance) => ArtilleryRouteEvents.BuildingChanged(__instance, __instance.Map);
    }
}
