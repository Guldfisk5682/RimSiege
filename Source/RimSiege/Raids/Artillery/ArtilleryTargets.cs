using System;
using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using RimSiege.Combat.Aiming;
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
        internal const int SearchInterval = 25;
        internal readonly Map Map;
        internal readonly IntVec3 Goal;
        internal readonly List<Thing> Walls = new();
        internal readonly List<Thing> Fallback = new();
        private readonly Dictionary<Thing, WallShape> shapes = new();
        private readonly bool floors;

        private readonly record struct WallShape(bool Door, bool Corner, bool Horizontal, bool Vertical, bool OnRoute);
        private readonly record struct Ranked(Thing Thing, int Band, WallShape Shape, float Normal, float Distance);

        internal ArtilleryTargets(Map map, IntVec3 goal, SupportRoute route)
        {
            Map = map; Goal = goal; floors = IsoFloor.HasFloors(map);
            var combat = new HashSet<Building>(map.listerBuildings.allBuildingsColonistCombatTargets);
            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                if (!Valid(building)) continue;
                if ((building.def.IsWall || building.def.IsDoor) && BreachingUtility.ShouldBreachBuilding(building))
                    Walls.Add(building);
                else if (combat.Contains(building)) Fallback.Add(building);
            }
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
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
            List<Thing> deploymentTargets = null, float reserve = 0f) =>
            CeArtillerySight.WithGun(Map, faction, placement,
                gun => Search(gun, false, effectiveRange, wallsOnly: false, deploymentTargets, reserve));

        internal ArtilleryTargetResult Find(Building_TurretGunCE gun) => Search(gun, true, null, wallsOnly: false);

        internal ArtilleryTargetResult FindWalls(Building_TurretGunCE gun) => Search(gun, true, null, wallsOnly: true);

        // At most two representative wall segments per piece and layout round.
        // This does not invoke CE's full CanHitTargetFrom construction gate.
        internal FixedCorridorResult CheckFixedCorridor(Faction faction, EmplacementSpec placement, float? effectiveRange,
            List<Thing> deploymentTargets = null, float reserve = 0f) =>
            CeArtillerySight.WithGun(Map, faction, placement, gun =>
            {
                float range = effectiveRange ?? ((Verb_LaunchProjectileCE)gun.AttackVerb).EffectiveRange;
                foreach (List<Thing> group in deploymentTargets == null ? new[] { Walls, Fallback } : new[] { deploymentTargets })
                {
                    var ranked = Order(group.Where(t => Filter(gun, t, range, !shapes.ContainsKey(t), reserve) == ArtilleryTargetReason.GeometryPassed)
                        .Select(t => Rank(gun.Position, t))).ToList();
                    if (ranked.Count == 0) continue;
                    Thing first = ranked[0].Thing;
                    FixedCorridorResult firstResult = FixedShotCorridor.Trace(gun, first);
                    if (firstResult.Clear) return firstResult;
                    Thing second = ranked.Skip(1).Select(c => c.Thing).FirstOrDefault(t =>
                        (t.Position - first.Position).LengthHorizontalSquared >= 16);
                    if (second != null)
                    {
                        FixedCorridorResult secondResult = FixedShotCorridor.Trace(gun, second);
                        if (secondResult.Clear) return secondResult;
                    }
                    return firstResult;
                }
                return new FixedCorridorResult(FixedObstruction.Unavailable, IntVec3.Invalid);
            });

        private ArtilleryTargetResult Search(Building_TurretGunCE gun, bool precise, float? effectiveRange, bool wallsOnly,
            List<Thing> deploymentTargets = null, float reserve = 0f)
        {
            if (gun.AttackVerb is not Verb_LaunchProjectileCE verb)
                return new ArtilleryTargetResult(LocalTargetInfo.Invalid, ArtilleryTargetReason.UnavailableWeapon, false, false);
            float range = effectiveRange ?? verb.EffectiveRange;
            Pawn operatorPawn = CeArtillerySight.OperatorFor(gun);
            ArtilleryTargetReason reason = ArtilleryTargetReason.NoCandidates;
            bool ceRejected = false;
            List<Thing>[] groups = deploymentTargets != null ? new[] { deploymentTargets }
                : wallsOnly ? new[] { Walls } : new[] { Walls, Fallback };
            foreach (List<Thing> group in groups)
            {
                var eligible = new List<Ranked>();
                foreach (Thing target in group)
                {
                    bool fallback = deploymentTargets != null ? !shapes.ContainsKey(target) : !wallsOnly && group == Fallback;
                    ArtilleryTargetReason rejected = Filter(gun, target, range, fallback, reserve);
                    if (rejected != ArtilleryTargetReason.GeometryPassed) { reason = rejected; continue; }
                    // Search only asks whether an inexpensive candidate exists; no ray or sort is needed yet.
                    if (!precise)
                        return new ArtilleryTargetResult(target, ArtilleryTargetReason.GeometryPassed, false, false);
                    eligible.Add(Rank(gun.Position, target));
                }
                foreach (Ranked candidate in Order(eligible))
                {
                    // Preserve virtual dispatch, including CE weapon subclasses' operator checks.
                    if (verb.CanHitTargetFrom(gun.Position, candidate.Thing))
                        return new ArtilleryTargetResult(candidate.Thing,
                            ArtilleryTargetReason.Accepted, true, operatorPawn != null);
                    reason = ArtilleryTargetReason.CeRejected;
                    ceRejected = true;
                }
            }
            return new ArtilleryTargetResult(LocalTargetInfo.Invalid,
                ceRejected ? ArtilleryTargetReason.CeRejected : reason, ceRejected, ceRejected && operatorPawn != null);
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
