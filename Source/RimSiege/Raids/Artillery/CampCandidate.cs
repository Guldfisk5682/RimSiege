using System.Collections.Generic;
using System.Linq;
using RimSiege.Combat.Aiming;
using RimSiege.Integration;

namespace RimSiege.Raids.Artillery
{
    // One transient proposal. Neither its screening cache nor its search metadata
    // is retained by CampPlan or serialized into a saved raid.
    internal sealed class CampCandidate
    {
        internal readonly CampPlan Plan;
        internal readonly CampField Field;
        internal readonly List<ArtilleryPlacement> Templates;
        internal readonly float Bearing, Deviation, EntryDistance;
        internal readonly int Sector;
        internal readonly Dictionary<ArtilleryPlacement, FixedCorridorResult> Corridors = new();
        internal int DirectCount => Plan.Guns.Count(g => CeArtillerySight.IsDirectFire(g.Def, g.Ammo));

        internal CampCandidate(CampPlan plan, CampField field, List<ArtilleryPlacement> templates,
            float bearing, int sector, float deviation, float entryDistance)
        {
            Plan = plan; Field = field; Templates = templates;
            Bearing = bearing; Sector = sector; Deviation = deviation; EntryDistance = entryDistance;
        }

        internal void Remove(HashSet<ArtilleryPlacement> failed)
        {
            for (int i = Plan.Guns.Count - 1; i >= 0; i--)
                if (failed.Contains(Plan.Guns[i])) { Plan.Guns.RemoveAt(i); Templates.RemoveAt(i); }
        }
    }
}
