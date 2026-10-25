using System.Collections.Generic;
using System;
using CombatExtended;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimSiege.Integration
{
    internal static class CeAccess
    {
        // CE names the legacy Unity module in metadata. Resolve its vector members
        // against the runtime types without loading a second Unity assembly.
        private static readonly Func<ProjectileCE, Vector3> ExactPosition =
            AccessTools.MethodDelegate<Func<ProjectileCE, Vector3>>(
                AccessTools.PropertyGetter(typeof(ProjectileCE), "ExactPosition"));

        public static Vector3 ProjectilePosition(ProjectileCE projectile) => ExactPosition(projectile);

        internal static readonly AccessTools.FieldRef<ProjectileCE, Vector3> Last = AccessTools.FieldRefAccess<ProjectileCE, Vector3>("LastPos");
        internal static readonly Action<ProjectileCE, Vector3> SetExact = AccessTools.MethodDelegate<Action<ProjectileCE, Vector3>>(AccessTools.PropertySetter(typeof(ProjectileCE), "ExactPosition"));
        internal static readonly Func<ProjectileCE, IntVec3, bool> CheckCell = AccessTools.MethodDelegate<Func<ProjectileCE, IntVec3, bool>>(AccessTools.Method(typeof(ProjectileCE), "CheckCellForCollision"));
        internal static readonly Action<ProjectileCE, Thing> Impact = AccessTools.MethodDelegate<Action<ProjectileCE, Thing>>(AccessTools.Method(typeof(ProjectileCE), "Impact"));
        internal static readonly AccessTools.FieldRef<ShiftVecReport, Vector2> ReportSource = AccessTools.FieldRefAccess<ShiftVecReport, Vector2>("shotSource");
        internal static readonly AccessTools.FieldRef<ProjectileCE, int> ShotLineTick = AccessTools.FieldRefAccess<ProjectileCE, int>("lastShotLine");

        internal static readonly Func<Vector3, Vector3, IEnumerable<IntVec3>> Points = AccessTools.MethodDelegate<Func<Vector3, Vector3, IEnumerable<IntVec3>>>(AccessTools.Method(typeof(GenSightCE), "PointsOnLineOfSight"));

        internal delegate bool CellSight(Verb_LaunchProjectileCE verb, Vector3 source, IntVec3 target, Thing targetThing);

        internal static readonly CellSight CanHitCell = AccessTools.MethodDelegate<CellSight>(
            AccessTools.Method(typeof(Verb_LaunchProjectileCE), "CanHitCellFromCellIgnoringRange"));
    }
}
