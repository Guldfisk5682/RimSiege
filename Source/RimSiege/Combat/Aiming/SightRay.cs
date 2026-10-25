using CombatExtended;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Aiming
{
    internal readonly struct SightRay
    {
        private const float CellHalfDiagonal = 0.71f; // Rounded up from sqrt(0.5).
        public readonly Map Map;
        public readonly Vector3 From;
        public readonly Vector3 To;
        public readonly bool HasFloors;
        public readonly bool CrossFloor;
        public readonly int SourceFloor;
        private readonly Vector3 delta;
        private readonly float horizontalLengthSquared;
        private readonly float halfCellParameter;

        public SightRay(Vector3 from, Vector3 to, Verb_LaunchProjectileCE verb, IntVec3 targetCell)
        {
            Map = verb.Caster.Map;
            From = from;
            To = to;
            HasFloors = IsoFloor.HasFloors(Map);
            SourceFloor = HasFloors ? IsoFloor.FloorOf(verb.Caster.Position, Map) : 0;
            CrossFloor = HasFloors && SourceFloor != IsoFloor.FloorOf(targetCell, Map);
            delta = to - from;
            horizontalLengthSquared = delta.x * delta.x + delta.z * delta.z;
            halfCellParameter = horizontalLengthSquared == 0f ? 0f : CellHalfDiagonal / Mathf.Sqrt(horizontalLengthSquared);
        }

        public void Heights(IntVec3 ground, out float minimum, out float maximum)
        {
            if (horizontalLengthSquared == 0f)
            {
                minimum = Mathf.Min(From.y, To.y);
                maximum = Mathf.Max(From.y, To.y);
                return;
            }
            float t = ((ground.x + 0.5f - From.x) * delta.x + (ground.z + 0.5f - From.z) * delta.z) / horizontalLengthSquared;
            float a = From.y + delta.y * Mathf.Clamp01(t - halfCellParameter);
            float b = From.y + delta.y * Mathf.Clamp01(t + halfCellParameter);
            minimum = Mathf.Min(a, b);
            maximum = Mathf.Max(a, b);
        }

        public float Minimum(IntVec3 cell)
        {
            if (!CrossFloor) return From.y; // Preserve CE's same-floor comparison.
            IntVec3 ground = IsoFloor.GroundCellOf(cell, Map);
            Heights(ground, out float minimum, out _);
            return minimum;
        }
    }
}
