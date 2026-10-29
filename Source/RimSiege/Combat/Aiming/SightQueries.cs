using System.Collections.Generic;
using System;
using CombatExtended;
using RimSiege.Combat.Geometry;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Aiming
{
    internal static class SightQueries
    {
        public static float CoverDistance(IntVec3 from, IntVec3 to, Verb_LaunchProjectileCE verb) =>
            FloorCoordinates.HorizontalDistance(from, to, CeArtillerySight.MapFor(verb.Caster));

        // CE compares cover size to a muzzle height measured from floor zero.
        // Translate the size's height into the same frame as the lifted muzzle;
        // the actual collision bounds and CE's exemption formula stay unchanged.
        public static Vector3 CoverSize(ref Bounds bounds, Verb_LaunchProjectileCE verb)
        {
            Vector3 size = bounds.size;
            Map map = CeArtillerySight.MapFor(verb.Caster);
            if (IsoFloor.HasFloors(map))
                size.y += CombatHeights.Base(verb.Caster.Position, map);
            return size;
        }

        public static Ray BuildRay(Vector3 origin, Vector3 direction, Verb_LaunchProjectileCE verb,
            IntVec3 targetLoc, Thing targetThing, out SightRay sight)
        {
            Vector3 endpoint = origin + direction;
            if (SiegeLadderGeometry.TryGet(targetThing, out SiegeLadderShape ladder))
            {
                endpoint = ladder.AimPoint(verb.Caster.Position);
                direction = endpoint - origin;
            }
            // Thing targets already went through CE's native target-height/lean
            // construction and our frame adapters. Cell targets still need lifting.
            Map map = CeArtillerySight.MapFor(verb.Caster);
            if (targetThing == null && IsoFloor.HasFloors(map))
            {
                endpoint = FloorCoordinates.Ground(endpoint, targetLoc, map);
                endpoint.y += CombatHeights.Base(targetLoc, map);
                direction = endpoint - origin;
            }
            sight = new SightRay(origin, endpoint, verb, targetLoc);
            return new Ray(origin, direction);
        }

        public static bool PlaneBlocked(in SightRay sight) => sight.HasFloors &&
            FloorPlanes.Find(sight.Map, sight.From, sight.To).Found;

        public static IntVec3 SourceCell(Vector3 source, in SightRay sight)
        {
            IntVec3 cell = source.ToIntVec3();
            if (!sight.HasFloors) return cell;
            cell.y = 0;
            return IsoFloor.CellOnFloor(cell, sight.SourceFloor, sight.Map);
        }

        public static bool CheckCell(Predicate<IntVec3> fineCheck, IntVec3 cell, in SightRay sight)
        {
            // HighestCoverAt already includes k*StoreyCe through the existing
            // ThingBoundsFramePatch. Adding that base here would double it.
            float coverTop = sight.Map.GetLightingTracker().HighestCoverAt(cell);
            if (coverTop >= sight.Minimum(cell) && !fineCheck(cell)) return false;
            return EmplacementLayout.CheckCell(fineCheck, cell, sight);
        }

        public static IEnumerable<IntVec3> Cells(Vector3 source, Vector3 targetCell, SightRay sight)
        {
            Vector3 end = sight.HasFloors ? FloorCoordinates.Ground(targetCell, sight.Map) : targetCell;
            foreach (IntVec3 walked in CeAccess.Points(source, end))
            {
                if (!sight.HasFloors)
                {
                    yield return walked;
                    continue;
                }
                IntVec3 ground = walked;
                ground.y = 0;
                if (!sight.CrossFloor)
                {
                    yield return IsoFloor.CellOnFloor(ground, sight.SourceFloor, sight.Map);
                    continue;
                }
                sight.Heights(ground, out float minimum, out float maximum);
                int low = Mathf.Clamp(Mathf.FloorToInt(minimum / CombatHeights.StoreyCe), IsoFloor.MinLevel(sight.Map), IsoFloor.MaxLevel(sight.Map));
                int high = Mathf.Clamp(Mathf.FloorToInt(maximum / CombatHeights.StoreyCe), IsoFloor.MinLevel(sight.Map), IsoFloor.MaxLevel(sight.Map));
                bool ascending = sight.To.y >= sight.From.y;
                int first = ascending ? low : high, last = ascending ? high : low;
                int step = ascending ? 1 : -1;
                for (int level = first; ; level += step)
                {
                    yield return IsoFloor.CellOnFloor(ground, level, sight.Map);
                    if (level == last) break;
                }
            }
        }

        public static IEnumerable<IntVec3> SightCells(Vector3 from, Vector3 to, Verb_LaunchProjectileCE verb, Thing targetThing)
        {
            Map map = CeArtillerySight.MapFor(verb.Caster);
            if (!IsoFloor.HasFloors(map))
                return CeAccess.Points(from, to);
            IntVec3 mapTarget = to.ToIntVec3();
            to = FloorCoordinates.Ground(to, map);
            if (SiegeLadderGeometry.TryGet(targetThing, out SiegeLadderShape ladder))
                to = ladder.AimPoint(verb.Caster.Position);
            else
                to.y = CombatHeights.Base(mapTarget, map) + new CollisionVertical(targetThing).Max;
            return SightQueries.Cells(from, mapTarget.ToVector3Shifted(), new SightRay(from, to, verb, mapTarget));
        }
    }
}
