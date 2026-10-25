using HarmonyLib;
using Verse;

namespace RimSiege
{
    public class RimSiegeMod : Mod
    {
        public RimSiegeMod(ModContentPack content) : base(content)
        {
            new Harmony("Guld.ThreeDSiege").PatchAll(typeof(RimSiegeMod).Assembly);
            Log.Message("[3Dsiege] Combat Extended / IsoRim patches loaded.");
        }
    }
}
