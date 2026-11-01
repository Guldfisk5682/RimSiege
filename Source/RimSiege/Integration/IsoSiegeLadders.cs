using ProjectZ;
using ProjectZ.Patches;
using Verse;
using Verse.AI;

namespace RimSiege.Integration
{
    internal static class IsoSiegeLadders
    {
        // Both raider links share the combat interface; their native link kinds,
        // visuals and stats remain distinct. Player emergency ladders stay native.
        internal static bool Is(Thing thing) => thing is Building_SiegeLadder &&
            (thing.def == PZDefOf.PZ_SiegeLadder || thing.def == PZDefOf.PZ_SiegeLift);

        internal static bool Ends(Thing thing, out IntVec3 foot, out IntVec3 top)
        {
            foot = top = IntVec3.Invalid;
            if (!Is(thing) || thing.Map == null) return false;
            Building_SiegeLadder ladder = (Building_SiegeLadder)thing;
            foot = ladder.foot;
            top = ladder.top;
            return ladder.span > 0 && foot.IsValid && top.IsValid && IsoFloor.HasFloors(thing.Map);
        }

        internal static void HeadTarget(IntVec3 source, ref LocalTargetInfo target, Map map, PathEndMode mode)
        {
            Thing thing = target.Thing;
            if (mode != PathEndMode.Touch || thing?.Map != map || !Ends(thing, out _, out IntVec3 top) ||
                IsoFloor.FloorOf(source, map) != IsoFloor.FloorOf(top, map) || !top.Walkable(map) ||
                !StairTouch.TouchAllowedFrom(source, thing)) return;
            // This is an interaction destination, not another map object or link.
            target = new LocalTargetInfo(top);
        }

        internal static bool DifferentDestination(LocalTargetInfo target, LocalTargetInfo destination)
        {
            if (target == destination) return false;
            // The melee job retains the real ladder entity while its path ends
            // at the exposed head. Both addresses designate the same target.
            return destination.HasThing || !Ends(target.Thing, out _, out IntVec3 top) || destination.Cell != top;
        }
    }
}
