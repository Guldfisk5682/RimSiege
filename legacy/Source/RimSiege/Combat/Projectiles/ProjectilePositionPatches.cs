using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Projectiles
{
    [HarmonyPatch]
    internal static class LaunchFramePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(ProjectileCE), "Launch")
            .Where(m => (m.GetParameters().Length == 3 || m.GetParameters().Length == 8) &&
                m.GetParameters()[1].ParameterType.FullName == typeof(Vector2).FullName);
        private static void Prefix(ProjectileCE __instance, ref Vector2 origin)
        {
            if (!IsoFloor.HasFloors(__instance.Map)) return;
            Vector3 g = FloorCoordinates.Ground(new Vector3(origin.x, 0f, origin.y), __instance.Map);
            origin = new Vector2(g.x, g.z);
        }
    }

    [HarmonyPatch]
    internal static class ThrownOriginFramePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(ProjectileCE), "Throw");

        private static void Prefix(ProjectileCE __instance, ref Vector3 origin)
        {
            if (IsoFloor.HasFloors(__instance.Map))
                origin = FloorCoordinates.Ground(origin, __instance.Map);
        }
    }

    [HarmonyPatch]
    internal static class ExactMapPositionPatch
    {
        private static MethodBase TargetMethod() => AccessTools.PropertySetter(typeof(ProjectileCE), "ExactPosition");
        private static void Postfix(ProjectileCE __instance)
        {
            if (IsoFloor.HasFloors(__instance.Map)) ProjectileCoordinates.MapPosition(__instance, __instance.Position);
        }
    }

    [HarmonyPatch(typeof(ProjectileCE), "Tick")]
    internal static class TickFramePatch
    {
        private static bool Prefix(ProjectileCE __instance)
        {
            if (IsoFloor.HasFloors(__instance.Map) && !FloorCoordinates.InBounds(CeAccess.ProjectilePosition(__instance), __instance.Map))
            {
                __instance.Destroy();
                return false;
            }
            return true;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var c in instructions)
            {
                if (c.operand is MethodInfo m && m == AccessTools.PropertySetter(typeof(Thing), "Position"))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(ProjectileCoordinates), nameof(ProjectileCoordinates.MapPosition));
                }
                else if (c.operand is MethodInfo bounds && bounds.Name == "InBounds" && bounds.GetParameters().Length == 2 && (bounds.GetParameters()[0].ParameterType == typeof(Vector3) || bounds.GetParameters()[0].ParameterType.GetElementType() == typeof(Vector3)))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(FloorCoordinates), bounds.GetParameters()[0].ParameterType.IsByRef ? nameof(FloorCoordinates.InBoundsRef) : nameof(FloorCoordinates.InBounds));
                }
                yield return c;
            }
        }
    }

    [HarmonyPatch(typeof(ProjectileCE), "CheckForCollisionBetween")]
    internal static class CollisionWalkFramePatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var c in instructions)
            {
                if (c.operand is MethodInfo m && m == AccessTools.Method(typeof(ProjectileCE), "CheckCellForCollision"))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(ProjectileCoordinates), nameof(ProjectileCoordinates.CheckLevels));
                }
                yield return c;
            }
        }
    }
}
