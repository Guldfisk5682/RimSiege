using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Projectiles
{
    [HarmonyPatch(typeof(ProjectileCE), nameof(ProjectileCE.DrawAt))]
    internal static class ProjectileDrawPatch
    {
        private static void DrawCard(Mesh mesh, Vector3 point, Quaternion rotation, Material material, int layer, ProjectileCE projectile)
        {
            if (!IsoView.Ready)
            {
                Graphics.DrawMesh(mesh, point, rotation, material, layer);
                return;
            }
            // Convert at the final draw call, after CE and other draw prefixes
            // have supplied their positions. Never reuse CE's flattened DrawPos.
            point = VisualCoordinates.Projectile(CeAccess.ProjectilePosition(projectile), projectile.def.Altitude, projectile.Map);
            Vector3 direction = projectile.FlightTicks == 0 ? CeAccess.Velocity(projectile)
                : CeAccess.ProjectilePosition(projectile) - CeAccess.Last(projectile);
            if (IsoFloor.HasFloors(projectile.Map)) direction.y *= IsoFloor.StoreyHeight / CombatHeights.StoreyCe;
            float spin = IsoRim.SkyStrikes.ScreenSpin(direction);
            if (projectile.def.projectile.spinRate != 0f)
            {
                float period = GenTicks.TicksPerRealSecond / projectile.def.projectile.spinRate;
                spin += Find.TickManager.TicksGame % period / period * 360f;
            }
            Matrix4x4 card = IsoRim.SkyStrikes.Card(point, spin);
            Graphics.DrawMesh(mesh, card, material, layer);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool replaced = false;
            foreach (CodeInstruction instruction in instructions)
            {
                // Only the projectile card; CE's shadow and comp drawing stay native.
                if (!replaced && instruction.operand is MethodInfo method && method.Name == "DrawMesh" &&
                    method.GetParameters() is var args && args.Length == 5 &&
                    args[0].ParameterType.FullName == typeof(Mesh).FullName &&
                    args[1].ParameterType.FullName == typeof(Vector3).FullName &&
                    args[2].ParameterType.FullName == typeof(Quaternion).FullName)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(ProjectileDrawPatch), nameof(DrawCard));
                    replaced = true;
                }
                yield return instruction;
            }
        }
    }

}
