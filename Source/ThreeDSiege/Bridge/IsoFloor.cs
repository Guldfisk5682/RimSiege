using Verse;

namespace ThreeDSiege.Bridge
{
    /// <summary>
    /// Thin facade over IsoRimZ / ProjectZ.Api floor queries.
    /// Prefer calling ProjectZ.Api directly once assembly refs are wired; keep this for compile isolation.
    /// </summary>
    public static class IsoFloor
    {
        // Intentional stubs until assembly references to IsoRimProjectZ are configured.
        // Real implementation must call ProjectZ.Api.FloorOf / HasFloors / CellOnFloor / Distance3D — never reinvent band math.
        public static bool Ready => false;

        public static bool HasFloors(Map map) => false;

        public static int FloorOf(IntVec3 c, Map map) => 0;
    }
}
