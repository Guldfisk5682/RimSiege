using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using RimSiege.Integration;
using Verse;

namespace RimSiege.Combat.Aiming
{
    internal static class LeanQueries
    {
        public static void Sources(IntVec3 from, IntVec3 to, Map map, List<IntVec3> cells) =>
            ShootLeanUtility.LeanShootingSourcesFromTo(from,
                IsoFloor.HasFloors(map) ? IsoFloor.OnFloorOf(to, from, map) : to, map, cells);

        public static void Destinations(List<IntVec3> cells, Thing target, IntVec3 from) =>
            ShootLeanUtility.CalcShootableCellsOf(cells, target,
                IsoFloor.HasFloors(target.Map) ? IsoFloor.OnFloorOf(from, target.Position, target.Map) : from);
    }

    [HarmonyPatch]
    internal static class CeLeanDirectionPatch
    {
        private static MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(Verb_LaunchProjectileCE))
            .Single(m => m.Name == nameof(Verb_LaunchProjectileCE.TryFindCEShootLineFromTo) && m.GetParameters().Length == 4);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo sources = AccessTools.Method(typeof(ShootLeanUtility), nameof(ShootLeanUtility.LeanShootingSourcesFromTo));
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(sources))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(LeanQueries), nameof(LeanQueries.Sources));
                }
                yield return c;
            }
        }
    }
}
