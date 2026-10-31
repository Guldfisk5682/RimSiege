using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ProjectZ;
using UnityEngine;
using Verse;

namespace RimSiege.Integration
{
    internal static class IsoPlaneQueries
    {
        internal static bool Available => Api.Version >= 6;

        // Called only for a short neighbourhood of one already-computed plane
        // crossing. Iso owns which walked column receives an edge/corner contact.
        // Keep Api 6 types inside this non-inlined method for older installations.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static IntVec3 CrossingCell(Map map, Vector3 before, Vector3 after, int plane)
        {
            List<Api.LineStep> steps = SimplePool<List<Api.LineStep>>.Get();
            try
            {
                Api.WalkLine3D(before, after, map, steps, stopAtSolidPlane: false);
                foreach (Api.LineStep step in steps)
                    if (step.solidPlane == plane) return IsoFloor.GroundCellOf(step.cell, map);
                return IntVec3.Invalid;
            }
            finally
            {
                steps.Clear();
                SimplePool<List<Api.LineStep>>.Return(steps);
            }
        }
    }
}
