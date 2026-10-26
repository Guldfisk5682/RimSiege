using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimSiege
{
    public class RimSiegeMod : Mod
    {
        internal static SiegeSettings Settings { get; private set; }

        public RimSiegeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SiegeSettings>();
            new Harmony("Guld.ThreeDSiege").PatchAll(typeof(RimSiegeMod).Assembly);
            Log.Message("[3Dsiege] Combat Extended / IsoRim patches loaded.");
        }

        public override string SettingsCategory() => "3Dsiege";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled("RS_EnhancedAssault".Translate(), ref Settings.EnhancedAssault,
                "RS_EnhancedAssaultDescription".Translate());
            listing.End();
        }
    }
}
