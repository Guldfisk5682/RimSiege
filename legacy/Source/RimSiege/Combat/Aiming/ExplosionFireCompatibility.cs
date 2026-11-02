using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
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

        private sealed class SurfaceTargets
        {
            public SurfaceTargets() { }
            internal HashSet<IntVec3> Cells = new HashSet<IntVec3>();
        }
        private static readonly ConditionalWeakTable<ExplosionCE, SurfaceTargets> Surfaces = new();
        private static readonly Action<DamageWorker, Explosion, IntVec3> DamageTerrain =
            AccessTools.MethodDelegate<Action<DamageWorker, Explosion, IntVec3>>(
                AccessTools.Method(typeof(DamageWorker), "ExplosionDamageTerrain"));

        public static Vector3 DetonationPosition(ProjectileCE projectile)
        {
            Vector3 point = CeAccess.ProjectilePosition(projectile);
            var surface = CeProjectileOrigins.SurfaceOf(projectile);
            if (surface?.SurfaceFromBelow == true)
                // CE classifies above-roof blasts at WallCollisionHeight. Use
                // its nearest below-roof height on the incoming side, rather
                // than placing an underside detonation on the floor above.
                point.y = CombatHeights.Base(surface.ExplosionCell, projectile.Map) +
                    MathF.BitDecrement(CollisionVertical.WallCollisionHeight);
            return point;
        }

        public static IntVec3 ProjectileCell(Vector3 point, ProjectileCE projectile)
        {
            var surface = CeProjectileOrigins.SurfaceOf(projectile);
            if (surface != null)
            {
                Vector3 effect = DetonationPosition(projectile);
                Vector3 shifted = effect;
                Vector3 direction = CeAccess.ProjectileRay(projectile).direction;
                // Match CE's existing west/south boundary-cell correction.
                if (direction.x < 0f && Mathf.Approximately(shifted.x, (int)shifted.x)) shifted.x -= 0.5f;
                if (direction.z < 0f && Mathf.Approximately(shifted.z, (int)shifted.z)) shifted.z -= 0.5f;
                if (point == effect || point == shifted || point == surface.SurfacePoint) return surface.ExplosionCell;
            }
            return CeProjectileOrigins.TrySurface(projectile, point, out IntVec3 cell)
                ? cell : FloorCoordinates.ContactCell(point, projectile.Position, projectile.Map);
        }

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

        internal static IEnumerable<IntVec3> SurfaceCells(ExplosionCE explosion, IEnumerable<IntVec3> cells)
        {
            var surface = CeProjectileOrigins.SurfaceOf(Source);
            if (surface?.SurfaceFromBelow == true && surface.ExplosionCell == explosion.Position &&
                surface.SurfaceCell != explosion.Position && ProjectZ.SlabDamage.SlabAt(surface.SurfaceCell, explosion.Map) != null)
                Surfaces.GetOrCreateValue(explosion).Cells.Add(surface.SurfaceCell);
            if (!Surfaces.TryGetValue(explosion, out SurfaceTargets targets) || targets.Cells.Count == 0) return cells;
            var result = cells.ToList();
            foreach (IntVec3 cell in targets.Cells)
                if (!result.Contains(cell)) result.Add(cell);
            return result;
        }

        internal static bool SurfaceTerrain(ExplosionCE explosion, IntVec3 cell)
        {
            if (!Surfaces.TryGetValue(explosion, out SurfaceTargets targets) || !targets.Cells.Remove(cell) ||
                ProjectZ.SlabDamage.SlabAt(cell, explosion.Map) == null) return false;
            // Affect the struck slab through its native terrain-damage hook,
            // without treating occupants above an intact slab as exposed.
            DamageTerrain(explosion.damType.Worker, explosion, cell);
            return true;
        }

        internal static void MergeSurfaces(ExplosionCE first, ExplosionCE second, ExplosionCE merged)
        {
            foreach (ExplosionCE source in new[] { first, second })
                if (Surfaces.TryGetValue(source, out SurfaceTargets targets))
                    Surfaces.GetOrCreateValue(merged).Cells.UnionWith(targets.Cells);
        }

        internal static void ExposeSurfaces(ExplosionCE explosion)
        {
            SurfaceTargets targets = Surfaces.GetOrCreateValue(explosion);
            Scribe_Collections.Look(ref targets.Cells, "rimSiegeStruckSlabs", LookMode.Value);
            targets.Cells ??= new HashSet<IntVec3>();
        }

        internal static IEnumerable<CodeInstruction> MapCalls(IEnumerable<CodeInstruction> instructions, bool component, bool detonation = false)
        {
            MethodInfo toCell = AccessTools.Method(typeof(IntVec3Utility), nameof(IntVec3Utility.ToIntVec3), new[] { typeof(Vector3) });
            MethodInfo draw = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.DrawPos));
            MethodInfo exact = AccessTools.PropertyGetter(typeof(ProjectileCE), "ExactPosition");
            foreach (CodeInstruction c in instructions)
            {
                if (detonation && c.Calls(exact))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(ExplosionFireCompatibility), nameof(DetonationPosition));
                }
                else if (c.Calls(toCell))
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

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) =>
            ExplosionFireCompatibility.MapCalls(instructions, false,
                __originalMethod.DeclaringType == typeof(ProjectileCE) && __originalMethod.Name == "Impact");
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
            var surface = CeProjectileOrigins.SurfaceOf(source);
            if (surface != null)
            {
                center = surface.ExplosionCell;
                if (surface.SurfaceFromBelow)
                {
                    height = MathF.BitDecrement(CollisionVertical.WallCollisionHeight);
                    return;
                }
            }
            IntVec3 ground = IsoFloor.GroundCellOf(center, map);
            if (surface == null) center = IsoFloor.CellOnFloor(ground, IsoFloor.FloorOf(source.Position, map), map);
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
                __result = ExplosionFireCompatibility.SurfaceCells(__instance, __result);
            }
            finally
            {
                ExplosionFireCompatibility.Picker = previous;
            }
        }
    }

    [HarmonyPatch(typeof(Explosion), "AffectCell")]
    internal static class SurfaceBlastTerrainPatch
    {
        private static bool Prefix(Explosion __instance, IntVec3 c) => __instance is not ExplosionCE ce ||
            !ExplosionFireCompatibility.SurfaceTerrain(ce, c);
    }

    [HarmonyPatch(typeof(ExplosionCE), nameof(ExplosionCE.MergeWith))]
    internal static class SurfaceBlastMergePatch
    {
        private static void Postfix(ExplosionCE __instance, ExplosionCE other, ExplosionCE merged, bool __result)
        {
            if (__result) ExplosionFireCompatibility.MergeSurfaces(__instance, other, merged);
        }
    }

    [HarmonyPatch(typeof(ExplosionCE), nameof(ExplosionCE.ExposeData))]
    internal static class SurfaceBlastSavePatch
    {
        private static void Postfix(ExplosionCE __instance) => ExplosionFireCompatibility.ExposeSurfaces(__instance);
    }

    [HarmonyPatch]
    internal static class IsoExplosionGeometryPatch
    {
        // ProjectZ's allocating entry point delegates to a buffer overload in
        // newer builds. Adapt the implementation wherever it lives.
        private static IEnumerable<MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(ProjectZ.Combat.Explosions3D))
                .Where(method => method.Name == nameof(ProjectZ.Combat.Explosions3D.CellsToHit));

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

}
