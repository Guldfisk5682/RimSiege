using System;
using System.Collections.Generic;
using System.Linq;
using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Raids.Artillery
{
    // A planning-only field shared by all pieces at one centre. Native placement
    // results are shared across centres; planned footprints remain layout-local.
    internal sealed class CampField
    {
        private const int WorkAreaSpacing = 3;
        private const float BatteryHalfWidth = 4.5f;
        private const float RowDepth = 2f;
        internal readonly Map Map;
        internal readonly IntVec3 Center;
        internal readonly List<ArtilleryPlacement> Cover;
        internal readonly int Capacity;
        internal readonly int Openness;
        // Arrival-time validation can use an autonomous battery's real builder.
        internal Pawn ConstructionWorker;
        private readonly HashSet<IntVec3> interior = new();
        private readonly HashSet<IntVec3> clearCells = new();
        private readonly List<IntVec3> open = new();
        private readonly Dictionary<(ThingDef, ThingDef), List<Slot>> slots = new();
        private readonly Dictionary<(ThingDef, ThingDef, Rot4, IntVec3), bool> placements;
        private readonly Vector2 forward, right;
        private readonly Rot4[] rotations;

        internal readonly record struct Slot(IntVec3 Cell, Rot4 Rotation);

        internal static (int Capacity, int Openness) Survey(Map map, IntVec3 center, Dictionary<IntVec3, bool> terrain)
        {
            var clear = new HashSet<IntVec3>();
            bool floors = IsoFloor.HasFloors(map);
            int floor = floors ? IsoFloor.FloorOf(center, map) : 0;
            CellRect area = CellRect.CenteredOn(center, CampPlanner.SiteSurveyRadius);
            CellRect gunArea = CellRect.CenteredOn(center, CampPlanner.GunAreaRadius);
            area.ClipInsideMap(map);
            int count = 0, openCount = 0;
            foreach (IntVec3 cell in area)
            {
                if (floors && IsoFloor.FloorOf(cell, map) != floor) continue;
                count++;
                if (!terrain.TryGetValue(cell, out bool open))
                    terrain[cell] = open = cell.Walkable(map) && !cell.Roofed(map);
                if (open)
                {
                    openCount++;
                    if (gunArea.Contains(cell)) clear.Add(cell);
                }
            }
            return (WorkAreas(clear), count == 0 ? 0 : openCount * 10 / count);
        }

        internal CampField(Map map, IntVec3 center, IntVec3 anchor, List<ArtilleryPlacement> cover,
            Dictionary<IntVec3, bool> terrain, Dictionary<(ThingDef, ThingDef, Rot4, IntVec3), bool> placements)
        {
            Map = map; Center = center; Cover = cover; this.placements = placements;
            IntVec3 goal = IsoFloor.HasFloors(map) ? IsoFloor.GroundCellOf(anchor, map) : anchor;
            IntVec3 ground = IsoFloor.HasFloors(map) ? IsoFloor.GroundCellOf(center, map) : center;
            forward = new Vector2(goal.x - ground.x, goal.z - ground.z).normalized;
            right = new Vector2(forward.y, -forward.x);
            Rot4 horizontal = forward.x >= 0 ? Rot4.East : Rot4.West;
            Rot4 vertical = forward.y >= 0 ? Rot4.North : Rot4.South;
            rotations = Math.Abs(forward.x) > Math.Abs(forward.y)
                ? new[] { horizontal, vertical } : new[] { vertical, horizontal };

            // Native cover consists of open L segments, not a closed wall.
            // Its convex envelope bounds the battery without treating it as a
            // collision barrier or inventing additional cover blueprints.
            var coverCells = new HashSet<IntVec3>(cover.SelectMany(p => GenAdj.OccupiedRect(p.Cell, p.Rotation, p.Def.Size)));
            List<IntVec2> hull = Hull(coverCells);
            bool floors = IsoFloor.HasFloors(map);
            int floor = floors ? IsoFloor.FloorOf(center, map) : 0;
            CellRect area = CellRect.CenteredOn(center, CampPlanner.GunAreaRadius);
            area.ClipInsideMap(map);
            foreach (IntVec3 cell in area)
            {
                if (floors && IsoFloor.FloorOf(cell, map) != floor || !ContainsCell(hull, cell)) continue;
                interior.Add(cell);
                if (!terrain.TryGetValue(cell, out bool clear))
                    terrain[cell] = clear = cell.Walkable(map) && !cell.Roofed(map);
                if (clear && !coverCells.Contains(cell)) { open.Add(cell); clearCells.Add(cell); }
            }
            // Count separated, locally open work areas. This is a cheap site
            // ranking, not a replacement for native placement or path checks.
            Capacity = WorkAreas(clearCells);
            Openness = interior.Count == 0 ? 0 : open.Count * 10 / interior.Count;
        }

        private static int WorkAreas(HashSet<IntVec3> clear)
        {
            var areas = new HashSet<(int, int)>();
            foreach (IntVec3 cell in clear)
            {
                bool open = true;
                foreach (IntVec3 offset in GenAdj.AdjacentCells)
                    if (!clear.Contains(cell + offset)) { open = false; break; }
                if (open) areas.Add((cell.x / WorkAreaSpacing, cell.z / WorkAreaSpacing));
            }
            return areas.Count;
        }

        internal bool Contains(IntVec3 cell) => interior.Contains(cell);

        internal IEnumerable<Slot> Candidates(ArtilleryPlacement template, bool direct, int ordinal, int count)
        {
            var key = (template.Def, template.Stuff);
            if (!slots.TryGetValue(key, out List<Slot> available))
            {
                slots[key] = available = new();
                foreach (IntVec3 cell in open)
                foreach (Rot4 rotation in rotations)
                {
                    CellRect footprint = GenAdj.OccupiedRect(cell, rotation, template.Def.Size);
                    IntVec3 work = ThingUtility.InteractionCellWhenAt(template.Def, cell, rotation, Map);
                    if (!clearCells.Contains(work) || !footprint.All(clearCells.Contains)) continue;
                    var placement = (template.Def, template.Stuff, rotation, cell);
                    if (!placements.TryGetValue(placement, out bool valid))
                        placements[placement] = valid = CampPlanner.CanPlace(Map,
                            new ArtilleryPlacement { Def = template.Def, Stuff = template.Stuff, Cell = cell, Rotation = rotation });
                    if (valid) available.Add(new Slot(cell, rotation));
                }
            }
            // Spread direct pieces across the front and mortars across the rear.
            // Physical separation and work/supply reservations are checked later.
            float lateral = count <= 1 ? 0f : Mathf.Lerp(-BatteryHalfWidth, BatteryHalfWidth, ordinal / (float)(count - 1));
            Vector2 desired = forward * (direct ? RowDepth : -RowDepth) + right * lateral;
            return available.OrderBy(slot =>
            {
                Vector2 offset = new Vector2(slot.Cell.x - Center.x, slot.Cell.z - Center.z);
                return (offset - desired).sqrMagnitude;
            });
        }

        private static long Cross(IntVec2 a, IntVec2 b, IntVec2 c) =>
            (long)(b.x - a.x) * (c.z - a.z) - (long)(b.z - a.z) * (c.x - a.x);

        private static List<IntVec2> Hull(IEnumerable<IntVec3> cells)
        {
            // Doubled cell-corner coordinates keep the envelope exact on the grid.
            var points = new HashSet<IntVec2>();
            foreach (IntVec3 cell in cells)
                for (int dx = -1; dx <= 1; dx += 2)
                for (int dz = -1; dz <= 1; dz += 2)
                    points.Add(new IntVec2(cell.x * 2 + 1 + dx, cell.z * 2 + 1 + dz));
            var ordered = points.OrderBy(p => p.x).ThenBy(p => p.z).ToList();
            var hull = new List<IntVec2>();
            foreach (IntVec2 point in ordered)
            {
                while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            int lower = hull.Count;
            for (int i = ordered.Count - 2; i >= 0; i--)
            {
                IntVec2 point = ordered[i];
                while (hull.Count > lower && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            if (hull.Count > 0) hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        private static bool ContainsCell(List<IntVec2> hull, IntVec3 cell)
        {
            if (hull.Count < 3) return false;
            for (int i = 0; i < hull.Count; i++)
            {
                IntVec2 a = hull[i], b = hull[(i + 1) % hull.Count];
                // The corner with the smallest cross product must be inside too.
                var corner = new IntVec2(cell.x * 2 + (b.z > a.z ? 2 : 0),
                    cell.z * 2 + (b.x < a.x ? 2 : 0));
                if (Cross(a, b, corner) < 0) return false;
            }
            return true;
        }
    }
}
