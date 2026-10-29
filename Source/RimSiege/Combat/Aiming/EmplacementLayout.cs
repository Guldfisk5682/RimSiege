using System;
using System.Collections.Generic;
using RimSiege.Integration;
using Verse;

namespace RimSiege.Combat.Aiming
{
    internal static class EmplacementLayout
    {
        internal static bool CheckCell(Predicate<IntVec3> fineCheck, IntVec3 cell, in SightRay sight)
        {
            CeArtillerySight.Scope scope = CeArtillerySight.Current;
            if (scope == null || !scope.Obstacles.TryGetValue(cell, out List<Thing> things)) return true;
            foreach (Thing thing in things)
            {
                scope.Overlay = thing;
                try
                {
                    if (CeAccess.BoundsFor(thing).max.y >= sight.Minimum(cell) && !fineCheck(cell)) return false;
                }
                finally { scope.Overlay = null; }
            }
            return true;
        }
    }
}
