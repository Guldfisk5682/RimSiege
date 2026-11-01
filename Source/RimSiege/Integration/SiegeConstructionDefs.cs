using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimSiege.Integration
{
    // Construction data stays on the building Def without a player designator.
    public sealed class SiegeConstructionExtension : DefModExtension { }

    [HarmonyPatch(typeof(ThingDefGenerator_Buildings), nameof(ThingDefGenerator_Buildings.ImpliedBlueprintAndFrameDefs))]
    internal static class SiegeConstructionDefs
    {
        private static readonly Func<ThingDef, bool, ThingDef, bool, ThingDef> Blueprint =
            AccessTools.MethodDelegate<Func<ThingDef, bool, ThingDef, bool, ThingDef>>(
                AccessTools.Method(typeof(ThingDefGenerator_Buildings), "NewBlueprintDef_Thing"));
        private static readonly Func<ThingDef, bool, ThingDef> Frame =
            AccessTools.MethodDelegate<Func<ThingDef, bool, ThingDef>>(
                AccessTools.Method(typeof(ThingDefGenerator_Buildings), "NewFrameDef_Thing"));

        private static void Postfix(bool hotReload, ref IEnumerable<ThingDef> __result) =>
            __result = ConstructionDefs(__result, hotReload);

        private static IEnumerable<ThingDef> ConstructionDefs(IEnumerable<ThingDef> native, bool hotReload)
        {
            foreach (ThingDef def in native) yield return def;
            // The native caller registers, resolves and hashes these implied
            // definitions with the rest of the database, before any save loads.
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs.ToList())
            {
                if (def.BuildableByPlayer || def.GetModExtension<SiegeConstructionExtension>() == null) continue;
                yield return Blueprint(def, false, null, hotReload);
                yield return Frame(def, hotReload);
            }
        }
    }
}
