using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using IsoRim;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Rendering
{
    // CE's DrawEquipmentAiming transpiler swaps the last Graphics.DrawMesh for its own helper,
    // which then calls Graphics.DrawMesh directly. Iso only redirects Verse/RimWorld IL, so
    // during FigureWeapons capture the CE gun still draws at Y≈0 (feet under the 3D figure).
    // Point CE's draw helpers at DrawHook so capture (and Iso's other draw transforms) see them.
    [HarmonyPatch]
    internal static class CeEquipmentDrawMeshCompat
    {
        private static readonly MethodInfo GraphicsDrawMesh4 = AccessTools.Method(
            typeof(Graphics), nameof(Graphics.DrawMesh),
            new[] { typeof(Mesh), typeof(Matrix4x4), typeof(Material), typeof(int) });

        private static readonly MethodInfo DrawHookDrawMesh4 = AccessTools.Method(
            typeof(DrawHook), nameof(DrawHook.DrawMesh),
            new[] { typeof(Mesh), typeof(Matrix4x4), typeof(Material), typeof(int) });

        private static IEnumerable<MethodBase> TargetMethods()
        {
            MethodInfo ceDraw = AccessTools.Method(
                AccessTools.TypeByName("CombatExtended.HarmonyCE.Harmony_PawnRenderer_DrawEquipmentAiming"),
                "DrawMesh");
            if (ceDraw != null)
            {
                yield return ceDraw;
            }

            MethodInfo platform = AccessTools.DeclaredMethod(
                typeof(WeaponPlatform),
                nameof(WeaponPlatform.DrawPlatform),
                new[] { typeof(Matrix4x4), typeof(bool), typeof(int) });
            if (platform != null)
            {
                yield return platform;
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int replaced = 0;
            foreach (CodeInstruction ci in instructions)
            {
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt)
                    && ReferenceEquals(ci.operand, GraphicsDrawMesh4))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = DrawHookDrawMesh4;
                    replaced++;
                }

                yield return ci;
            }

            if (replaced == 0)
            {
                Log.Warning(
                    "[3Dsiege] CE×Iso weapon draw: no Graphics.DrawMesh(Mesh,Matrix4x4,Material,int) in "
                    + __originalMethod.DeclaringType.Name + "." + __originalMethod.Name);
            }
        }
    }
}
