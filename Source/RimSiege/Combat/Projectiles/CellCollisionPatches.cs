using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using CombatExtended.Compatibility;
using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Projectiles
{
    /// <summary>
    /// Keeps CE's near-origin exemption
    /// (and the candidate sort key) so Thing.DrawPos is read in ground coordinates.
    /// Does not change GetMinCollisionDistance or CE's threshold formula.
    /// </summary>
    [HarmonyPatch(typeof(ProjectileCE), "CheckCellForCollision")]
    internal static class CheckCellForCollisionPatch
    {
        // CE: MagnitudeHorizontal(thing.DrawPos - OriginIV3.ToVector3Shifted()) >= minCollisionDistance
        // LaunchFramePatch already stores OriginIV3 in ground x/z.
        // Same pattern as VerbPositionCallsPatch: swap get_DrawPos → FloorCoordinates.DrawPos.
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            return RewriteBlockerPath(RewriteDrawPos(instructions), generator);
        }

        private static IEnumerable<CodeInstruction> RewriteBlockerPath(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            MethodInfo center = AccessTools.Method(typeof(GenThing), nameof(GenThing.TrueCenter), new[] { typeof(Thing) });
            MethodInfo toCell = AccessTools.Method(typeof(IntVec3Utility), nameof(IntVec3Utility.ToIntVec3), new[] { typeof(Vector3) });
            MethodInfo callback = AccessTools.Method(typeof(BlockerRegistry), nameof(BlockerRegistry.CheckCellForCollisionCallback));
            MethodInfo flash = AccessTools.Method(typeof(DebugCellDrawer), nameof(DebugCellDrawer.FlashCell),
                new[] { typeof(IntVec3), typeof(float), typeof(string), typeof(int) });
            LocalBuilder endpoint = generator.DeclareLocal(typeof(Vector3));
            bool blockerPath = false;
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(center))
                {
                    // Capture the full CE-space endpoint before ToIntVec3 loses
                    // its fractional height. This local belongs to this call,
                    // including when a registered callback re-enters CE.
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(BlockerFrame), nameof(BlockerFrame.Center));
                    yield return c;
                    yield return new CodeInstruction(OpCodes.Dup);
                    yield return new CodeInstruction(OpCodes.Stloc, endpoint);
                    blockerPath = true;
                    continue;
                }
                if (blockerPath && c.Calls(toCell))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(BlockerFrame), nameof(BlockerFrame.GroundCell));
                }
                else if (blockerPath && c.Calls(callback))
                {
                    // The entry callback already receives the real query cell;
                    // only the later ground-cell walk needs floor mapping.
                    yield return new CodeInstruction(OpCodes.Ldloc, endpoint).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(BlockerFrame), nameof(BlockerFrame.CheckCell));
                }
                else if (blockerPath && c.Calls(flash))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    yield return new CodeInstruction(OpCodes.Ldloc, endpoint);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(BlockerFrame), nameof(BlockerFrame.FlashCell));
                }
                yield return c;
            }
        }

        internal static IEnumerable<CodeInstruction> RewriteDrawPos(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo drawPos = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.DrawPos));
            MethodInfo worldDraw = AccessTools.Method(typeof(FloorCoordinates), nameof(FloorCoordinates.DrawPos));
            foreach (CodeInstruction c in instructions)
            {
                if (c.opcode == OpCodes.Callvirt && c.operand is MethodInfo m && m == drawPos)
                {
                    yield return new CodeInstruction(OpCodes.Call, worldDraw).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    continue;
                }
                yield return c;
            }
        }
    }

    /// <summary>
    /// The SortBy key lambda is a separate compiler-generated method
    /// ProjectileCE.&lt;CheckCellForCollision&gt;b__118_0 (verified in CE DLL IL).
    /// It still did thing.DrawPos - LastPos in band space, so multi-candidate order
    /// on F1 could be wrong. Patch it the same way; does not change CE's sort key formula.
    /// </summary>
    [HarmonyPatch]
    internal static class CheckCellForCollisionSortKeyPatch
    {
        private static MethodBase TargetMethod()
        {
            // Exact name from CombatExtended.dll IL (ilspycmd -il).
            MethodInfo exact = AccessTools.Method(typeof(ProjectileCE), "<CheckCellForCollision>b__118_0");
            if (exact != null) return exact;
            // Fallback: any compiler-generated instance float method on ProjectileCE
            // whose name starts with <CheckCellForCollision>b__ and takes Thing.
            return AccessTools.GetDeclaredMethods(typeof(ProjectileCE))
                .FirstOrDefault(m =>
                    m.Name.StartsWith("<CheckCellForCollision>b__")
                    && m.ReturnType == typeof(float)
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(Thing));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return CheckCellForCollisionPatch.RewriteDrawPos(instructions);
        }
    }
}
