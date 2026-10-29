using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSiege.Integration
{
    internal readonly struct EmplacementSpec
    {
        internal readonly ThingDef Def, Stuff, Ammo;
        internal readonly IntVec3 Cell;
        internal readonly Rot4 Rotation;
        internal readonly Pawn Operator;

        internal EmplacementSpec(ThingDef def, ThingDef stuff, ThingDef ammo, IntVec3 cell, Rot4 rotation, Pawn @operator)
        {
            Def = def; Stuff = stuff; Ammo = ammo; Cell = cell; Rotation = rotation; Operator = @operator;
        }
    }

    // The query uses real CE verbs on unspawned, per-map cannon prototypes.
    internal static class CeArtillerySight
    {
        // Match CE's actual firing verb and selected projectile, including mortar subclasses.
        internal static bool IsDirectFire(ThingDef turret, ThingDef ammo)
        {
            if (turret?.building == null || turret.building.IsMortar) return false;
            ThingDef weapon = turret.building.turretGunDef;
            VerbProperties verb = weapon?.Verbs?.FirstOrDefault();
            if (verb?.verbClass == null || !typeof(Verb_LaunchProjectileCE).IsAssignableFrom(verb.verbClass) ||
                typeof(Verb_ShootMortarCE).IsAssignableFrom(verb.verbClass)) return false;
            CompProperties_AmmoUser props = weapon.GetCompProperties<CompProperties_AmmoUser>();
            ThingDef projectile = verb.defaultProjectile;
            if (props?.ammoSet != null && AmmoUtility.IsAmmoSystemActive(props.ammoSet))
            {
                ThingDef selected = ammo ?? props.ammoSet.ammoTypes?.FirstOrDefault()?.ammo;
                projectile = props.ammoSet.ammoTypes?.FirstOrDefault(link => link.ammo == selected)?.projectile ?? projectile;
            }
            return projectile?.projectile != null && !projectile.projectile.flyOverhead;
        }

        internal static float MaximumRange(ThingDef turret) =>
            turret?.building?.turretGunDef?.Verbs?.FirstOrDefault()?.range ?? 0f;

        // The same scoped CE verb used for a candidate sight query supplies its
        // current maximum range, including weapon stats, ammunition and weather.
        internal static float EffectiveRange(Map map, Faction faction, EmplacementSpec placement)
        {
            Building_TurretGunCE gun = Prototype(map, placement);
            if (gun == null || gun.AttackVerb is not Verb_LaunchProjectileCE verb) return 0f;
            IntVec3 position = gun.Position;
            Rot4 rotation = gun.Rotation;
            Faction previousFaction = gun.Faction;
            CompAmmoUser ammo = gun.CompAmmo;
            bool useAmmo = ammo?.UseAmmo == true;
            AmmoDef previousSelected = useAmmo ? ammo.SelectedAmmo : null;
            AmmoDef previousCurrent = useAmmo ? ammo.CurrentAmmo : null;
            Scope previous = Current;
            try
            {
                PreparePrototype(map, faction, placement, true);
                Current = new Scope { Map = map, Gun = gun, Operator = placement.Operator,
                    Obstacles = EmptyObstacles, VirtualThings = EmptyVirtualThings };
                return verb.EffectiveRange;
            }
            finally
            {
                Current = previous;
                if (useAmmo) { ammo.SelectedAmmo = previousSelected; ammo.CurrentAmmo = previousCurrent; }
                gun.Position = position;
                gun.Rotation = rotation;
                FactionOf(gun) = previousFaction;
            }
        }

        internal sealed class Scope
        {
            internal Map Map;
            internal Building_TurretGunCE Gun;
            internal Pawn Operator;
            internal Dictionary<IntVec3, List<Thing>> Obstacles;
            internal HashSet<Thing> VirtualThings;
            internal Thing Overlay;
        }

        private sealed class MapPrototypes
        {
            internal readonly Dictionary<(ThingDef, ThingDef), Building_TurretGunCE> Guns = new();
            internal readonly Dictionary<Building_TurretGunCE, AmmoDef> DefaultAmmo = new();
            internal readonly Dictionary<(ThingDef, ThingDef), List<Thing>> Obstacles = new();
        }

        [ThreadStatic] internal static Scope Current;
        private static readonly ConditionalWeakTable<Map, MapPrototypes> Prototypes = new();
        private static readonly AccessTools.FieldRef<Thing, Faction> FactionOf = AccessTools.FieldRefAccess<Thing, Faction>("factionInt");
        private static readonly Dictionary<IntVec3, List<Thing>> EmptyObstacles = new();
        private static readonly HashSet<Thing> EmptyVirtualThings = new();

        internal static Map MapFor(Thing thing) => thing != null && Current != null &&
            (thing == Current.Gun || Current.VirtualThings.Contains(thing)) ? Current.Map : thing?.Map;

        internal static Map MapHeldFor(Thing thing) => Current != null && thing == Current.Gun ? Current.Map : thing?.MapHeld;

        internal static Pawn OperatorFor(Thing thing) => Current != null && thing == Current.Gun
            ? Current.Operator : CE_Utility.TryGetTurretOperator(thing);

        private static Building_TurretGunCE Prototype(Map map, EmplacementSpec placement)
        {
            var cache = Prototypes.GetValue(map, _ => new MapPrototypes()).Guns;
            var key = (placement.Def, placement.Stuff);
            if (!cache.TryGetValue(key, out Building_TurretGunCE gun))
            {
                gun = ThingMaker.MakeThing(placement.Def, placement.Stuff) as Building_TurretGunCE;
                cache.Add(key, gun);
                if (gun?.CompFireArc is CompFireArc arc && arc.CurrentSpan < 0f) arc.CurrentSpan = arc.Props.spanRange.max;
                if (gun != null) Prototypes.GetValue(map, _ => new MapPrototypes()).DefaultAmmo[gun] = gun.CompAmmo?.CurrentAmmo;
            }
            return gun;
        }

        private static Building_TurretGunCE PreparePrototype(Map map, Faction faction, EmplacementSpec placement, bool rangeOnly)
        {
            Building_TurretGunCE gun = Prototype(map, placement);
            if (gun == null) return null;
            gun.Position = placement.Cell;
            gun.Rotation = placement.Rotation;
            FactionOf(gun) = placement.Operator?.Faction ?? faction;
            if (gun.CompAmmo != null && (!rangeOnly || gun.CompAmmo.UseAmmo))
            {
                AmmoDef ammo = placement.Ammo as AmmoDef ?? Prototypes.GetValue(map, _ => new MapPrototypes()).DefaultAmmo[gun];
                gun.CompAmmo.SelectedAmmo = ammo;
                gun.CompAmmo.CurrentAmmo = ammo;
            }
            return gun;
        }

        private static Dictionary<IntVec3, List<Thing>> Planned(Map map, Faction faction, IEnumerable<EmplacementSpec> obstacles)
        {
            var cells = new Dictionary<IntVec3, List<Thing>>();
            var cache = Prototypes.GetValue(map, _ => new MapPrototypes()).Obstacles;
            var used = new Dictionary<(ThingDef, ThingDef), int>();
            foreach (EmplacementSpec placement in obstacles)
            {
                if (placement.Def == null || !placement.Cell.IsValid) continue;
                var key = (placement.Def, placement.Stuff);
                if (!cache.TryGetValue(key, out List<Thing> prototypes)) cache[key] = prototypes = new List<Thing>();
                used.TryGetValue(key, out int index);
                if (index == prototypes.Count) prototypes.Add(ThingMaker.MakeThing(placement.Def, placement.Stuff));
                Thing thing = prototypes[index];
                used[key] = index + 1;
                thing.Position = placement.Cell;
                thing.Rotation = placement.Rotation;
                FactionOf(thing) = faction;
                foreach (IntVec3 cell in GenAdj.OccupiedRect(placement.Cell, placement.Rotation, placement.Def.Size))
                {
                    if (!cells.TryGetValue(cell, out List<Thing> list)) cells[cell] = list = new List<Thing>();
                    list.Add(thing);
                }
            }
            return cells;
        }

        // A search prepares its overlay once, then uses the same CE verb for all candidates.
        internal static T WithGun<T>(Map map, Faction faction, EmplacementSpec placement,
            IEnumerable<EmplacementSpec> plannedObstacles, Func<Building_TurretGunCE, T> query)
        {
            Building_TurretGunCE gun = PreparePrototype(map, faction, placement, false);
            if (gun == null) return default;
            Dictionary<IntVec3, List<Thing>> obstacles = plannedObstacles == null
                ? EmptyObstacles : Planned(map, faction, plannedObstacles);
            Scope previous = Current;
            Current = new Scope { Map = map, Gun = gun, Operator = placement.Operator, Obstacles = obstacles,
                VirtualThings = obstacles.Count == 0 ? EmptyVirtualThings : new HashSet<Thing>(obstacles.Values.SelectMany(x => x)) };
            try { return query(gun); }
            finally { Current = previous; }
        }

        // Called only while CE's original cell predicate is evaluating a plan overlay.
        internal static Pawn FirstPawn(IntVec3 cell, Map map) => Current?.Overlay != null ? null : cell.GetFirstPawn(map);
        internal static Thing Cover(IntVec3 cell, Map map) => Current?.Overlay ?? cell.GetCover(map);

        // CE's target-cover scan selects the tallest partial cover at each cell.
        internal static Thing TargetCover(IntVec3 cell, Map map)
        {
            Thing result = cell.GetCover(map);
            if (Current == null || !Current.Obstacles.TryGetValue(cell, out List<Thing> planned)) return result;
            float height = result == null ? 0f : new CollisionVertical(result).Max;
            foreach (Thing thing in planned)
            {
                if (thing.def.Fillage != FillCategory.Partial || thing is Building_TrapExplosive) continue;
                float plannedHeight = new CollisionVertical(thing).Max;
                if (result == null || plannedHeight > height) { result = thing; height = plannedHeight; }
            }
            return result;
        }
    }
}
