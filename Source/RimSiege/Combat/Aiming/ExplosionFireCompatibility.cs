using System;
using System.Collections.Generic;
using System.Linq;
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
    internal static class ExplosionFireCompatibility
    {
        private const float CellMiddleCe = CollisionVertical.WallCollisionHeight * ProjectZ.Combat.Heights.Middle;
        // Invocation scopes, not per-tick caches. Components can detonate while
        // another projectile is impacting; their own origin must take precedence.
        [ThreadStatic] internal static ProjectileCE Source;
        [ThreadStatic] internal static ExplosionCE Picker;

        public static IntVec3 ProjectileCell(Vector3 point, ProjectileCE projectile) =>
            FloorCoordinates.ContactCell(point, projectile.Position, projectile.Map);

        public static IntVec3 ComponentCell(Vector3 point, ThingComp component) =>
            component.parent is ProjectileCE projectile ? ProjectileCell(point, projectile) : point.ToIntVec3();

        internal static float WorldHeight(ExplosionCE explosion) =>
            CombatHeights.Base(explosion.Position, explosion.Map) + explosion.height;

        internal static float DistanceSquared(IntVec3 a, IntVec3 b, ExplosionCE explosion)
        {
            Map map = explosion.Map;
            int ka = IsoFloor.FloorOf(a, map), kb = IsoFloor.FloorOf(b, map);
            if (ka == kb) return (a - b).LengthHorizontalSquared;
            IntVec3 ga = IsoFloor.GroundCellOf(a, map), gb = IsoFloor.GroundCellOf(b, map);
            float ya = a == explosion.Position ? WorldHeight(explosion) : ka * CombatHeights.StoreyCe + CellMiddleCe;
            float yb = b == explosion.Position ? WorldHeight(explosion) : kb * CombatHeights.StoreyCe + CellMiddleCe;
            float dy = ya - yb;
            return (ga - gb).LengthHorizontalSquared + dy * dy;
        }

        public static float Distance(IntVec3 a, IntVec3 b, Explosion explosion) =>
            explosion is ExplosionCE ce && IsoFloor.HasFloors(explosion.Map)
                ? Mathf.Sqrt(DistanceSquared(a, b, ce)) : a.DistanceTo(b);

        public static float Storey() => Picker != null ? CombatHeights.StoreyCe : ProjectZ.Combat.Range3D.Storey;

        public static float VerticalDistance(float floorDelta) => Picker != null
            ? floorDelta + CellMiddleCe - Picker.height : floorDelta;

        private static float SourceHeight(float nativeHeight) => Picker != null
            ? CombatHeights.ToIsoOcclusion(WorldHeight(Picker)) : nativeHeight;

        public static bool Clear(IntVec3 from, IntVec3 to, float sourceHeight, float targetHeight, Map map) =>
            ProjectZ.Combat.Heights.Clear(from, to, SourceHeight(sourceHeight), targetHeight, map);

        public static bool PlanesClear(IntVec3 from, IntVec3 to, float sourceHeight, float targetHeight, Map map) =>
            ProjectZ.Combat.Explosions3D.PlanesClear(from, to, SourceHeight(sourceHeight), targetHeight, map);

        internal static IEnumerable<CodeInstruction> MapCalls(IEnumerable<CodeInstruction> instructions, bool component)
        {
            MethodInfo toCell = AccessTools.Method(typeof(IntVec3Utility), nameof(IntVec3Utility.ToIntVec3), new[] { typeof(Vector3) });
            MethodInfo draw = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.DrawPos));
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(toCell))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(ExplosionFireCompatibility), component ? nameof(ComponentCell) : nameof(ProjectileCell));
                }
                else if (!component && c.Calls(draw))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(FloorCoordinates), nameof(FloorCoordinates.DrawPos));
                }
                yield return c;
            }
        }

        public static Vector3 FleckPosition(Thing thing)
        {
            if (thing is ProjectileCE projectile && IsoFloor.HasFloors(thing.Map) && IsoView.Ready)
                return VisualCoordinates.Projectile(CeAccess.ProjectilePosition(projectile), thing.def.Altitude, thing.Map);
            return thing.DrawPos;
        }
    }

    [HarmonyPatch]
    internal static class ProjectileExplosionFramePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(ProjectileCE), "Impact", "ImpactSomething");

        [HarmonyPriority(Priority.First)]
        private static void Prefix(ProjectileCE __instance, out ProjectileCE __state)
        {
            __state = ExplosionFireCompatibility.Source;
            ExplosionFireCompatibility.Source = __instance;
        }

        // Restore the caller's origin even when the original method throws.
        private static void Finalizer(ProjectileCE __state) => ExplosionFireCompatibility.Source = __state;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            ExplosionFireCompatibility.MapCalls(instructions, false);
    }

    [HarmonyPatch]
    internal static class ExplosiveTickCellsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(ProjectileCE), "Tick")
            .Where(m => m.DeclaringType != typeof(ProjectileCE));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            ExplosionFireCompatibility.MapCalls(instructions, false);
    }

    [HarmonyPatch]
    internal static class ComponentExplosionSourcePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            CeAccess.Methods(typeof(CompExplosiveCE), "Explode")
                .Concat(CeAccess.Methods(typeof(CompExplosive), "Detonate"))
                .Concat(CeAccess.Methods(typeof(AmmoThing), "TryDetonate"))
                .Concat(CeAccess.Methods(typeof(CompAbilityEffect_ExplosionCE), "Apply"));

        [HarmonyPriority(Priority.First)]
        private static void Prefix(object __instance, out ProjectileCE __state)
        {
            __state = ExplosionFireCompatibility.Source;
            ExplosionFireCompatibility.Source = (__instance as ThingComp)?.parent as ProjectileCE;
        }

        private static void Finalizer(ProjectileCE __state) => ExplosionFireCompatibility.Source = __state;
    }

    [HarmonyPatch]
    internal static class ComponentExplosionCellsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(CompExplosiveCE), "Explode");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            ExplosionFireCompatibility.MapCalls(instructions, true);
    }

    [HarmonyPatch(typeof(GenExplosionCE), nameof(GenExplosionCE.DoExplosion))]
    internal static class ExplosionOriginPatch
    {
        private static void Prefix(ref IntVec3 center, Map map, ref float height)
        {
            if (!IsoFloor.HasFloors(map) || !center.IsValid) return;
            center.y = 0;
            ProjectileCE source = ExplosionFireCompatibility.Source;
            if (source == null || source.Map != map) return;
            IntVec3 ground = IsoFloor.GroundCellOf(center, map);
            center = IsoFloor.CellOnFloor(ground, IsoFloor.FloorOf(source.Position, map), map);
            // ExplosionCE's own roof/merge checks require a local height. Its
            // absolute blast height is recoverable from this band plus height.
            // An omitted height is CE's surface explosion, including bunker
            // busters. Explicit projectile heights are in continuous CE space.
            if (height != 0f) height -= CombatHeights.Base(center, map);
        }
    }

    [HarmonyPatch]
    internal static class PenetratingExplosionFramePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(ProjectileCE_BunkerBuster), "Impact");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo position = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Position));
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(position))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(FloorCoordinates), nameof(FloorCoordinates.GroundPosition));
                }
                yield return c;
            }
        }
    }

    [HarmonyPatch]
    internal static class CeExplosionCellsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(ExplosionCE), "get_ExplosionCellsToHit");

        private static void Postfix(ExplosionCE __instance, ref IEnumerable<IntVec3> __result)
        {
            if (!IsoFloor.HasFloors(__instance.Map)) return;
            ExplosionCE previous = ExplosionFireCompatibility.Picker;
            ExplosionFireCompatibility.Picker = __instance;
            try
            {
                __result = IsoFloor.ExplosionCells(__result, __instance.Position, __instance.Map,
                    __instance.radius, __instance.affectedAngle);
            }
            finally
            {
                ExplosionFireCompatibility.Picker = previous;
            }
        }
    }

    [HarmonyPatch(typeof(ProjectZ.Combat.Explosions3D), nameof(ProjectZ.Combat.Explosions3D.CellsToHit))]
    internal static class IsoExplosionGeometryPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo storey = AccessTools.PropertyGetter(typeof(ProjectZ.Combat.Range3D), nameof(ProjectZ.Combat.Range3D.Storey));
            MethodInfo clear = AccessTools.Method(typeof(ProjectZ.Combat.Heights), nameof(ProjectZ.Combat.Heights.Clear));
            MethodInfo planes = AccessTools.Method(typeof(ProjectZ.Combat.Explosions3D), nameof(ProjectZ.Combat.Explosions3D.PlanesClear));
            bool vertical = false;
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(storey))
                {
                    c.operand = AccessTools.Method(typeof(ExplosionFireCompatibility), nameof(ExplosionFireCompatibility.Storey));
                    vertical = true;
                }
                else if (c.Calls(clear))
                    c.operand = AccessTools.Method(typeof(ExplosionFireCompatibility), nameof(ExplosionFireCompatibility.Clear));
                else if (c.Calls(planes))
                    c.operand = AccessTools.Method(typeof(ExplosionFireCompatibility), nameof(ExplosionFireCompatibility.PlanesClear));
                yield return c;
                if (vertical && c.opcode == OpCodes.Mul)
                {
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(ExplosionFireCompatibility), nameof(ExplosionFireCompatibility.VerticalDistance)));
                    vertical = false;
                }
            }
        }
    }

    [HarmonyPatch]
    internal static class CeExplosionFalloffPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(ExplosionCE), "GetDamageAmountAtCE", "GetArmorPenetrationAtCE");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo distance = AccessTools.Method(typeof(IntVec3Utility), nameof(IntVec3Utility.DistanceTo), new[] { typeof(IntVec3), typeof(IntVec3) });
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(distance))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(ExplosionFireCompatibility), nameof(ExplosionFireCompatibility.Distance));
                }
                yield return c;
            }
        }
    }

    [HarmonyPatch(typeof(ProjectZ.Combat.Explosions3D), nameof(ProjectZ.Combat.Explosions3D.Distance))]
    internal static class ExplosionPropagationDistancePatch
    {
        private static bool Prefix(IntVec3 a, IntVec3 b, Explosion e, ref float __result)
        {
            if (e is not ExplosionCE || !IsoFloor.HasFloors(e.Map)) return true;
            __result = ExplosionFireCompatibility.Distance(a, b, e);
            return false;
        }
    }

    [HarmonyPatch(typeof(ProjectZ.Combat.Explosions3D), nameof(ProjectZ.Combat.Explosions3D.DistanceToSquared))]
    internal static class ExplosionPropagationSquaredPatch
    {
        private static bool Prefix(IntVec3 a, IntVec3 b, Explosion e, ref int __result)
        {
            if (e is not ExplosionCE ce || !IsoFloor.HasFloors(e.Map)) return true;
            __result = Mathf.RoundToInt(ExplosionFireCompatibility.DistanceSquared(a, b, ce));
            return false;
        }
    }

    [HarmonyPatch(typeof(ProjectZ.Combat.Explosions3D), nameof(ProjectZ.Combat.Explosions3D.InHorDistOf))]
    internal static class ExplosionPropagationRangePatch
    {
        private static bool Prefix(ref IntVec3 a, IntVec3 b, float maxDist, Explosion e, ref bool __result)
        {
            if (e is not ExplosionCE ce || !IsoFloor.HasFloors(e.Map)) return true;
            __result = ExplosionFireCompatibility.DistanceSquared(a, b, ce) <= maxDist * maxDist;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class FireWindFloorPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(
            typeof(ProjectileCE).Assembly.GetType("CombatExtended.HarmonyCE.Harmony_Fire_TrySpread"), "GetRandWindShift");

        private static void Postfix(Fire fire, ref IntVec3 __result)
        {
            Map map = fire.Map;
            if (!IsoFloor.HasFloors(map)) return;
            int floor = IsoFloor.FloorOf(fire.Position, map);
            int first = IsoFloor.FirstRow(map, floor), last = IsoFloor.LastRow(map, floor);
            int z = fire.Position.z + __result.z;
            // Match ProjectZ's radial clipping after CE substitutes wind offsets.
            if (z < first) __result.z = first - 1 - fire.Position.z;
            else if (z > last) __result.z = last + 1 - fire.Position.z;
        }
    }

    [HarmonyPatch]
    internal static class ProjectileFlameFlecksPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(CompProjectileFleck), "CompTick", "Emit");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo draw = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.DrawPos));
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(draw))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(ExplosionFireCompatibility), nameof(ExplosionFireCompatibility.FleckPosition));
                }
                yield return c;
            }
        }
    }
}
