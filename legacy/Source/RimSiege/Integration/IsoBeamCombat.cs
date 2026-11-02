using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ProjectZ.Combat;
using Verse;

namespace RimSiege.Integration
{
    // AimFloor normally registers nothing when CE is present. Bind only its
    // native beam rows, and reuse its own projection, sweep and scope handling.
    internal static class IsoBeamCombat
    {
        private static readonly Func<Assembly, AimFloor.Row, MethodBase> Resolve =
            AccessTools.MethodDelegate<Func<Assembly, AimFloor.Row, MethodBase>>(AccessTools.Method(typeof(AimFloor), "Resolve"));
        private static readonly Func<List<CodeInstruction>, MethodBase, string> Edit =
            AccessTools.MethodDelegate<Func<List<CodeInstruction>, MethodBase, string>>(AccessTools.Method(typeof(AimFloor), "Edit"));

        internal static void Patch(Harmony harmony)
        {
            if (AimFloor.Off) return;
            var rows = (Dictionary<MethodBase, AimFloor.Row>)AccessTools.Field(typeof(AimFloor), "rowOf").GetValue(null);
            foreach (AimFloor.Row row in AimFloor.Rows.Where(r => r.type.StartsWith("Verse.Verb_ShootBeam")))
            {
                row.found = Resolve(typeof(Pawn).Assembly, row);
                rows[row.found] = row;
                bool inner = row.target == "inner";
                harmony.Patch(row.found,
                    prefix: inner ? null : new HarmonyMethod(typeof(AimFloor), nameof(AimFloor.Enter)) { priority = Priority.First },
                    transpiler: new HarmonyMethod(typeof(IsoBeamCombat), nameof(Transpiler)),
                    finalizer: inner ? null : new HarmonyMethod(typeof(AimFloor), nameof(AimFloor.Leave)));
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var codes = instructions.ToList();
            Edit(codes, __originalMethod);
            return codes;
        }
    }
}
