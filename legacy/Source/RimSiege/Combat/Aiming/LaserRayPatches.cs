using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using CombatExtended.Lasers;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Combat.Projectiles;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Aiming
{
    [HarmonyPatch(typeof(ProjectileCE), nameof(ProjectileCE.RayCast))]
    internal static class LaserRayFramePatch
    {
        private static void Prefix(ProjectileCE __instance, VerbProperties verbProps, ref Vector2 origin, out LaserRayState __state)
        {
            __state = LaserRayFrame.Current;
            LaserRayFrame.Current = IsoFloor.HasFloors(__instance.Map) ? new LaserRayState(__instance, verbProps.range) : null;
            if (LaserRayFrame.Current != null)
            {
                Vector3 ground = FloorCoordinates.Ground(new Vector3(origin.x, 0f, origin.y), __instance.Map);
                origin = new Vector2(ground.x, ground.z);
                __instance.OriginIV3 = ground.ToIntVec3();
            }
        }

        private static void Finalizer(LaserRayState __state) => LaserRayFrame.Current = __state;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo range = AccessTools.Field(typeof(VerbProperties), nameof(VerbProperties.range));
            MethodInfo point = AccessTools.Method(typeof(Ray), nameof(Ray.GetPoint));
            MethodInfo things = AccessTools.Method(typeof(ThingGrid), nameof(ThingGrid.ThingsListAtFast), new[] { typeof(IntVec3) });
            MethodInfo intersection = AccessTools.Method(typeof(Bounds), nameof(Bounds.IntersectRay), new[] { typeof(Ray), typeof(float).MakeByRefType() });
            MethodInfo exact = AccessTools.PropertySetter(typeof(ProjectileCE), "ExactPosition");
            MethodInfo position = AccessTools.PropertySetter(typeof(Thing), nameof(Thing.Position));
            MethodInfo spawn = AccessTools.Method(typeof(LaserBeamCE), nameof(LaserBeamCE.SpawnBeam));
            MethodInfo destroy = AccessTools.Method(typeof(Thing), nameof(Thing.Destroy));
            foreach (CodeInstruction code in instructions)
            {
                string with = null;
                bool extraProjectile = false;
                if (code.opcode == OpCodes.Ldfld && Equals(code.operand, range)) { with = nameof(LaserRayFrame.Range); extraProjectile = true; }
                else if (code.Calls(point)) { with = nameof(LaserRayFrame.Point); extraProjectile = true; }
                else if (code.Calls(things)) { with = nameof(LaserRayFrame.Things); extraProjectile = true; }
                else if (code.Calls(intersection)) { with = nameof(LaserRayFrame.Intersects); extraProjectile = true; }
                else if (code.Calls(exact)) with = nameof(LaserRayFrame.SetPosition);
                else if (code.Calls(position))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(ProjectileCoordinates), nameof(ProjectileCoordinates.MapPosition));
                }
                else if (code.opcode == OpCodes.Stfld && code.operand is FieldInfo field && field.Name == "LastPos") with = nameof(LaserRayFrame.SetLast);
                else if (code.Calls(spawn)) with = nameof(LaserRayFrame.SpawnBeam);
                else if (code.Calls(destroy)) with = nameof(LaserRayFrame.Finish);
                else if (code.operand is MethodInfo method && method.Name == "InBounds")
                    with = method.GetParameters()[0].ParameterType.IsByRef ? nameof(LaserRayFrame.InBoundsRef) : nameof(LaserRayFrame.InBounds);
                if (with != null)
                {
                    if (extraProjectile) yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code).MoveBlocksFrom(code);
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(LaserRayFrame), with);
                }
                yield return code;
            }
        }
    }
}
