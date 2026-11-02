using System;
using System.Collections.Generic;
using System.Linq;
using RimSiege.Integration;
using IsoRimCEPackZ;
using UnityEngine;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    // Search with cheap geometry first; complete layouts validate native paths and supply.
    internal static class CampPlanner
    {
        internal const int GunAreaRadius = 8;
        internal const int SiteSurveyRadius = 12;
        private const int GunClearance = 1;
        private const int MaxLayoutRounds = 3;
        private const int MaxCoarseLayouts = 6;
        private const int MaxDirections = 3;
        private const int DirectionSectors = 8;

        private sealed record Site(IntVec3 Center, List<ArtilleryPlacement> Possible,
            int DirectCapacity, int SupportCapacity, int Openness, float Bearing, int Sector,
            float Deviation, float EntryDistance);

        private static int Sector(float bearing) =>
            Mathf.FloorToInt(Mathf.Repeat(bearing + 180f / DirectionSectors, 360f) / (360f / DirectionSectors));

        internal static bool TryPlan(Map map, Faction faction, IntVec3 entry, IntVec3 goal,
            IEnumerable<ArtilleryPlacement> guns, IEnumerable<IntVec3> centers,
            CampSiting siting, IReadOnlyDictionary<ArtilleryPlacement, float> effectiveRanges, out CampPlan plan,
            bool fortified)
        {
            plan = null;
            var templates = guns.ToList();
            if (!templates.Any(g => g.IsSupport) || siting.Targets.Count == 0 || !entry.InBounds(map)) return false;
            var direct = new HashSet<ArtilleryPlacement>(templates.Where(g => g.IsDirectSupport));
            float? retreat = direct.Count == 0 ? null : direct.Min(g => effectiveRanges[g]) - CampSiting.RangeMargin;
            templates = templates.OrderBy(g => g.FormationGroup)
                .ThenBy(g => direct.Contains(g) ? effectiveRanges[g] : 0f)
                .ThenByDescending(g => g.Def.Size.x * g.Def.Size.z).ToList();
            var placements = new Dictionary<(ThingDef, ThingDef, Rot4, IntVec3), bool>();
            var terrain = new Dictionary<IntVec3, bool>();
            var sites = new List<Site>();
            foreach (IntVec3 center in centers)
            {
                if (!center.InBounds(map) || !center.Walkable(map)) continue;
                bool directAllowed = !retreat.HasValue || siting.Allows(center, retreat.Value);
                var possible = templates.Where(g => !g.IsSupport || (directAllowed || !direct.Contains(g)) &&
                    InRange(map, center, g.Def, siting.TargetsFor(direct.Contains(g)), 14f,
                        effectiveRanges == null ? null : effectiveRanges[g])).ToList();
                if (!possible.Any(g => g.IsSupport) || !CanReach(map, entry, center, null)) continue;
                var survey = CampField.Survey(map, center, terrain);
                if (survey.Capacity == 0) continue;
                float bearing = siting.Bearing(center);
                sites.Add(new Site(center, possible, Math.Min(survey.Capacity, possible.Count(direct.Contains)),
                    Math.Min(survey.Capacity, possible.Count(g => g.IsSupport)), survey.Openness, bearing, Sector(bearing),
                    retreat.HasValue ? Math.Abs(siting.Distance(center, siting.BreachAnchor) - retreat.Value) : 0f,
                    siting.Distance(entry, center)));
            }

            var directions = sites.OrderByDescending(s => s.DirectCapacity).ThenByDescending(s => s.SupportCapacity)
                .ThenBy(s => s.EntryDistance).ThenBy(s => s.Deviation).ThenByDescending(s => s.Openness)
                .GroupBy(s => s.Sector).Select(group => new Queue<Site>(group)).ToList();
            var selected = new List<(Queue<Site> Queue, float Bearing)>();
            var candidates = new List<CampCandidate>();
            float entryBearing = siting.Bearing(entry);
            // Each direction gets its main proposal before any direction gets a
            // backup. Only the finite pool receives static corridor screening.
            while (directions.Count > 0 && selected.Count < MaxDirections)
            {
                Queue<Site> queue = directions.OrderByDescending(q => q.Peek().DirectCapacity)
                    .ThenByDescending(q => q.Peek().SupportCapacity).ThenBy(q => q.Peek().EntryDistance)
                    .ThenByDescending(q => selected.Count == 0
                        ? -Math.Abs(Mathf.DeltaAngle(entryBearing, q.Peek().Bearing))
                        : selected.Min(chosen => Math.Abs(Mathf.DeltaAngle(chosen.Bearing, q.Peek().Bearing))))
                    .ThenBy(q => q.Peek().Deviation).ThenByDescending(q => q.Peek().Openness).First();
                directions.Remove(queue);
                CampCandidate candidate = Next(queue);
                if (candidate == null) continue;
                candidates.Add(candidate);
                selected.Add((queue, candidate.Bearing));
            }
            foreach (var direction in selected)
            {
                if (candidates.Count >= MaxCoarseLayouts) break;
                CampCandidate backup = Next(direction.Queue);
                if (backup != null) candidates.Add(backup);
            }
            foreach (CampCandidate candidate in candidates)
                ScreenCandidate(faction, candidate, siting, effectiveRanges);

            CampCandidate best = null;
            int precise = 0;
            foreach (CampCandidate candidate in candidates.OrderByDescending(c => c.DirectCount)
                .ThenByDescending(c => c.SupportCount).ThenBy(c => c.EntryDistance)
                .ThenBy(c => c.Deviation).ThenByDescending(c => c.Field.Openness).ThenByDescending(c => c.DefenseCount))
            {
                if (candidate.SupportCount == 0 || best != null &&
                    (candidate.DirectCount < best.DirectCount || candidate.DirectCount == best.DirectCount &&
                     candidate.SupportCount < best.SupportCount)) continue;
                if (precise >= MaxLayoutRounds) break;
                if (ValidateAndRepair(map, faction, entry, candidate, siting, effectiveRanges, false, ref precise) &&
                    Better(candidate, best)) best = candidate;
            }
            plan = best?.Plan;
            if (plan != null && best.DirectCount > 0) plan.BreachAnchor = siting.BreachAnchor;
            return plan != null;

            CampCandidate Next(Queue<Site> queue)
            {
                while (queue.Count > 0)
                {
                    CampCandidate candidate = BuildCandidate(map, faction, entry, goal, queue.Dequeue(),
                        siting, effectiveRanges, terrain, placements, fortified);
                    if (candidate != null) return candidate;
                }
                return null;
            }
        }

        private static CampCandidate BuildCandidate(Map map, Faction faction, IntVec3 entry, IntVec3 goal,
            Site site, CampSiting siting, IReadOnlyDictionary<ArtilleryPlacement, float> ranges,
            Dictionary<IntVec3, bool> terrain, Dictionary<(ThingDef, ThingDef, Rot4, IntVec3), bool> placements,
            bool fortified)
        {
            var cover = fortified ? VanillaSiegeCover.Plan(map, faction, site.Center)
                .Select(item => new ArtilleryPlacement { Def = item.Def, Stuff = item.Stuff,
                    Cell = item.Cell, Rotation = item.Rotation }).ToList() : new List<ArtilleryPlacement>();
            if (fortified && cover.Count == 0) return null;
            var field = new CampField(map, site.Center, siting.BreachAnchor, cover, terrain, placements);
            if (field.Capacity == 0) return null;
            var plan = new CampPlan { Center = field.Center, Goal = goal, Cover = field.Cover };
            var retained = new List<ArtilleryPlacement>();
            foreach (ArtilleryPlacement template in site.Possible)
            {
                int ordinal = retained.Count(g => g.FormationGroup == template.FormationGroup);
                int count = site.Possible.Count(g => g.FormationGroup == template.FormationGroup);
                ArtilleryPlacement gun = FindPlacement(map, faction, entry, plan, template, siting,
                    ranges == null ? null : ranges[template], field, ordinal, count);
                if (gun != null) { plan.Guns.Add(gun); retained.Add(template); }
            }
            return !plan.Guns.Any(g => g.IsSupport) ? null : new CampCandidate(plan, field, retained,
                site.Bearing, site.Sector, site.Deviation, site.EntryDistance);
        }

        private static bool Better(CampCandidate candidate, CampCandidate previous)
        {
            if (previous == null) return true;
            if (candidate.DirectCount != previous.DirectCount) return candidate.DirectCount > previous.DirectCount;
            if (candidate.SupportCount != previous.SupportCount) return candidate.SupportCount > previous.SupportCount;
            // These costs came from the paths already checked for the complete
            // layout; no extra A* queries are introduced for the ranking.
            if (candidate.ApproachCost.HasValue && previous.ApproachCost.HasValue &&
                candidate.ApproachCost != previous.ApproachCost) return candidate.ApproachCost < previous.ApproachCost;
            if (candidate.EntryDistance != previous.EntryDistance) return candidate.EntryDistance < previous.EntryDistance;
            if (candidate.Deviation != previous.Deviation) return candidate.Deviation < previous.Deviation;
            if (candidate.Field.Openness != previous.Field.Openness) return candidate.Field.Openness > previous.Field.Openness;
            return candidate.DefenseCount > previous.DefenseCount;
        }

        private static void ScreenCandidate(Faction faction, CampCandidate candidate, CampSiting siting,
            IReadOnlyDictionary<ArtilleryPlacement, float> ranges)
        {
            GeometryFailures(faction, candidate, siting, ranges, out HashSet<ArtilleryPlacement> failed, out _);
            candidate.Remove(failed);
        }

        private static void GeometryFailures(Faction faction, CampCandidate candidate, CampSiting siting,
            IReadOnlyDictionary<ArtilleryPlacement, float> ranges, out HashSet<ArtilleryPlacement> failed, out bool lostFront)
        {
            failed = new HashSet<ArtilleryPlacement>();
            var blocked = new HashSet<ArtilleryPlacement>();
            CampPlan plan = candidate.Plan;
            for (int i = 0; i < plan.Guns.Count; i++)
            {
                ArtilleryPlacement gun = plan.Guns[i];
                if (!gun.IsSupport) continue;
                float? range = ranges == null ? null : ranges[candidate.Templates[i]];
                if (!siting.DeploymentTarget(faction, Spec(gun), range).HasTarget) failed.Add(gun);
                else if (gun.IsDirectSupport)
                {
                    if (!candidate.Corridors.TryGetValue(gun, out FixedCorridorResult result))
                        candidate.Corridors[gun] = result = siting.Candidates.CheckFixedCorridor(faction, Spec(gun), range,
                            siting.BreachTargets, CampSiting.RangeMargin);
                    if (!result.Clear) { failed.Add(gun); blocked.Add(gun); }
                }
            }
            var front = plan.Guns.Where(g => g.IsDirectSupport).ToList();
            lostFront = front.Count > 0 && front.All(blocked.Contains);
            MarkRetreatFailures(plan, siting, candidate.Templates, ranges, failed);
        }

        private static ArtilleryPlacement FindPlacement(Map map, Faction faction, IntVec3 entry, CampPlan camp,
            ArtilleryPlacement template, CampSiting siting, float? range, CampField field, int ordinal, int count,
            ArtilleryPlacement excluded = null, int? insertAt = null)
        {
            bool direct = template.IsDirectSupport;
            foreach (CampField.Slot slot in field.Candidates(template, ordinal, count))
            {
                var gun = new ArtilleryPlacement { Def = template.Def, Stuff = template.Stuff, Ammo = template.Ammo,
                    Cell = slot.Cell, Rotation = slot.Rotation, Operator = template.Operator, Role = template.Role };
                if (excluded != null && excluded.Cell == gun.Cell && excluded.Rotation == gun.Rotation) continue;
                if (!Fits(camp, gun) || gun.IsSupport &&
                    (!InRange(map, gun.Cell, gun.Def, siting.TargetsFor(direct), 2f, range) ||
                    !siting.DeploymentTarget(faction, Spec(gun), range).HasTarget)) continue;
                int index = insertAt ?? camp.Guns.Count;
                camp.Guns.Insert(index, gun);
                bool valid = LayoutValid(map, entry, camp, gun.Operator != null, false, field, out _);
                camp.Guns.RemoveAt(index);
                if (valid) return gun;
            }
            return null;
        }

        private static bool Fits(CampPlan camp, ArtilleryPlacement gun)
        {
            CellRect rect = GenAdj.OccupiedRect(gun.Cell, gun.Rotation, gun.Def.Size);
            foreach (ArtilleryPlacement cover in camp.Cover)
                if (rect.Overlaps(GenAdj.OccupiedRect(cover.Cell, cover.Rotation, cover.Def.Size))) return false;
            // Leave a clear cell between complete footprints, including 3x3 guns.
            foreach (ArtilleryPlacement existing in camp.Guns)
                if (rect.ExpandedBy(GunClearance).Overlaps(GenAdj.OccupiedRect(existing.Cell, existing.Rotation, existing.Def.Size))) return false;
            return true;
        }

        private static bool InRange(Map map, IntVec3 source, ThingDef def, IEnumerable<LocalTargetInfo> targets, float margin, float? effectiveRange)
        {
            ThingDef weapon = def?.building?.turretGunDef;
            if (weapon?.Verbs == null || weapon.Verbs.Count == 0) return true;
            float range = effectiveRange ?? weapon.Verbs.Max(v => v.range);
            return targets.Any(target => target.IsValid && target.Cell.InBounds(map) &&
                (IsoFloor.HasFloors(map) ? IsoFloor.RangeDistance(source, target.Cell, map) : (source - target.Cell).LengthHorizontal)
                <= range + margin + (target.Thing == null ? 0 : Math.Max(target.Thing.def.Size.x, target.Thing.def.Size.z)));
        }

        // Called immediately before placing blueprints. No generation-time
        // corridor cache survives into this fresh snapshot with actual operators.
        internal static bool Revalidate(Map map, ArtilleryRaid raid, CampPlan plan, CampSiting siting)
        {
            if (siting.Targets.Count == 0) return false;
            var job = (LordJob_Artillery)raid.CampLord.LordJob;
            if (job.RequiresOperators)
                plan.Guns.RemoveAll(g => g.NeedsOperator && (g.Operator == null || g.Operator.Dead ||
                    (g.Operator.Spawned ? g.Operator.Map != map : !raid.IndependentCrew || g.Operator.GetLord() != raid.CampLord)));
            var templates = plan.Guns.ToList();
            if (!templates.Any(g => g.IsSupport)) return false;
            var ranges = templates.ToDictionary(g => g, g => !g.IsSupport ? 0f : CeArtillerySight.EffectiveRange(map, raid.Faction, Spec(g)));
            if (!plan.BreachAnchor.IsValid && templates.Any(g => g.IsDirectSupport))
                plan.BreachAnchor = siting.BreachAnchor;
            var cache = new Dictionary<(ThingDef, ThingDef, Rot4, IntVec3), bool>();
            var field = new CampField(map, plan.Center, siting.BreachAnchor, plan.Cover, new(), cache)
            {
                ConstructionWorker = job.ConstructionWorker ?? raid.CampLord.ownedPawns
                    .FirstOrDefault(p => ArtilleryCrew.CanBuild((LordToil_Siege)raid.CampLord.CurLordToil, p))
            };
            float bearing = siting.Bearing(plan.Center);
            var direct = templates.Where(g => g.IsDirectSupport).ToList();
            float deviation = direct.Count == 0 ? 0f : Math.Abs(siting.Distance(plan.Center, siting.BreachAnchor) -
                (direct.Min(g => ranges[g]) - CampSiting.RangeMargin));
            var candidate = new CampCandidate(plan, field, templates, bearing, Sector(bearing), deviation, 0f);
            int rounds = 0;
            IntVec3 entry = field.ConstructionWorker?.Position ?? IntVec3.Invalid;
            return ValidateAndRepair(map, raid.Faction, entry, candidate, siting, ranges, job.RequiresOperators, ref rounds);
        }

        private static bool ValidateAndRepair(Map map, Faction faction, IntVec3 entry, CampCandidate candidate,
            CampSiting siting, IReadOnlyDictionary<ArtilleryPlacement, float> ranges, bool actual, ref int rounds)
        {
            CampPlan plan = candidate.Plan;
            List<ArtilleryPlacement> templates = candidate.Templates;
            var repaired = new HashSet<ArtilleryPlacement>();
            while (plan.Guns.Any(g => g.IsSupport) && rounds < MaxLayoutRounds)
            {
                rounds++;
                if (!ValidateLayout(map, faction, entry, candidate, siting, ranges, actual,
                    out HashSet<ArtilleryPlacement> failed, out bool lostFront)) return false;
                if (failed.Count == 0) return KeepSuppliedSubset(map, plan, siting, templates, ranges);
                var originalGuns = plan.Guns.ToList();
                var originalTemplates = templates.ToList();
                candidate.Remove(failed);
                int reinserted = 0;
                if (!lostFront && (actual || failed.Count == 1) && rounds < MaxLayoutRounds)
                {
                    for (int i = 0; i < originalGuns.Count; i++)
                    {
                        ArtilleryPlacement gun = originalGuns[i], template = originalTemplates[i];
                        if (!failed.Contains(gun) || !repaired.Add(template)) continue;
                        int index = reinserted;
                        for (int j = 0; j < i; j++) if (!failed.Contains(originalGuns[j])) index++;
                        ArtilleryPlacement replacement = FindPlacement(map, faction, actual && gun.NeedsOperator ? gun.Operator.Position : entry,
                            plan, gun, siting, ranges == null ? null : ranges[template], candidate.Field,
                            originalGuns.Take(i).Count(g => g.FormationGroup == gun.FormationGroup),
                            originalGuns.Count(g => g.FormationGroup == gun.FormationGroup), gun, index);
                        if (replacement == null) continue;
                        plan.Guns.Insert(index, replacement);
                        templates.Insert(index, template);
                        reinserted++;
                    }
                }
                if (reinserted > 0) continue;
                // Only already validated survivors remain. Removal cannot block
                // their paths; check retreat again before selecting a supplied set.
                return KeepSuppliedSubset(map, plan, siting, templates, ranges);
            }
            return false;
        }

        private static bool ValidateLayout(Map map, Faction faction, IntVec3 entry, CampCandidate candidate,
            CampSiting siting, IReadOnlyDictionary<ArtilleryPlacement, float> ranges, bool actual,
            out HashSet<ArtilleryPlacement> failed, out bool lostFront)
        {
            GeometryFailures(faction, candidate, siting, ranges, out failed, out lostFront);
            var guns = candidate.Plan.Guns.ToList();
            var templates = candidate.Templates.ToList();
            // Failed static pieces must not obstruct the surviving pieces' paths.
            candidate.Remove(failed);
            bool valid = LayoutFailures(map, entry, candidate.Plan, actual, true, candidate.Field,
                out HashSet<ArtilleryPlacement> pathFailures, candidate.ApproachCosts);
            candidate.Plan.Guns.Clear();
            candidate.Plan.Guns.AddRange(guns);
            candidate.Templates.Clear();
            candidate.Templates.AddRange(templates);
            failed.UnionWith(pathFailures);
            MarkRetreatFailures(candidate.Plan, siting, candidate.Templates, ranges, failed);
            return valid;
        }

        // These pieces have already passed this round's geometry and paths.
        // Removing planned obstacles cannot obstruct those paths. Keep the same
        // direct-first priority as candidate ranking; mortars remain a fallback.
        private static bool KeepSuppliedSubset(Map map, CampPlan plan, CampSiting siting,
            List<ArtilleryPlacement> templates, IReadOnlyDictionary<ArtilleryPlacement, float> ranges)
        {
            var guns = plan.Guns.ToList();
            var originals = templates.ToList();
            var priority = Enumerable.Range(0, guns.Count)
                .OrderBy(i => guns[i].FormationGroup)
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
                return plan.Guns.Any(g => g.IsSupport) && CampSupplies.CanSupply(map, plan);
            }
        }

        private static void MarkRetreatFailures(CampPlan plan, CampSiting siting, List<ArtilleryPlacement> templates,
            IReadOnlyDictionary<ArtilleryPlacement, float> ranges, HashSet<ArtilleryPlacement> failed)
        {
            if (ranges == null) return;
            var direct = Enumerable.Range(0, plan.Guns.Count).Where(i =>
                !failed.Contains(plan.Guns[i]) && plan.Guns[i].IsDirectSupport).ToList();
            if (direct.Count == 0 || siting.Allows(plan.Center,
                direct.Min(i => ranges[templates[i]]) - CampSiting.RangeMargin)) return;
            foreach (int i in direct) failed.Add(plan.Guns[i]);
        }

        private static EmplacementSpec Spec(ArtilleryPlacement p) => new EmplacementSpec(p.Def, p.Stuff, p.Ammo, p.Cell, p.Rotation, p.Operator);

        internal static bool CanPlace(Map map, ArtilleryPlacement item) => item.Def != null && item.Cell.InBounds(map) &&
            GenConstruct.CanPlaceBlueprintAt_NewTemp(item.Def, item.Cell, item.Rotation, map,
                false, null, null, item.Stuff, ignoreEdgeArea: true).Accepted;

        private static bool LayoutValid(Map map, IntVec3 entry, CampPlan plan, bool actual, bool precise, CampField field, out ArtilleryPlacement failed)
        {
            if (!LayoutFailures(map, entry, plan, actual, precise, field, out HashSet<ArtilleryPlacement> failures))
            { failed = null; return false; }
            failed = failures.FirstOrDefault();
            return failed == null;
        }

        // A bad cover makes the entire layout invalid; gun failures are collected
        // together, so one round can retain every independently valid piece.
        private static bool LayoutFailures(Map map, IntVec3 entry, CampPlan plan, bool actual, bool precise,
            CampField field, out HashSet<ArtilleryPlacement> failed,
            Dictionary<ArtilleryPlacement, float> approachCosts = null)
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
                if (!GenAdj.OccupiedRect(gun.Cell, gun.Rotation, gun.Def.Size).All(field.Contains) ||
                    gun.Cell.Roofed(map) || !FitsOtherGuns(plan, gun)) { failed.Add(gun); continue; }
                IntVec3 interaction = ThingUtility.InteractionCellWhenAt(gun.Def, gun.Cell, gun.Rotation, map);
                if (!field.Contains(interaction) || !interaction.Walkable(map) ||
                    (occupied.Contains(interaction) && !GenAdj.OccupiedRect(gun.Cell, gun.Rotation, gun.Def.Size).Contains(interaction)) || !reserved.Add(interaction))
                { failed.Add(gun); continue; }
                bool operated = actual && gun.NeedsOperator && gun.Operator?.Spawned == true;
                IntVec3 start = operated ? gun.Operator?.Position ?? IntVec3.Invalid : entry;
                Pawn pawn = operated ? gun.Operator : field.ConstructionWorker;
                var blockers = new HashSet<IntVec3>(impassable);
                blockers.Remove(interaction);
                float approach = 0f;
                if (!(precise ? Reachable(map, start, interaction, blockers, pawn, out approach) : CanReach(map, start, interaction, pawn)))
                { failed.Add(gun); reserved.Remove(interaction); continue; }
                bool supply = false;
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 cell = interaction + offset;
                    if (field.Contains(cell) && cell.Walkable(map) && !occupied.Contains(cell) && !reserved.Contains(cell) &&
                        (precise ? Reachable(map, start, cell, impassable, pawn) : CanReach(map, start, cell, pawn)))
                    { reserved.Add(cell); supply = true; break; }
                }
                if (!supply) { failed.Add(gun); reserved.Remove(interaction); }
                else if (precise && approachCosts != null) approachCosts[gun] = approach;
            }
            return true;
        }

        private static bool FitsOtherGuns(CampPlan plan, ArtilleryPlacement gun)
        {
            CellRect rect = GenAdj.OccupiedRect(gun.Cell, gun.Rotation, gun.Def.Size).ExpandedBy(GunClearance);
            return plan.Guns.All(other => other == gun ||
                !rect.Overlaps(GenAdj.OccupiedRect(other.Cell, other.Rotation, other.Def.Size)));
        }

        private static TraverseParms Traversal(Pawn pawn) => pawn == null
            ? TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly) : TraverseParms.For(pawn, Danger.Deadly);

        private static bool CanReach(Map map, IntVec3 start, IntVec3 end, Pawn pawn) =>
            start.InBounds(map) && end.InBounds(map) && map.reachability.CanReach(start, end, PathEndMode.OnCell, Traversal(pawn));

        private static bool Reachable(Map map, IntVec3 start, IntVec3 end, HashSet<IntVec3> occupied, Pawn pawn)
            => Reachable(map, start, end, occupied, pawn, out _);

        private static bool Reachable(Map map, IntVec3 start, IntVec3 end, HashSet<IntVec3> occupied, Pawn pawn, out float cost)
        {
            cost = 0f;
            if (!CanReach(map, start, end, pawn)) return false;
            using (PawnPath path = map.pathFinder.FindPathNow(start, end, Traversal(pawn), null, PathEndMode.OnCell))
            {
                if (path == null || !path.Found) return false;
                foreach (IntVec3 cell in path.NodesReversed)
                    if (occupied.Contains(cell)) return false;
                cost = path.TotalCost;
                return true;
            }
        }
    }
}
