using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Aiming
{
    [HarmonyPatch]
    internal static class GrenadeAngleFramePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(Verb_ThrowGrenade), "FindAngle");

        public static IntVec3 TargetCell(ref LocalTargetInfo target, Verb_LaunchProjectileCE verb) =>
            IsoFloor.HasFloors(verb.Caster.Map) ? IsoFloor.GroundCellOf(target.Cell, verb.Caster.Map) : target.Cell;

        public static void InitializeVertical(ref CollisionVertical vertical, Thing thing, Verb_LaunchProjectileCE verb)
        {
            vertical = new CollisionVertical(thing);
            if (thing == null || !IsoFloor.HasFloors(verb.Caster.Map)) return;
            FloatRange range = vertical.HeightRange;
            float offset = CombatHeights.Base(thing.Position, verb.Caster.Map);
            // This is CE's local temporary value, not a global collision model.
            CeAccess.VerticalRange(ref vertical) = new FloatRange(range.min + offset, range.max + offset);
        }

        public static CollisionVertical CreateVertical(Thing thing, Verb_LaunchProjectileCE verb)
        {
            CollisionVertical vertical = default;
            InitializeVertical(ref vertical, thing, verb);
            return vertical;
        }

        public static float CoverTop(ref CollisionVertical vertical, Verb_LaunchProjectileCE verb) =>
            vertical.Max - (IsoFloor.HasFloors(verb.Caster.Map) ? CombatHeights.Base(verb.Caster.Position, verb.Caster.Map) : 0f);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo center = AccessTools.Method(typeof(GenThing), nameof(GenThing.TrueCenter), new[] { typeof(Thing) });
            MethodInfo draw = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.DrawPos));
            MethodInfo position = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Position));
            MethodInfo target = AccessTools.PropertyGetter(typeof(LocalTargetInfo), nameof(LocalTargetInfo.Cell));
            ConstructorInfo vertical = AccessTools.Constructor(typeof(CollisionVertical), new[] { typeof(Thing) });
            MethodInfo max = AccessTools.PropertyGetter(typeof(CollisionVertical), nameof(CollisionVertical.Max));
            MethodInfo cells = AccessTools.Method(typeof(GenSightCE), nameof(GenSightCE.AllPointsOnLineOfSight));
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(center))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(FloorCoordinates), nameof(FloorCoordinates.TrueCenter));
                }
                else if (c.Calls(draw))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(FloorCoordinates), nameof(FloorCoordinates.DrawPos));
                }
                else if (c.Calls(position))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(FloorCoordinates), nameof(FloorCoordinates.GroundPosition));
                }
                else if (c.Calls(target) || c.Calls(max) || Equals(c.operand, vertical))
                {
                    bool targetCell = c.Calls(target), cover = c.Calls(max), create = c.opcode == OpCodes.Newobj;
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(GrenadeAngleFramePatch), targetCell ? nameof(TargetCell) :
                        cover ? nameof(CoverTop) : create ? nameof(CreateVertical) : nameof(InitializeVertical));
                }
                else if (c.Calls(cells))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(CoverQueries), nameof(CoverQueries.ThrownCells));
                }
                yield return c;
            }
        }
    }

    [HarmonyPatch]
    internal static class GrenadeHeadingFramePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(Verb_ThrowGrenade), nameof(Verb_ThrowGrenade.TryCastShot));

        public static IntVec3 Delta(IntVec3 target, IntVec3 source, Verb_LaunchProjectileCE verb)
        {
            if (IsoFloor.HasFloors(verb.Caster.Map))
            {
                target = IsoFloor.GroundCellOf(target, verb.Caster.Map);
                source = IsoFloor.GroundCellOf(source, verb.Caster.Map);
            }
            return target - source;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo subtract = AccessTools.Method(typeof(IntVec3), "op_Subtraction", new[] { typeof(IntVec3), typeof(IntVec3) });
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(subtract))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(GrenadeHeadingFramePatch), nameof(Delta));
                }
                yield return c;
            }
        }
    }
}
