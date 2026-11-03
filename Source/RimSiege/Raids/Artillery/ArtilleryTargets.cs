using System;
using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using IsoRimCEPackZ;
using RimSiege.Integration;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSiege.Raids.Artillery
{
    internal enum ArtilleryTargetReason
    {
        NoCandidates, InvalidTarget, UnavailableWeapon, OutsideRange, BelowMinimumRange,
        OutsideArc, DisabledThreat, CeRejected, GeometryPassed, Accepted, InsufficientDeploymentReserve
    }

    // This reports shooting only. Operator path validation belongs to CampPlanner.
    internal readonly struct ArtilleryTargetResult
    {
        internal readonly LocalTargetInfo Target;
        internal readonly ArtilleryTargetReason Reason;
        internal readonly bool ShotChecked, OperatorChecked;
        internal bool HasTarget => Reason == ArtilleryTargetReason.GeometryPassed || Reason == ArtilleryTargetReason.Accepted;

        internal ArtilleryTargetResult(LocalTargetInfo target, ArtilleryTargetReason reason, bool shotChecked, bool operatorChecked)
        { Target = target; Reason = reason; ShotChecked = shotChecked; OperatorChecked = operatorChecked; }
    }

    // A short-lived raid snapshot, shared by planning and all of its firing guns.
    // Membership is rebuilt on demand; it is not a continuously maintained map index.
    internal sealed class ArtilleryTargets
    {
        // A wave-wide, short-lived enumeration; each camp still owns its route/ranking.
        internal sealed class Catalog
        {
            internal readonly List<Building> Buildings;
            internal readonly HashSet<Building> Combat;
            internal readonly List<Pawn> Pawns;
            internal Catalog(Map map)
            {
                Buildings = map.listerBuildings.allBuildingsColonist.ToList();
                Combat = new(map.listerBuildings.allBuildingsColonistCombatTargets);
                Pawns = map.mapPawns.AllPawnsSpawned.Where(p => p.Faction == Faction.OfPlayer && !p.Dead).ToList();
            }
        }
        internal const int SearchInterval = 25;
        internal readonly Map Map;
        internal readonly IntVec3 Goal;
        internal readonly List<Thing> Walls = new();
        internal readonly List<Thing> Fallback = new();
        private readonly Dictionary<Thing, WallShape> shapes = new();
        private readonly bool floors;

        private readonly record struct WallShape(bool Door, bool Corner, bool Horizontal, bool Vertical, bool OnRoute);
        private readonly record struct Ranked(Thing Thing, int Band, WallShape Shape, float Normal, float Distance);

        internal ArtilleryTargets(Map map, IntVec3 goal, SupportRoute route, Catalog catalog = null)
        {
            Map = map; Goal = goal; floors = IsoFloor.HasFloors(map);
            catalog ??= new Catalog(map);
            foreach (Building building in catalog.Buildings)
            {
                if (!Valid(building)) continue;
                if ((building.def.IsWall || building.def.IsDoor) && BreachingUtility.ShouldBreachBuilding(building))
                    Walls.Add(building);
                else if (catalog.Combat.Contains(building)) Fallback.Add(building);
            }
            foreach (Pawn pawn in catalog.Pawns)
                if (pawn.Faction == Faction.OfPlayer && !pawn.Dead) Fallback.Add(pawn);

            var cells = new HashSet<IntVec3>(Walls.SelectMany(t => t.OccupiedRect()));
            foreach (Thing wall in Walls)
            {
                IntVec3 cell = wall.Position;
                bool horizontal = cells.Contains(cell + IntVec3.East) || cells.Contains(cell + IntVec3.West);
                bool vertical = cells.Contains(cell + IntVec3.North) || cells.Contains(cell + IntVec3.South);
                shapes.Add(wall, new WallShape(wall.def.IsDoor, horizontal && vertical, horizontal, vertical,
                    route?.Affects(cell) == true));
            }
        }

        internal IEnumerable<LocalTargetInfo> All => Walls.Concat(Fallback).Select(t => (LocalTargetInfo)t);

        // A battery-wide breach front, bounded by its work-area radius. Group
        // floors in the same ground column; the native filter still checks the
        // actual target floor. An unrelated closer wall cannot justify this camp.
        internal List<Thing> FrontAt(IntVec3 anchor)
        {
            bool Near(Thing target) => target.OccupiedRect().ClosestDistSquaredTo(
                floors ? IsoFloor.OnFloorOf(anchor, target.Position, Map) : anchor)
                <= CampPlanner.GunAreaRadius * CampPlanner.GunAreaRadius;
            var walls = Walls.Where(Near).ToList();
            return walls.Count != 0 ? walls : Fallback.Where(Near).ToList();
        }

        internal bool Valid(Thing target) => target != null && target.Spawned && !target.Destroyed &&
            target.Map == Map && target.Faction == Faction.OfPlayer;

        // Deployment proves geometry only; actual CE shot eligibility is checked by firing guns.
        internal ArtilleryTargetResult FindGeometry(Faction faction, EmplacementSpec placement, float? effectiveRange = null,
            List<Thing> deploymentTargets = null, float reserve = 0f)
        {
            if (CeArtillerySight.Open(Map, faction, placement, out var query) != GeometryStatus.Success)
                return new(LocalTargetInfo.Invalid, ArtilleryTargetReason.UnavailableWeapon, false, false);
            using (query)
            {
                if (query.ReadWeapon(out var weapon) != GeometryStatus.Success)
                    return new(LocalTargetInfo.Invalid, ArtilleryTargetReason.UnavailableWeapon, false, false);
                ArtilleryTargetReason reason = ArtilleryTargetReason.NoCandidates;
                foreach (List<Thing> group in deploymentTargets == null ? new[] { Walls, Fallback } : new[] { deploymentTargets })
                    foreach (Thing target in group)
                    {
                        reason = FilterCandidate(query, faction, placement, weapon, target,
                            !shapes.ContainsKey(target), effectiveRange ?? weapon.EffectiveRange, reserve);
                        if (reason == ArtilleryTargetReason.GeometryPassed) return new(target, reason, false, false);
                    }
                return new(LocalTargetInfo.Invalid, reason, false, false);
            }
        }

        internal ArtilleryTargetResult Find(Building_TurretGunCE gun) => Search(gun, wallsOnly: false);
        internal ArtilleryTargetResult FindWalls(Building_TurretGunCE gun) => Search(gun, wallsOnly: true);

        // Keep the same two representative segments and candidate lifetime; CEPack owns the fixed geometry trace.
        internal FixedCorridorResult CheckFixedCorridor(Faction faction, EmplacementSpec placement, float? effectiveRange,
            List<Thing> deploymentTargets = null, float reserve = 0f)
        {
            if (CeArtillerySight.Open(Map, faction, placement, out var query) != GeometryStatus.Success)
                return new(FixedObstruction.Unavailable, IntVec3.Invalid, default);
            using (query)
            {
                if (query.ReadWeapon(out var weapon) != GeometryStatus.Success)
                    return new(FixedObstruction.Unavailable, IntVec3.Invalid, default);
                float range = effectiveRange ?? weapon.EffectiveRange;
                foreach (List<Thing> group in deploymentTargets == null ? new[] { Walls, Fallback } : new[] { deploymentTargets })
                {
                    var ranked = Order(group.Where(t => FilterCandidate(query, faction, placement, weapon, t,
                        !shapes.ContainsKey(t), range, reserve) == ArtilleryTargetReason.GeometryPassed)
                        .Select(t => Rank(placement.Cell, t))).ToList();
                    if (ranked.Count == 0) continue;
                    Thing first = ranked[0].Thing;
                    if (query.TraceFixedCorridor(first, out var result) != GeometryStatus.Success)
                        return new(FixedObstruction.Unavailable, IntVec3.Invalid, default);
                    if (result.Clear) return result;
                    Thing second = ranked.Skip(1).Select(c => c.Thing).FirstOrDefault(t =>
                        (t.Position - first.Position).LengthHorizontalSquared >= 16);
                    if (second != null && query.TraceFixedCorridor(second, out var other) == GeometryStatus.Success && other.Clear)
                        return other;
                    return result;
                }
                return new(FixedObstruction.Unavailable, IntVec3.Invalid, default);
            }
        }

        private ArtilleryTargetReason FilterCandidate(IPlacementQuery query, Faction faction, EmplacementSpec placement,
            WeaponGeometry weapon, Thing target, bool fallback, float range, float reserve)
        {
            if (!Valid(target) || !(placement.Operator?.Faction ?? faction).HostileTo(target.Faction))
                return ArtilleryTargetReason.InvalidTarget;
            if (query.CheckRangeAndArc(target, out var geometry) != GeometryStatus.Success)
                return ArtilleryTargetReason.UnavailableWeapon;
            if (geometry.MaximumRangeDistance > range) return ArtilleryTargetReason.OutsideRange;
            if (reserve > 0f && geometry.MaximumRangeDistance > range - reserve) return ArtilleryTargetReason.InsufficientDeploymentReserve;
            float minimum = geometry.MinimumRange;
            if (weapon.MinimumRangeInCover >= 0f) minimum = Math.Min(minimum, weapon.MinimumRangeInCover);
            if (geometry.MinimumRangeDistance + Math.Max(target.def.Size.x, target.def.Size.z) + 2f < minimum)
                return ArtilleryTargetReason.BelowMinimumRange;
            if (!geometry.WithinArc) return ArtilleryTargetReason.OutsideArc;
            if (fallback && (geometry.ThreatDisabled || target is Pawn pawn &&
                (weapon.Mannable && pawn.RaceProps.Animal || !weapon.Mannable && GenAI.MachinesLike(faction, pawn)) ||
                weapon.Incendiary && target.IsBurning())) return ArtilleryTargetReason.DisabledThreat;
            return ArtilleryTargetReason.GeometryPassed;
        }

        private ArtilleryTargetResult Search(Building_TurretGunCE gun, bool wallsOnly)
        {
            if (gun.AttackVerb is not Verb_LaunchProjectileCE verb)
                return new(LocalTargetInfo.Invalid, ArtilleryTargetReason.UnavailableWeapon, false, false);
            Pawn operatorPawn = CeArtillerySight.OperatorFor(gun);
            ArtilleryTargetReason reason = ArtilleryTargetReason.NoCandidates;
            bool ceRejected = false;
            foreach (List<Thing> group in wallsOnly ? new[] { Walls } : new[] { Walls, Fallback })
            {
                var eligible = new List<Ranked>();
                foreach (Thing target in group)
                {
                    reason = Filter(gun, target, verb.EffectiveRange, !wallsOnly && group == Fallback);
                    if (reason == ArtilleryTargetReason.GeometryPassed) eligible.Add(Rank(gun.Position, target));
                }
                foreach (Ranked candidate in Order(eligible))
                {
                    if (verb.CanHitTargetFrom(gun.Position, candidate.Thing))
                        return new(candidate.Thing, ArtilleryTargetReason.Accepted, true, operatorPawn != null);
                    reason = ArtilleryTargetReason.CeRejected;
                    ceRejected = true;
                }
            }
            return new(LocalTargetInfo.Invalid, ceRejected ? ArtilleryTargetReason.CeRejected : reason,
                ceRejected, ceRejected && operatorPawn != null);
        }

        private static IOrderedEnumerable<Ranked> Order(IEnumerable<Ranked> candidates) => candidates.OrderBy(c => c.Band)
            .ThenByDescending(c => c.Shape.Door).ThenByDescending(c => c.Shape.Corner)
            .ThenByDescending(c => c.Normal).ThenByDescending(c => c.Shape.OnRoute)
            .ThenBy(c => c.Distance).ThenBy(c => c.Thing.thingIDNumber);

        // Match the native maximum-range metric without a line-of-fire query.
        // On one floor CE uses the nearest occupied cell; between floors our
        // existing Verb.OutOfRange adapter uses Iso's RangeDistance instead.
        internal static float MaximumRangeDistance(Map map, IntVec3 source, LocalTargetInfo target)
        {
            if (IsoFloor.HasFloors(map) && IsoFloor.FloorOf(source, map) != IsoFloor.FloorOf(target.Cell, map))
                return IsoFloor.RangeDistance(source, target.Cell, map);
            CellRect occupied = target.HasThing ? target.Thing.OccupiedRect() : CellRect.SingleCell(target.Cell);
            return Mathf.Sqrt(occupied.ClosestDistSquaredTo(source));
        }

        // Search-window margins do not belong in final geometry acceptance.
        // Only the minimum-range filter stays conservative for CE's cover exception.
        internal ArtilleryTargetReason Filter(Building_TurretGunCE gun, Thing target, float range, bool fallback, float reserve = 0f)
        {
            if (!Valid(target) || !gun.HostileTo(target)) return ArtilleryTargetReason.InvalidTarget;
            Verb verb = gun.AttackVerb;
            float allowance = Math.Max(target.def.Size.x, target.def.Size.z) + 2f;
            float distance = MaximumRangeDistance(Map, gun.Position, target);
            if (distance > range) return ArtilleryTargetReason.OutsideRange;
            // Deployment policy only. Firing queries keep reserve=0 and CE's
            // complete weapon range; nothing changes the real verb or ammo.
            if (reserve > 0f && distance > range - reserve) return ArtilleryTargetReason.InsufficientDeploymentReserve;
            float minimum = verb.verbProps.EffectiveMinRange(target, gun);
            if (verb.verbProps is VerbPropertiesCE props && props.minRangeInCover >= 0f)
                minimum = Math.Min(minimum, props.minRangeInCover);
            float minimumDistance = floors ? IsoFloor.Distance3D(gun.Position, target.Position, Map) : distance;
            if (minimumDistance + allowance < minimum) return ArtilleryTargetReason.BelowMinimumRange;
            if (!(gun.CompFireArc?.WithinFireArc(target) ?? true)) return ArtilleryTargetReason.OutsideArc;
            if (fallback)
            {
                Pawn operatorPawn = CeArtillerySight.OperatorFor(gun);
                IAttackTargetSearcher searcher = operatorPawn != null ? operatorPawn : gun;
                if (target is IAttackTarget attackTarget && attackTarget.ThreatDisabled(searcher))
                    return ArtilleryTargetReason.DisabledThreat;
                // Prototype turrets have not run SpawnSetup, so CE's cached MannableComp is still null.
                bool mannable = gun.TryGetComp<CompMannable>() != null;
                if (target is Pawn pawn && (mannable && pawn.RaceProps.Animal ||
                    !mannable && GenAI.MachinesLike(gun.Faction, pawn)))
                    return ArtilleryTargetReason.DisabledThreat;
                if (verb.IsIncendiary_Ranged() && target.IsBurning()) return ArtilleryTargetReason.DisabledThreat;
            }
            return ArtilleryTargetReason.GeometryPassed;
        }

        private Ranked Rank(IntVec3 source, Thing target)
        {
            IntVec3 from = floors ? IsoFloor.GroundCellOf(source, Map) : source;
            IntVec3 to = floors ? IsoFloor.GroundCellOf(target.Position, Map) : target.Position;
            IntVec3 goal = floors ? IsoFloor.GroundCellOf(Goal, Map) : Goal;
            Vector2 direction = new Vector2(goal.x - from.x, goal.z - from.z).normalized;
            var offset = new Vector2(to.x - from.x, to.z - from.z);
            float distance = offset.magnitude;
            float lateral = Math.Abs(offset.x * direction.y - offset.y * direction.x);
            // Behind the gun is less useful than a wall in the breach direction.
            float score = distance + lateral + Math.Max(0f, -Vector2.Dot(offset, direction));
            shapes.TryGetValue(target, out WallShape shape);
            float normal = shape.Horizontal && !shape.Vertical ? Math.Abs(direction.y)
                : shape.Vertical && !shape.Horizontal ? Math.Abs(direction.x) : 0f;
            return new Ranked(target, Mathf.FloorToInt(score / 3f), shape, normal, distance);
        }
    }
}
