using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System;
using CombatExtended;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimSiege.Integration;

namespace RimSiege.Combat.Aiming
{
    [HarmonyPatch]
    internal static class SightCoverPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = typeof(Verb_LaunchProjectileCE);
            return type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                .SelectMany(t => AccessTools.GetDeclaredMethods(t))
                .Where(m => m.Name.StartsWith("<CanHitCellFromCellIgnoringRange>") &&
                    m.ReturnType == typeof(bool) && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof(Verse.IntVec3));
        }

        // Move the leading comparison to SightQueries.CheckCell, one comparison
        // per cell. This predicate retains CE's complete subsequent fine check.
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var codes = instructions.ToList();
            MethodInfo highest = AccessTools.Method(typeof(LightingTracker), nameof(LightingTracker.HighestCoverAt));
            int comparison = codes.FindIndex(c => c.Calls(highest));
            int branch = codes.FindIndex(comparison + 1, c => c.opcode.FlowControl == FlowControl.Cond_Branch);
            Label fineEntry = (Label)codes[branch].operand;
            int firstFine = codes.FindIndex(c => c.labels.Contains(fineEntry));
            for (int i = 0; i < firstFine; i++)
                codes[firstFine].MoveLabelsFrom(codes[i]).MoveBlocksFrom(codes[i]);
            MethodInfo distance = AccessTools.Method(typeof(IntVec3Utility), nameof(IntVec3Utility.DistanceTo), new[] { typeof(IntVec3), typeof(IntVec3) });
            MethodInfo size = AccessTools.PropertyGetter(typeof(Bounds), nameof(Bounds.size));
            FieldInfo[] owner = CeAccess.VerbClosureOwner(__originalMethod.DeclaringType);
            foreach (CodeInstruction c in codes.Skip(firstFine))
            {
                if (c.Calls(distance) || c.Calls(size))
                {
                    bool measuringDistance = c.Calls(distance);
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    foreach (FieldInfo field in owner) yield return new CodeInstruction(OpCodes.Ldfld, field);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(SightQueries), measuringDistance ? nameof(SightQueries.CoverDistance) : nameof(SightQueries.CoverSize));
                }
                yield return c;
            }
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), "CanHitCellFromCellIgnoringRange")]
    internal static class SightRayFramePatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            LocalBuilder sight = generator.DeclareLocal(typeof(SightRay));
            ConstructorInfo ray = AccessTools.Constructor(typeof(Ray), new[] { typeof(Vector3), typeof(Vector3) });
            MethodInfo points = AccessTools.Method(typeof(GenSightCE), nameof(GenSightCE.PointsOnLineOfSight), new[] { typeof(Vector3), typeof(Vector3) });
            MethodInfo invoke = AccessTools.Method(typeof(Predicate<Verse.IntVec3>), nameof(Predicate<Verse.IntVec3>.Invoke));
            MethodInfo cell = AccessTools.Method(typeof(Verse.IntVec3Utility), nameof(Verse.IntVec3Utility.ToIntVec3), new[] { typeof(Vector3) });
            bool constructed = false;
            foreach (CodeInstruction c in instructions)
            {
                if (c.opcode == OpCodes.Newobj && Equals(c.operand, ray))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Ldarg_3);
                    yield return new CodeInstruction(OpCodes.Ldloca, sight);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(SightQueries), nameof(SightQueries.BuildRay));
                    yield return c;
                    constructed = true;
                    continue;
                }
                if (constructed && c.opcode == OpCodes.Stfld && c.operand is FieldInfo field && field.Name == "shotLine")
                {
                    yield return c;
                    // Slabs/roofs may reject a line. A clear plane test always
                    // continues through CE's per-cell checks; no early pass.
                    Label resume = generator.DefineLabel();
                    yield return new CodeInstruction(OpCodes.Ldloca, sight);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SightQueries), nameof(SightQueries.PlaneBlocked)));
                    yield return new CodeInstruction(OpCodes.Brfalse, resume);
                    yield return new CodeInstruction(OpCodes.Ldc_I4_0);
                    yield return new CodeInstruction(OpCodes.Ret);
                    var next = new CodeInstruction(OpCodes.Nop);
                    next.labels.Add(resume);
                    yield return next;
                    continue;
                }
                if (c.Calls(points))
                {
                    yield return new CodeInstruction(OpCodes.Ldloc, sight).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(SightQueries), nameof(SightQueries.Cells));
                }
                else if (c.Calls(invoke))
                {
                    yield return new CodeInstruction(OpCodes.Ldloca, sight).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(SightQueries), nameof(SightQueries.CheckCell));
                }
                else if (constructed && c.Calls(cell))
                {
                    yield return new CodeInstruction(OpCodes.Ldloca, sight).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(SightQueries), nameof(SightQueries.SourceCell));
                }
                yield return c;
            }
        }
    }
}
