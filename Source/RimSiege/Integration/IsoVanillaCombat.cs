using ProjectZ.Combat;
using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSiege.Integration
{
    // Invoke ProjectZ's existing native-projectile algorithms selectively while
    // its global CE gate remains off. ProjectileCE has a separate inheritance tree.
    internal static class IsoVanillaCombat
    {
        internal static bool Sight(IntVec3 from, IntVec3 to, Map map, bool corners, Func<IntVec3, bool> validator) =>
            Los3D.CanSee(from, to, map, corners, validator);

        internal static void Launch(Projectile projectile, ref Vector3 origin, ref Vector3 destination,
            ref int ticksToImpact, ref int lifetime) => Projectile_Launch_Patch.Postfix(
                projectile, ref origin, ref destination, ref ticksToImpact, ref lifetime);

        internal static bool Intercept(Projectile projectile, IntVec3 cell, ref bool result, Vector3 origin,
            Vector3 destination, Thing launcher, bool friendlyFire, out byte state) =>
            Projectile_Intercept_Patch.Prefix(projectile, cell, ref result, origin, destination, launcher, friendlyFire, out state);

        internal static void InterceptResult(Projectile projectile, IntVec3 cell, ref bool result, Vector3 origin,
            Vector3 destination, Thing launcher, bool friendlyFire, byte state) =>
            Projectile_Intercept_Patch.Postfix(projectile, cell, ref result, origin, destination, launcher, friendlyFire, state);

        internal static bool HitCell(Verb verb, IntVec3 from, IntVec3 to, bool corners, ref bool result, out byte state) =>
            Verb_CanHitCell_Patch.Prefix(verb, from, to, corners, ref result, out state);

        internal static void HitCellResult(Verb verb, IntVec3 from, IntVec3 to, bool corners, ref bool result, byte state) =>
            Verb_CanHitCell_Patch.Postfix(verb, from, to, corners, ref result, state);

        internal static bool OutOfRange(Verb verb, IntVec3 root, LocalTargetInfo target, CellRect rect, ref bool result, out byte state) =>
            Verb_OutOfRange_Patch.Prefix(verb, root, target, rect, ref result, out state);

        internal static void RangeResult(Verb verb, IntVec3 root, LocalTargetInfo target, CellRect rect, ref bool result, byte state) =>
            Verb_OutOfRange_Patch.Postfix(verb, root, target, rect, ref result, state);

        internal static void Report(Thing caster, Verb verb, LocalTargetInfo target, ref ShotReport report) =>
            ShotReport_Patch.Postfix(caster, verb, target, ref report);

        internal static void BeginCast(Verb_LaunchProjectile verb, out Verb previous) => LobbedCast_Patch.Prefix(verb, out previous);

        internal static void EndCast(Verb previous) => LobbedCast_Patch.Finalizer(previous);

        internal static void BeginMiss(ref ShootLine line, Map map, out IntVec3 source) =>
            ShootLine_MissWild_Patch.Prefix(ref line, map, out source);

        internal static void EndMiss(ref ShootLine line, IntVec3 source) => ShootLine_MissWild_Patch.Postfix(ref line, source);

        internal static bool CastPosition(CastPositionRequest request, out IntVec3 destination, ref bool result,
            out (bool ran, IntVec3 spot) state) => CastPositionFinder_Patch.Prefix(request, out destination, ref result, out state);

        internal static void CastPositionResult(CastPositionRequest request, ref IntVec3 destination, ref bool result,
            (bool ran, IntVec3 spot) state) => CastPositionFinder_Patch.Postfix(request, ref destination, ref result, state);
    }
}
