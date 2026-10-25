using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSiege.Combat.Aiming
{
    internal static class NativeCast
    {
        [ThreadStatic] internal static Verb_LaunchProjectile Current;
    }

    [HarmonyPatch]
    internal static class NativeCastScopePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(Verb_LaunchProjectile), "TryCastShot");

        [HarmonyPriority(Priority.First)]
        private static void Prefix(Verb_LaunchProjectile __instance, out (Verb_LaunchProjectile caster, Verb lobbed) __state)
        {
            __state.caster = NativeCast.Current;
            IsoVanillaCombat.BeginCast(__instance, out __state.lobbed);
            NativeCast.Current = __instance;
        }

        private static void Finalizer((Verb_LaunchProjectile caster, Verb lobbed) __state)
        {
            NativeCast.Current = __state.caster;
            IsoVanillaCombat.EndCast(__state.lobbed);
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch), new[]
    {
        typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo),
        typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef)
    })]
    internal static class NativeProjectileLaunchPatch
    {
        private static void Postfix(Projectile __instance, ref Vector3 ___origin, ref Vector3 ___destination,
            ref int ___ticksToImpact, ref int ___lifetime)
        {
            if (IsoFloor.HasFloors(__instance.Map))
                IsoVanillaCombat.Launch(__instance, ref ___origin, ref ___destination, ref ___ticksToImpact, ref ___lifetime);
        }
    }

    [HarmonyPatch(typeof(Projectile), "CheckForFreeIntercept")]
    internal static class NativeProjectileInterceptPatch
    {
        private static bool Prefix(Projectile __instance, IntVec3 c, ref bool __result, Vector3 ___origin,
            Vector3 ___destination, Thing ___launcher, bool ___preventFriendlyFire, out byte __state) =>
            IsoVanillaCombat.Intercept(__instance, c, ref __result, ___origin, ___destination,
                ___launcher, ___preventFriendlyFire, out __state);

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Projectile __instance, IntVec3 c, ref bool __result, Vector3 ___origin,
            Vector3 ___destination, Thing ___launcher, bool ___preventFriendlyFire, byte __state) =>
            IsoVanillaCombat.InterceptResult(__instance, c, ref __result, ___origin, ___destination,
                ___launcher, ___preventFriendlyFire, __state);
    }

    [HarmonyPatch(typeof(Verb), "CanHitCellFromCellIgnoringRange")]
    internal static class NativeHitCellPatch
    {
        private static bool Prefix(Verb __instance, IntVec3 __0, IntVec3 __1, bool __2, ref bool __result, out byte __state)
        {
            __state = default;
            return __instance is not Verb_LaunchProjectile ||
                IsoVanillaCombat.HitCell(__instance, __0, __1, __2, ref __result, out __state);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Verb __instance, IntVec3 __0, IntVec3 __1, bool __2, ref bool __result, byte __state)
        {
            if (__instance is Verb_LaunchProjectile)
                IsoVanillaCombat.HitCellResult(__instance, __0, __1, __2, ref __result, __state);
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.OutOfRange))]
    internal static class NativeRangePatch
    {
        private static bool Prefix(Verb __instance, IntVec3 root, LocalTargetInfo targ, CellRect occupiedRect,
            ref bool __result, out byte __state)
        {
            __state = default;
            return __instance is not Verb_LaunchProjectile ||
                IsoVanillaCombat.OutOfRange(__instance, root, targ, occupiedRect, ref __result, out __state);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Verb __instance, IntVec3 root, LocalTargetInfo targ, CellRect occupiedRect,
            ref bool __result, byte __state)
        {
            if (__instance is Verb_LaunchProjectile)
                IsoVanillaCombat.RangeResult(__instance, root, targ, occupiedRect, ref __result, __state);
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    internal static class NativeShotReportPatch
    {
        private static void Postfix(Thing caster, Verb verb, LocalTargetInfo target, ref ShotReport __result)
        {
            if (verb is Verb_LaunchProjectile) IsoVanillaCombat.Report(caster, verb, target, ref __result);
        }
    }

    [HarmonyPatch(typeof(ShootLine), nameof(ShootLine.ChangeDestToMissWild))]
    internal static class NativeMissFramePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ref ShootLine __instance, Map map, out IntVec3 __state)
        {
            __state = IntVec3.Invalid;
            if (NativeCast.Current != null) IsoVanillaCombat.BeginMiss(ref __instance, map, out __state);
        }

        private static void Postfix(ref ShootLine __instance, IntVec3 __state) => IsoVanillaCombat.EndMiss(ref __instance, __state);
    }

    [HarmonyPatch(typeof(CastPositionFinder), nameof(CastPositionFinder.TryFindCastPosition))]
    internal static class NativeCastPositionPatch
    {
        private static bool Prefix(CastPositionRequest newReq, out IntVec3 dest, ref bool __result,
            out (bool ran, IntVec3 spot) __state)
        {
            dest = IntVec3.Invalid;
            __state = (false, IntVec3.Invalid);
            return newReq.verb is not Verb_LaunchProjectile ||
                IsoVanillaCombat.CastPosition(newReq, out dest, ref __result, out __state);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(CastPositionRequest newReq, ref IntVec3 dest, ref bool __result,
            (bool ran, IntVec3 spot) __state)
        {
            if (newReq.verb is Verb_LaunchProjectile)
                IsoVanillaCombat.CastPositionResult(newReq, ref dest, ref __result, __state);
        }
    }

    [HarmonyPatch(typeof(CompTurretGun), nameof(CompTurretGun.CompTick))]
    internal static class AttachedTurretDirectionPatch
    {
        public static Vector3 Direction(Vector3 target, Vector3 source, CompTurretGun turret)
        {
            Map map = turret.parent.Map;
            if (IsoFloor.HasFloors(map))
            {
                target = FloorCoordinates.Ground(target, map);
                source = FloorCoordinates.Ground(source, turret.parent.Position, map);
            }
            return target - source;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo subtract = AccessTools.Method(typeof(Vector3), "op_Subtraction", new[] { typeof(Vector3), typeof(Vector3) });
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(subtract))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c).MoveBlocksFrom(c);
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(AttachedTurretDirectionPatch), nameof(Direction));
                }
                yield return c;
            }
        }
    }
}
