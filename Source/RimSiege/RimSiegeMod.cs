using HarmonyLib;
using UnityEngine;
using Verse;
using RimSiege.Integration;

namespace RimSiege
{
    public class RimSiegeMod : Mod
    {
        internal static SiegeSettings Settings { get; private set; }

        public RimSiegeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SiegeSettings>();
            Harmony harmony = new Harmony("Guld.ThreeDSiege");
            harmony.PatchAll(typeof(RimSiegeMod).Assembly);
            IsoBeamCombat.Patch(harmony);
            Log.Message("[3Dsiege] Combat Extended / IsoRim patches loaded.");
        }

        public override string SettingsCategory() => "3Dsiege";

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
}
