using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimSiege.Integration
{
    internal static class VanillaSiegeCover
    {
        private static readonly FieldInfo Center = AccessTools.Field(typeof(SiegeBlueprintPlacer), "center");
        private static readonly FieldInfo FactionField = AccessTools.Field(typeof(SiegeBlueprintPlacer), "faction");
        private static readonly FieldInfo Locations = AccessTools.Field(typeof(SiegeBlueprintPlacer), "placedCoverLocs");
        private delegate IntVec3 CoverRoot(Map map, ThingDef def, ThingDef stuff);
        private static readonly CoverRoot FindRoot = AccessTools.MethodDelegate<CoverRoot>(AccessTools.Method(typeof(SiegeBlueprintPlacer), "FindCoverRoot"));

        internal static List<EmplacementSpec> Plan(Map map, Faction faction, IntVec3 center)
        {
            var previousCenter = Center.GetValue(null);
            var previousFaction = FactionField.GetValue(null);
            var locations = (List<IntVec3>)Locations.GetValue(null);
            var previousLocations = new List<IntVec3>(locations);
            var result = new List<EmplacementSpec>();
            Center.SetValue(null, center);
            FactionField.SetValue(null, faction);
            locations.Clear();
            try
            {
                bool floors = IsoFloor.HasFloors(map);
                int floor = floors ? IsoFloor.FloorOf(center, map) : 0;
                ThingDef def = Rand.Chance(.5f) ? ThingDefOf.Sandbags : ThingDefOf.Barricade;
                ThingDef stuff = def == ThingDefOf.Sandbags ? ThingDefOf.Cloth : (Rand.Chance(.5f) ? ThingDefOf.Steel : ThingDefOf.WoodLog);
                int count = Rand.RangeInclusive(2, 4);
                for (int i = 0; i < count; i++)
                {
                    IntVec3 root = FindRoot(map, def, stuff);
                    if (!root.IsValid) break;
                    if (floors && IsoFloor.FloorOf(root, map) != floor) continue;
                    Rot4 a = root.x <= center.x ? Rot4.East : Rot4.West;
                    Rot4 b = root.z <= center.z ? Rot4.North : Rot4.South;
                    AddLine(root, a, Rand.RangeInclusive(2, 7));
                    AddLine(root + b.FacingCell, b, Rand.RangeInclusive(2, 7));
                }
                void AddLine(IntVec3 root, Rot4 direction, int length)
                {
                    for (int i = 0; i < length; i++, root += direction.FacingCell)
                    {
                        if (!root.InBounds(map) || locations.Contains(root) ||
                            (floors && IsoFloor.FloorOf(root, map) != floor) ||
                            !GenConstruct.CanPlaceBlueprintAt_NewTemp(def, root, Rot4.North, map, false, null, null, stuff,
                                ignoreEdgeArea: true, ignoreInteractionSpots: true, ignoreClearableFreeBuildings: true).Accepted) break;
                        result.Add(new EmplacementSpec(def, stuff, null, root, Rot4.North, null));
                        locations.Add(root);
                    }
                }
                return result;
            }
            finally
            {
                locations.Clear();
                locations.AddRange(previousLocations);
                Center.SetValue(null, previousCenter);
                FactionField.SetValue(null, previousFaction);
            }
        }
    }
}
