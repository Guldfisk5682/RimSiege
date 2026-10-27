using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using System;
using CombatExtended;
using HarmonyLib;
using UnityEngine;
using Verse;
using CombatExtended.Lasers;

namespace RimSiege.Integration
{
    internal static class CeAccess
    {
        internal static readonly float SuppressionRadius =
            Convert.ToSingle(AccessTools.Field(typeof(ProjectileCE), "SuppressionRadius").GetRawConstantValue());

        // CE names the legacy Unity module in metadata. Resolve its vector members
        // against the runtime types without loading a second Unity assembly.
        private static readonly Func<ProjectileCE, Vector3> ExactPosition =
            AccessTools.MethodDelegate<Func<ProjectileCE, Vector3>>(
                AccessTools.PropertyGetter(typeof(ProjectileCE), "ExactPosition"));

        public static Vector3 ProjectilePosition(ProjectileCE projectile) => ExactPosition(projectile);

        internal static readonly AccessTools.FieldRef<ProjectileCE, Vector3> Last = AccessTools.FieldRefAccess<ProjectileCE, Vector3>("LastPos");
        internal static readonly Action<ProjectileCE, Vector3> SetExact = AccessTools.MethodDelegate<Action<ProjectileCE, Vector3>>(AccessTools.PropertySetter(typeof(ProjectileCE), "ExactPosition"));
        internal static readonly Func<ProjectileCE, IntVec3, bool> CheckCell = AccessTools.MethodDelegate<Func<ProjectileCE, IntVec3, bool>>(AccessTools.Method(typeof(ProjectileCE), "CheckCellForCollision"));
        internal static readonly Func<ProjectileCE, Ray> ProjectileRay = AccessTools.MethodDelegate<Func<ProjectileCE, Ray>>(
            AccessTools.PropertyGetter(typeof(ProjectileCE), "ShotLine"));
        internal static readonly Func<Thing, Bounds> BoundsFor = AccessTools.MethodDelegate<Func<Thing, Bounds>>(
            AccessTools.Method(typeof(CE_Utility), nameof(CE_Utility.GetBoundsFor), new[] { typeof(Thing) }));
        internal static readonly Action<LaserBeamCE, Vector3, Vector3> SpawnLaser =
            AccessTools.MethodDelegate<Action<LaserBeamCE, Vector3, Vector3>>(AccessTools.Method(typeof(LaserBeamCE), nameof(LaserBeamCE.SpawnBeam)));
        internal static readonly Action<LaserBeamCE, Thing, Vector3> LaserImpact =
            AccessTools.MethodDelegate<Action<LaserBeamCE, Thing, Vector3>>(AccessTools.Method(typeof(LaserBeamCE), nameof(LaserBeamCE.Impact), new[] { typeof(Thing), typeof(Vector3) }));
        internal static readonly Func<LaserBeamCE, Vector3> LaserOrigin = AccessTools.MethodDelegate<Func<LaserBeamCE, Vector3>>(
            AccessTools.PropertyGetter(typeof(LaserBeamCE), "Origin"));
        internal static readonly Action<ProjectileCE, Thing> Impact = AccessTools.MethodDelegate<Action<ProjectileCE, Thing>>(AccessTools.Method(typeof(ProjectileCE), "Impact"));
        internal static readonly AccessTools.FieldRef<ShiftVecReport, Vector2> ReportSource = AccessTools.FieldRefAccess<ShiftVecReport, Vector2>("shotSource");
        internal static readonly AccessTools.FieldRef<ProjectileCE, int> ShotLineTick = AccessTools.FieldRefAccess<ProjectileCE, int>("lastShotLine");

        internal static readonly Func<Vector3, Vector3, IEnumerable<IntVec3>> Points = AccessTools.MethodDelegate<Func<Vector3, Vector3, IEnumerable<IntVec3>>>(AccessTools.Method(typeof(GenSightCE), "PointsOnLineOfSight"));

        internal delegate bool CellSight(Verb_LaunchProjectileCE verb, Vector3 source, IntVec3 target, Thing targetThing);

        internal static readonly CellSight CanHitCell = AccessTools.MethodDelegate<CellSight>(
            AccessTools.Method(typeof(Verb_LaunchProjectileCE), "CanHitCellFromCellIgnoringRange"));

        internal static readonly AccessTools.StructFieldRef<CollisionVertical, FloatRange> VerticalRange =
            AccessTools.StructFieldRefAccess<CollisionVertical, FloatRange>("heightRange");

        internal static FieldInfo[] VerbClosureOwner(Type closure)
        {
            FieldInfo owner = AccessTools.Field(closure, "<>4__this");
            if (owner != null) return new[] { owner };
            FieldInfo locals = AccessTools.GetDeclaredFields(closure).First(f => f.Name.StartsWith("CS$<>8__locals"));
            return new[] { locals, AccessTools.Field(locals.FieldType, "<>4__this") };
        }

        internal delegate IEnumerator FragmentRoutine(Vector3 position, Map map, float height, Thing instigator,
            ThingDefCountClass fragment, float speed, float shadowChance, FloatRange elevation, FloatRange rotation,
            float minimumCollisionDistance, bool canTargetSelf);

        internal static readonly FragmentRoutine Fragments = AccessTools.MethodDelegate<FragmentRoutine>(
            AccessTools.Method(typeof(CompFragments), nameof(CompFragments.FragRoutine)));

        internal static IEnumerable<MethodBase> Methods(Type baseType, params string[] names) =>
            GenTypes.AllTypes.Where(t => !t.ContainsGenericParameters && baseType.IsAssignableFrom(t))
                .SelectMany(AccessTools.GetDeclaredMethods)
                .Where(m => !m.IsStatic && !m.IsAbstract && names.Contains(m.Name));
    }
}
