using System;
using System.Runtime.CompilerServices;
using IsoRimCEPackZ;
using ProjectZ;
using Verse;
using CePackApi = IsoRimCEPackZ.Api;

namespace RimSiege.Integration
{
    // Register once after startup constructors have prepared CEPack's lazy parts.
    // Tokens belong to this session, not the Mod constructor or saved raid state.
    [StaticConstructorOnStartup]
    internal static class CePackIntegration
    {
        private const string Owner = "Guld.ThreeDSiege";
        private static IDisposable ladderGeometry, surfaceTerrain;

        static CePackIntegration() => LongEventHandler.ExecuteWhenFinished(Register);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Register()
        {
            if (CePackApi.Version < 1)
            {
                Log.Warning("[RimSiege] CE Pack extension API 1 is required; combat extensions are unavailable.");
                return;
            }
            ExtensionCapabilities capabilities = CePackApi.Capabilities;
            if ((capabilities & ExtensionCapabilities.TargetGeometry) != 0)
                ladderGeometry = CePackApi.RegisterTargetGeometry(Owner, typeof(Building_SiegeLadder), new CeLadderGeometry());
            if ((capabilities & ExtensionCapabilities.SurfaceExplosions) != 0)
                surfaceTerrain = CePackApi.RegisterSurfaceExplosionRule(Owner, new CeSurfaceTerrain());
            // First-use binding can disable an individual part; report the post-registration state.
            Log.Message("[RimSiege] CE Pack API " + CePackApi.Version + ": " + CePackApi.Capabilities);
        }

        internal static GeometryStatus OpenPlacement(PlacementSpec spec, out IPlacementQuery query)
        {
            query = null;
            if (CePackApi.Version < 1 || (CePackApi.Capabilities & ExtensionCapabilities.PlacementQueries) == 0)
                return GeometryStatus.Unavailable;
            return CePackApi.OpenPlacement(spec, out query);
        }
    }
}
