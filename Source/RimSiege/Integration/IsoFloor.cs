using Verse;

namespace RimSiege.Integration
{
    /// <summary>
    /// Floor identity and geometry remain owned by IsoRimZ.
    /// </summary>
    public static class IsoFloor
    {
        public static bool HasFloors(Map map) => ProjectZ.Api.HasFloors(map);

        public static int FloorOf(IntVec3 cell, Map map) => ProjectZ.Api.FloorOf(cell, map);

        public static IntVec3 GroundCellOf(IntVec3 cell, Map map) => ProjectZ.Api.GroundCellOf(cell, map);

        public static IntVec3 CellOnFloor(IntVec3 ground, int floor, Map map) => ProjectZ.Api.CellOnFloor(ground, floor, map);

        public static float StoreyHeight => ProjectZ.Api.StoreyHeight;

        public static float Distance3D(IntVec3 from, IntVec3 to, Map map) => ProjectZ.Api.Distance3D(from, to, map);

        public static float RangeDistance(IntVec3 shooter, IntVec3 target, Map map) => ProjectZ.Api.RangeDistance(shooter, target, map);

        public static bool CanSeeBetweenFloors(IntVec3 from, IntVec3 to, Map map) => ProjectZ.Api.CanSeeBetweenFloors(from, to, map);

        public static CellRect GroundRect(Map map) => ProjectZ.Api.GroundRect(map);

        public static int MinLevel(Map map) => ProjectZ.Levels.Min(map);

        public static int MaxLevel(Map map) => ProjectZ.Levels.Max(map);

        public static bool HasLevel(Map map, int level) => ProjectZ.Levels.Has(map, level);

        public static bool PlaneSolid(IntVec3 ground, int level, Map map) => ProjectZ.Column.PlaneSolid(ground, level, map);

        public static bool FloorAt(IntVec3 ground, int level, Map map) => ProjectZ.Column.FloorAt(ground, level, map);
    }
}
