using System;
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

namespace RimSiege.Combat.Projectiles
{
    [HarmonyPatch]
    internal static class ProjectileTrailPatches
    {
        [ThreadStatic] private static bool smokeFromProjectile;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CompProjectileFleck), "Emit");
            yield return AccessTools.Method(typeof(ProjectileCE), nameof(ProjectileCE.Tick));
            yield return AccessTools.Method(typeof(ProjectileCE).Assembly.GetType("ProjectileImpactFX.TrailThrower"), "ThrowSmoke");
        }

        private static Vector3 Position(Thing thing) => IsoView.Ready && thing is ProjectileCE projectile
            ? CeAccess.ProjectilePosition(projectile) : thing.DrawPos;

        // CE's interpolation is unchanged, but both endpoints must be ballistic
        // positions. Its stored DrawPos cache stays native across saves/view toggles.
        private static Vector3 Last(CompProjectileFleck comp) => IsoView.Ready
            ? CeAccess.Last((ProjectileCE)comp.parent) : CeAccess.FleckLast(comp);

        private static void StoreLast(CompProjectileFleck comp, Vector3 point) =>
            CeAccess.FleckLast(comp) = IsoView.Ready ? comp.parent.DrawPos : point;

        private static FleckCreationData Fleck(Vector3 point, Map map, FleckDef def, float scale)
        {
            if (!IsoView.Ready) return FleckMaker.GetDataStatic(point, map, def, scale);
            float height = VisualCoordinates.Height(point.y, map);
            point.y = def.altitudeLayer.AltitudeFor(def.altitudeLayerIncOffset);
            FleckCreationData data = FleckMaker.GetDataStatic(point, map, def, scale);
            // FleckStatic.Draw overwrites y with its altitude layer. IsoRim keeps
            // height through that step and billboards the native particle quad.
            IsoView.RaiseFleck(data, height);
            return data;
        }

        // TrailThrower is also used for muzzle smoke. Only the call made by a
        // flying projectile supplies CE ballistic coordinates to this adapter.
        private static void Smoke(Vector3 point, float size, Map map, string defName)
        {
            bool previous = smokeFromProjectile;
            smokeFromProjectile = true;
            try { CeAccess.ThrowTrailSmoke(point, size, map, defName); }
            finally { smokeFromProjectile = previous; }
        }

        private static FleckCreationData SmokeFleck(Vector3 point, Map map, FleckDef def, float scale) =>
            smokeFromProjectile ? Fleck(point, map, def, scale) : FleckMaker.GetDataStatic(point, map, def, scale);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            bool smoke = __originalMethod.DeclaringType.FullName == "ProjectileImpactFX.TrailThrower";
            foreach (CodeInstruction instruction in instructions)
            {
                string replacement = null;
                if (instruction.operand is MethodInfo method)
                {
                    if (method.Name == "get_DrawPos" &&
                        (method.DeclaringType == typeof(Thing) || typeof(ProjectileCE).IsAssignableFrom(method.DeclaringType)))
                        replacement = nameof(Position);
                    else if (method.DeclaringType == typeof(FleckMaker) && method.Name == nameof(FleckMaker.GetDataStatic))
                        replacement = smoke ? nameof(SmokeFleck) : nameof(Fleck);
                    else if (method.DeclaringType.FullName == "ProjectileImpactFX.TrailThrower" && method.Name == "ThrowSmoke")
                        replacement = nameof(Smoke);
                }
                else if (instruction.operand is FieldInfo field && field.DeclaringType == typeof(CompProjectileFleck) && field.Name == "lastPos")
                {
                    if (instruction.opcode == OpCodes.Ldfld) replacement = nameof(Last);
                    else if (instruction.opcode == OpCodes.Stfld) replacement = nameof(StoreLast);
                }
                if (replacement != null)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(ProjectileTrailPatches), replacement);
                }
                yield return instruction;
            }
        }
    }
}
