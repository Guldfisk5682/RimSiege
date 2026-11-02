using HarmonyLib;
using UnityEngine;
using Verse;
using RimSiege.Integration;
using System;
using CePackApi = IsoRimCEPackZ.Api;
using ProjectZ;

namespace RimSiege
{
    public class RimSiegeMod : Mod
    {
        internal static SiegeSettings Settings { get; private set; }
        private static IDisposable ladderGeometry, surfaceTerrain;

        public RimSiegeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SiegeSettings>();
            Harmony harmony = new Harmony("Guld.ThreeDSiege");
            harmony.PatchAll(typeof(RimSiegeMod).Assembly);
            ladderGeometry?.Dispose();
            surfaceTerrain?.Dispose();
            ladderGeometry = CePackApi.RegisterTargetGeometry("Guld.ThreeDSiege", typeof(Building_SiegeLadder), new CeLadderGeometry());
            surfaceTerrain = CePackApi.RegisterSurfaceExplosionRule("Guld.ThreeDSiege", new CeSurfaceTerrain());
            Log.Message("[RimSiege] Siege gameplay loaded; CE geometry is provided by IsoRim CE Pack.");
        }

        public override string SettingsCategory() => "RimSiege";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled("RS_EnhancedAssault".Translate(), ref Settings.EnhancedAssault,
                "RS_EnhancedAssaultDescription".Translate());
            if (Settings.EnhancedAssault)
            {
                listing.Label("RS_ArtilleryChance".Translate((Settings.ArtilleryChance / 10f).ToString("0.0")));
                Settings.ArtilleryChance = Mathf.RoundToInt(listing.Slider(Settings.ArtilleryChance, 0f, 10f));
            }
            listing.End();
        }
    }

    [StaticConstructorOnStartup]
    internal static class CePackIntegrationState
    {
        static CePackIntegrationState() => LongEventHandler.ExecuteWhenFinished(() =>
            Log.Message("[RimSiege] CE Pack API " + CePackApi.Version + ": " + CePackApi.Capabilities));
    }
}
