using IsoRim;
using Verse;

namespace RimSiege.Integration
{
    internal static class IsoView
    {
        public static bool Ready => Api.ViewReady;

        public static float HeightSquash => Api.HeightSquash;

        public static void RaiseFleck(FleckCreationData data, float height) =>
            Emitters.Raise(data.spawnPosition, height, 0f, data.def);
    }
}
