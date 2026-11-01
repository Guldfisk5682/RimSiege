using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Combat.Aiming;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Projectiles
{
    [HarmonyPatch]
    internal static class FragmentParentFramePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(CompFragments), nameof(CompFragments.Throw));

        public static Vector3 LocalPosition(ProjectileCE projectile)
        {
            Vector3 point = ExplosionFireCompatibility.DetonationPosition(projectile);
            IntVec3 cell = CeProjectileOrigins.SurfaceOf(projectile)?.ExplosionCell ?? projectile.Position;
            if (IsoFloor.HasFloors(projectile.Map)) point.y -= CombatHeights.Base(cell, projectile.Map);
            return point;
        }

        public static IEnumerator FromParent(Vector3 position, Map map, float height, Thing instigator,
            ThingDefCountClass fragment, float speed, float shadowChance, FloatRange elevation, FloatRange rotation,
            float minimumCollisionDistance, bool canTargetSelf, CompFragments component)
        {
            if (IsoFloor.HasFloors(map) && component.parent is ProjectileCE projectile)
            {
                position = FloorCoordinates.Ground(position, map);
                IntVec3 cell = CeProjectileOrigins.SurfaceOf(projectile)?.ExplosionCell ?? projectile.Position;
                position = FloorCoordinates.OnBand(position, cell, map);
            }
            return CeAccess.Fragments(position, map, height, instigator, fragment, speed, shadowChance,
                elevation, rotation, minimumCollisionDistance, canTargetSelf);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo position = AccessTools.PropertyGetter(typeof(ProjectileCE), "ExactPosition");
            MethodInfo routine = AccessTools.Method(typeof(CompFragments), nameof(CompFragments.FragRoutine));
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(position))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(FragmentParentFramePatch), nameof(LocalPosition));
                }
                else if (c.Calls(routine))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(FragmentParentFramePatch), nameof(FromParent));
                }
                yield return c;
            }
        }
    }

    [HarmonyPatch(typeof(CompFragments), nameof(CompFragments.FragRoutine))]
    internal static class FragmentHeightPatch
    {
        private static void Prefix(ref Vector3 pos, Map map, ref float height)
        {
            if (!IsoFloor.HasFloors(map)) return;
            // Captured in CE's iterator before it yields: the map origin stays a
            // band cell, and every spawned fragment carries an absolute CE height.
            height += CombatHeights.Base(pos.ToIntVec3(), map);
            pos.y = 0f;
        }
    }

    [HarmonyPatch]
    internal static class CookOffHeightPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(AmmoThing), "TryLaunchCookOffProjectile");

        public static float Height(Thing ammo, float local) => local +
            (IsoFloor.HasFloors(ammo.MapHeld) ? CombatHeights.Base(ammo.PositionHeld, ammo.MapHeld) : 0f);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction c in instructions)
            {
                if (c.opcode == OpCodes.Ldc_R4 && c.operand is float value && value == 0.1f)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    yield return c;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CookOffHeightPatch), nameof(Height)));
                    continue;
                }
                yield return c;
            }
        }
    }
}
