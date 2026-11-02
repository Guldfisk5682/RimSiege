using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using Verse;

namespace RimSiege.Combat.Projectiles
{
    [HarmonyPatch(typeof(BulletCE), nameof(BulletCE.Impact))]
    internal static class ImpactBodyHeightPatch
    {
        public static BodyPartHeight BodyHeight(ref CollisionVertical vertical, float height, Thing target) =>
            vertical.GetCollisionBodyHeight(height - (IsoFloor.HasFloors(target.Map) ? CombatHeights.Base(target.Position, target.Map) : 0f));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var c in instructions)
            {
                if (c.operand is MethodInfo m && m.DeclaringType == typeof(CollisionVertical) && m.Name == "GetCollisionBodyHeight")
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(ImpactBodyHeightPatch), nameof(ImpactBodyHeightPatch.BodyHeight));
                }
                yield return c;
            }
        }
    }
}
