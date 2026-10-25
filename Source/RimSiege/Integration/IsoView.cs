using IsoRim;

namespace RimSiege.Integration
{
    internal static class IsoView
    {
        public static bool Ready => Api.ViewReady;

        public static float HeightSquash => Api.HeightSquash;
    }
}
