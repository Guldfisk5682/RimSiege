using System;
using System.Collections.Generic;
using System.Linq;
using RimSiege.Integration;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege.Raids.Artillery
{
    // Search with cheap geometry first; complete layouts validate native paths and supply.
    internal static class CampPlanner
    {
        internal const int GunAreaRadius = 8;
        private const int MaxCellsPerGun = 200;
        private const int MaxLayoutRounds = 3;

        internal static bool TryPlan(Map map, Faction faction, IntVec3 entry, IntVec3 goal,
            IEnumerable<ArtilleryPlacement> guns, IEnumerable<IntVec3> centers,
            CampSiting siting, IReadOnlyDictionary<ArtilleryPlacement, float> effectiveRanges, out CampPlan plan)
        {
            plan = null;
            var templates = guns.ToList();
            if (templates.Count == 0 || siting.Targets.Count == 0 || !entry.InBounds(map)) return false;
            var direct = new HashSet<ArtilleryPlacement>(templates.Where(g => CeArtillerySight.IsDirectFire(g.Def, g.Ammo)));
            float? retreat = direct.Count == 0 ? null : direct.Min(g => effectiveRanges[g]) - CampSiting.RangeMargin;
            templates = templates.OrderBy(g => direct.Contains(g) ? 0 : 1)
                .ThenBy(g => direct.Contains(g) ? effectiveRanges[g] : 0f).ToList();
            var placements = new Dictionary<(ThingDef, ThingDef, Rot4, IntVec3), bool>();
            var partials = new List<(CampPlan Plan, List<ArtilleryPlacement> Templates, int Direct)>();
            int precise = 0;
            foreach (IntVec3 center in centers)
            {
                if (!center.InBounds(map) || !center.Walkable(map)) continue;
                bool directAllowed = !retreat.HasValue || siting.Allows(center, retreat.Value);
                var possible = templates.Where(g => (directAllowed || !direct.Contains(g)) &&
                    InRange(map, center, g.Def, siting.Targets, 14f,
                        effectiveRanges == null ? null : effectiveRanges[g])).ToList();
                if (possible.Count == 0 || !CanReach(map, entry, center, null)) continue;
                var cover = VanillaSiegeCover.Plan(map, faction, center)
                    .Select(item => new ArtilleryPlacement { Def = item.Def, Stuff = item.Stuff, Ammo = item.Ammo,
                        Cell = item.Cell, Rotation = item.Rotation, Operator = item.Operator }).ToList();
                if (cover.Count == 0) continue;
                // One retry lets a large obstructed piece move before any reduction.
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    var candidate = new CampPlan { Center = center, Goal = goal, Cover = cover };
                    var retained = new List<ArtilleryPlacement>();
                    foreach (ArtilleryPlacement template in possible)
                    {
                        float? range = effectiveRanges == null ? null : effectiveRanges[template];
                        ArtilleryPlacement gun = FindPlacement(map, faction, entry, candidate, template, siting, range, placements);
                        if (gun != null) { candidate.Guns.Add(gun); retained.Add(template); }
                    }
                    if (candidate.Guns.Count < possible.Count && attempt == 0) continue;
                    if (candidate.Guns.Count == 0) break;
                    int directCount = retained.Count(direct.Contains);
                    if (candidate.Guns.Count == templates.Count && precise < MaxLayoutRounds)
                    {
                        if (ValidateAndRepair(map, faction, entry, candidate, siting, retained,
                            effectiveRanges, false, placements, ref precise))
                        { plan = candidate; return true; }
                        break;
                    }
                    partials.Add((candidate, retained, directCount));
                    partials = partials.OrderByDescending(p => p.Direct).ThenByDescending(p => p.Plan.Guns.Count)
                        .Take(MaxLayoutRounds).ToList();
                    break;
                }
                if (direct.Count == 0 && partials.Count > 0) break;
            }
            foreach (var partial in partials.OrderByDescending(p => p.Direct).ThenByDescending(p => p.Plan.Guns.Count))
            {
                if (precise >= MaxLayoutRounds) break;
                if (ValidateAndRepair(map, faction, entry, partial.Plan, siting, partial.Templates,
                    effectiveRanges, false, placements, ref precise))
                { plan = partial.Plan; return true; }
            }
            return false;
        }

        private static ArtilleryPlacement FindPlacement(Map map, Faction faction, IntVec3 entry, CampPlan camp,
            ArtilleryPlacement template, CampSiting siting, float? range,
            Dictionary<(ThingDef, ThingDef, Rot4, IntVec3), bool> placements, ArtilleryPlacement excluded = null, int? insertAt = null)
        {
            CellRect area = CellRect.CenteredOn(camp.Center, GunAreaRadius);
            area.ClipInsideMap(map);
            var cells = area.Cells.ToList();
            cells.Shuffle();
            IntVec3 anchor = siting.BreachAnchor;
            IntVec3 centerGround = IsoFloor.HasFloors(map) ? IsoFloor.GroundCellOf(camp.Center, map) : camp.Center;
            IntVec3 anchorGround = IsoFloor.HasFloors(map) ? IsoFloor.GroundCellOf(anchor, map) : anchor;
            int dx = anchorGround.x - centerGround.x, dz = anchorGround.z - centerGround.z;
            cells = cells.OrderBy(cell => (cell.x - centerGround.x) * dx +
                    ((IsoFloor.HasFloors(map) ? IsoFloor.GroundCellOf(cell, map).z : cell.z) - centerGround.z) * dz >= 0 ? 0 : 1)
                .ThenBy(cell => siting.Distance(cell, anchor)).ToList();
            Rot4[] rotations = PreferredRotations(map, camp.Center, anchor);
            int limit = Math.Min(MaxCellsPerGun, cells.Count);
            for (int i = 0; i < limit; i++)
            foreach (Rot4 rotation in rotations)
            {
                var gun = new ArtilleryPlacement { Def = template.Def, Stuff = template.Stuff, Ammo = template.Ammo,
                    Cell = cells[i], Rotation = rotation, Operator = template.Operator };
                if (excluded != null && excluded.Cell == gun.Cell && excluded.Rotation == gun.Rotation) continue;
                if (!StaticPlaceable(map, camp.Center, gun, placements) || !Fits(camp, gun) ||
                    !InRange(map, gun.Cell, gun.Def, siting.Targets, 2f, range)) continue;
                if (!siting.Candidates.FindGeometry(faction, Spec(gun), range).HasTarget) continue;
                int index = insertAt ?? camp.Guns.Count;
                camp.Guns.Insert(index, gun);
                bool valid = LayoutValid(map, entry, camp, gun.Operator != null, false, out _);
                camp.Guns.RemoveAt(index);
                if (valid) return gun;
            }
            return null;
        }

        private static Rot4[] PreferredRotations(Map map, IntVec3 from, IntVec3 to)
        {
            if (IsoFloor.HasFloors(map)) { from = IsoFloor.GroundCellOf(from, map); to = IsoFloor.GroundCellOf(to, map); }
            int x = to.x - from.x, z = to.z - from.z;
            Rot4 horizontal = x >= 0 ? Rot4.East : Rot4.West;
            Rot4 vertical = z >= 0 ? Rot4.North : Rot4.South;
            Rot4 first = Math.Abs(x) > Math.Abs(z) ? horizontal : vertical;
            Rot4 second = Math.Abs(x) > Math.Abs(z) ? vertical : horizontal;
            return new[] { first, second, first.Opposite, second.Opposite };
        }

        private static bool StaticPlaceable(Map map, IntVec3 center, ArtilleryPlacement item,
            Dictionary<(ThingDef, ThingDef, Rot4, IntVec3), bool> cache)
        {
            var key = (item.Def, item.Stuff, item.Rotation, item.Cell);
            if (!cache.TryGetValue(key, out bool value))
            {
                value = item.Cell.InBounds(map) && !item.Cell.Roofed(map) && CanPlace(map, item);
                cache.Add(key, value);
            }
            return value && (!IsoFloor.HasFloors(map) || IsoFloor.FloorOf(item.Cell, map) == IsoFloor.FloorOf(center, map));
        }

        private static bool Fits(CampPlan camp, ArtilleryPlacement gun)
        {
            CellRect rect = GenAdj.OccupiedRect(gun.Cell, gun.Rotation, gun.Def.Size);
            foreach (ArtilleryPlacement existing in camp.Cover.Concat(camp.Guns))
                foreach (IntVec3 cell in GenAdj.OccupiedRect(existing.Cell, existing.Rotation, existing.Def.Size))
                    if (rect.Contains(cell)) return false;
            return true;
        }

        private static bool InRange(Map map, IntVec3 source, ThingDef def, List<LocalTargetInfo> targets, float margin, float? effectiveRange)
        {
            ThingDef weapon = def?.building?.turretGunDef;
            if (weapon?.Verbs == null || weapon.Verbs.Count == 0) return true;
            float range = effectiveRange ?? weapon.Verbs.Max(v => v.range);
            return targets.Any(target => target.IsValid && target.Cell.InBounds(map) &&
                (IsoFloor.HasFloors(map) ? IsoFloor.RangeDistance(source, target.Cell, map) : (source - target.Cell).LengthHorizontal)
                <= range + margin + (target.Thing == null ? 0 : Math.Max(target.Thing.def.Size.x, target.Thing.def.Size.z)));
        }

        // Called immediately before placing blueprints, using actual operators.
        internal static bool Revalidate(Map map, ArtilleryRaid raid, CampPlan plan, CampSiting siting)
        {
            if (siting.Targets.Count == 0) return false;
            plan.Guns.RemoveAll(g => g.Operator == null || !g.Operator.Spawned || g.Operator.Map != map);
            var templates = plan.Guns.ToList();
            var ranges = templates.ToDictionary(g => g, g => CeArtillerySight.EffectiveRange(map, raid.Faction, Spec(g)));
            var cache = new Dictionary<(ThingDef, ThingDef, Rot4, IntVec3), bool>();
            int rounds = 0;
            return ValidateAndRepair(map, raid.Faction, IntVec3.Invalid, plan, siting, templates,
                ranges, true, cache, ref rounds);
        }

        // Each round validates the whole current layout. Failed pieces may move once;
        // on the last round, already validated survivors remain available.
        private static bool ValidateAndRepair(Map map, Faction faction, IntVec3 entry, CampPlan plan, CampSiting siting,
            List<ArtilleryPlacement> templates, IReadOnlyDictionary<ArtilleryPlacement, float> ranges, bool actual,
            Dictionary<(ThingDef, ThingDef, Rot4, IntVec3), bool> cache, ref int rounds)
        {
            var repaired = new HashSet<ArtilleryPlacement>();
            while (plan.Guns.Count > 0 && rounds < MaxLayoutRounds)
            {
                rounds++;
                if (!LayoutFailures(map, entry, plan, actual, true, out HashSet<ArtilleryPlacement> failed)) return false;
                for (int i = 0; i < plan.Guns.Count; i++)
                {
                    ArtilleryPlacement gun = plan.Guns[i];
                    if (failed.Contains(gun)) continue;
                    float? range = ranges == null ? null : ranges[templates[i]];
                    if (!siting.Candidates.FindGeometry(faction, Spec(gun), range).HasTarget)
                        failed.Add(gun);
                }
                MarkRetreatFailures(plan, siting, templates, ranges, failed);
                if (failed.Count == 0) return KeepSuppliedSubset(map, plan, siting, templates, ranges);

                var originalGuns = plan.Guns.ToList();
                var originalTemplates = templates.ToList();
                plan.Guns.Clear();
                templates.Clear();
                for (int i = 0; i < originalGuns.Count; i++)
                    if (!failed.Contains(originalGuns[i])) { plan.Guns.Add(originalGuns[i]); templates.Add(originalTemplates[i]); }

                if (rounds < MaxLayoutRounds)
                {
                    int reinserted = 0;
                    for (int i = 0; i < originalGuns.Count; i++)
                    {
                        ArtilleryPlacement gun = originalGuns[i], template = originalTemplates[i];
                        if (!failed.Contains(gun) || !repaired.Add(template)) continue;
                        int index = reinserted;
                        for (int j = 0; j < i; j++) if (!failed.Contains(originalGuns[j])) index++;
                        float? range = ranges == null ? null : ranges[template];
                        ArtilleryPlacement replacement = FindPlacement(map, faction, actual ? gun.Operator.Position : entry,
                            plan, gun, siting, range, cache, gun, index);
                        if (replacement == null) continue;
                        plan.Guns.Insert(index, replacement);
                        templates.Insert(index, template);
                        reinserted++;
                    }
                    continue;
                }

                // Removing shorter-ranged direct guns can increase the minimum
                // retreat for the remaining direct pieces; preserve mortars.
                var retreatFailures = new HashSet<ArtilleryPlacement>();
                MarkRetreatFailures(plan, siting, templates, ranges, retreatFailures);
                for (int i = plan.Guns.Count - 1; i >= 0; i--)
                    if (retreatFailures.Contains(plan.Guns[i])) { plan.Guns.RemoveAt(i); templates.RemoveAt(i); }
                return KeepSuppliedSubset(map, plan, siting, templates, ranges);
            }
            return false;
        }

        // These pieces have already passed this round's geometry and paths.
        // Removing planned obstacles cannot obstruct those paths. Prefer keeping
        // indirect support when reducing a crowded camp to leave room for its pods.
        private static bool KeepSuppliedSubset(Map map, CampPlan plan, CampSiting siting,
            List<ArtilleryPlacement> templates, IReadOnlyDictionary<ArtilleryPlacement, float> ranges)
        {
            var guns = plan.Guns.ToList();
            var originals = templates.ToList();
            var priority = Enumerable.Range(0, guns.Count)
                .OrderBy(i => CeArtillerySight.IsDirectFire(guns[i].Def, guns[i].Ammo) ? 1 : 0)
                .ThenBy(i => ranges == null ? 0f : ranges[originals[i]]).ToList();
            for (int count = priority.Count; count > 0; count--)
                if (TrySubset(priority.Take(count))) return true;
            // A different single emplacement can expose a different legal drop spot.
            foreach (int index in priority.Skip(1))
                if (TrySubset(new[] { index })) return true;
            plan.Guns.Clear();
            templates.Clear();
            return false;

            bool TrySubset(IEnumerable<int> indices)
            {
                var retained = new HashSet<int>(indices);
                plan.Guns.Clear();
                templates.Clear();
                for (int i = 0; i < guns.Count; i++)
                    if (retained.Contains(i)) { plan.Guns.Add(guns[i]); templates.Add(originals[i]); }
                var failed = new HashSet<ArtilleryPlacement>();
                MarkRetreatFailures(plan, siting, templates, ranges, failed);
                for (int i = plan.Guns.Count - 1; i >= 0; i--)
                    if (failed.Contains(plan.Guns[i])) { plan.Guns.RemoveAt(i); templates.RemoveAt(i); }
                return plan.Guns.Count > 0 && CampSupplies.CanSupply(map, plan);
            }
        }

        private static void MarkRetreatFailures(CampPlan plan, CampSiting siting, List<ArtilleryPlacement> templates,
            IReadOnlyDictionary<ArtilleryPlacement, float> ranges, HashSet<ArtilleryPlacement> failed)
        {
            if (ranges == null) return;
            var direct = Enumerable.Range(0, plan.Guns.Count).Where(i =>
                !failed.Contains(plan.Guns[i]) && CeArtillerySight.IsDirectFire(plan.Guns[i].Def, plan.Guns[i].Ammo)).ToList();
            if (direct.Count == 0 || siting.Allows(plan.Center,
                direct.Min(i => ranges[templates[i]]) - CampSiting.RangeMargin)) return;
            foreach (int i in direct) failed.Add(plan.Guns[i]);
        }

        private static EmplacementSpec Spec(ArtilleryPlacement p) => new EmplacementSpec(p.Def, p.Stuff, p.Ammo, p.Cell, p.Rotation, p.Operator);

        private static bool CanPlace(Map map, ArtilleryPlacement item) => item.Def != null && item.Cell.InBounds(map) &&
            GenConstruct.CanPlaceBlueprintAt_NewTemp(item.Def, item.Cell, item.Rotation, map,
                false, null, null, item.Stuff, ignoreEdgeArea: true).Accepted;

        private static bool LayoutValid(Map map, IntVec3 entry, CampPlan plan, bool actual, bool precise, out ArtilleryPlacement failed)
        {
            if (!LayoutFailures(map, entry, plan, actual, precise, out HashSet<ArtilleryPlacement> failures))
            { failed = null; return false; }
            failed = failures.FirstOrDefault();
            return failed == null;
        }

        // A bad cover makes the entire layout invalid; gun failures are collected
        // together, so one round can retain every independently valid piece.
        private static bool LayoutFailures(Map map, IntVec3 entry, CampPlan plan, bool actual, bool precise,
            out HashSet<ArtilleryPlacement> failed)
        {
            failed = new HashSet<ArtilleryPlacement>();
            var occupied = new HashSet<IntVec3>();
            var impassable = new HashSet<IntVec3>();
            var reserved = new HashSet<IntVec3>();
            foreach (ArtilleryPlacement item in plan.Cover.Concat(plan.Guns))
            {
                bool gun = plan.Guns.Contains(item);
                if (precise && !CanPlace(map, item))
                {
                    if (!gun) return false;
                    failed.Add(item);
                }
                foreach (IntVec3 cell in GenAdj.OccupiedRect(item.Cell, item.Rotation, item.Def.Size))
                {
                    if (!cell.InBounds(map) || !occupied.Add(cell))
                    {
                        if (!gun) return false;
                        failed.Add(item);
                    }
                    if (cell.InBounds(map) && item.Def.passability == Traversability.Impassable) impassable.Add(cell);
                }
            }
            foreach (ArtilleryPlacement gun in plan.Guns)
            {
                if (failed.Contains(gun)) continue;
                if (gun.Cell.Roofed(map)) { failed.Add(gun); continue; }
                IntVec3 interaction = ThingUtility.InteractionCellWhenAt(gun.Def, gun.Cell, gun.Rotation, map);
                if (!interaction.InBounds(map) || !interaction.Walkable(map) ||
                    (occupied.Contains(interaction) && !GenAdj.OccupiedRect(gun.Cell, gun.Rotation, gun.Def.Size).Contains(interaction)) || !reserved.Add(interaction))
                { failed.Add(gun); continue; }
                IntVec3 start = actual ? gun.Operator?.Position ?? IntVec3.Invalid : entry;
                Pawn pawn = actual ? gun.Operator : null;
                var blockers = new HashSet<IntVec3>(impassable);
                blockers.Remove(interaction);
                if (!(precise ? Reachable(map, start, interaction, blockers, pawn) : CanReach(map, start, interaction, pawn)))
                { failed.Add(gun); reserved.Remove(interaction); continue; }
                bool supply = false;
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 cell = interaction + offset;
                    if (cell.InBounds(map) && cell.Walkable(map) && !occupied.Contains(cell) && !reserved.Contains(cell) &&
                        (precise ? Reachable(map, start, cell, impassable, pawn) : CanReach(map, start, cell, pawn)))
                    { reserved.Add(cell); supply = true; break; }
                }
                if (!supply) { failed.Add(gun); reserved.Remove(interaction); }
            }
            return true;
        }

        private static TraverseParms Traversal(Pawn pawn) => pawn == null
            ? TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly) : TraverseParms.For(pawn, Danger.Deadly);

        private static bool CanReach(Map map, IntVec3 start, IntVec3 end, Pawn pawn) =>
            start.InBounds(map) && end.InBounds(map) && map.reachability.CanReach(start, end, PathEndMode.OnCell, Traversal(pawn));

        private static bool Reachable(Map map, IntVec3 start, IntVec3 end, HashSet<IntVec3> occupied, Pawn pawn)
        {
            if (!CanReach(map, start, end, pawn)) return false;
            using (PawnPath path = map.pathFinder.FindPathNow(start, end, Traversal(pawn), null, PathEndMode.OnCell))
            {
                if (path == null || !path.Found) return false;
                foreach (IntVec3 cell in path.NodesReversed)
                    if (occupied.Contains(cell)) return false;
                return true;
            }
        }
    }
}
