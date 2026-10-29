using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

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
                Current = new Scope { Map = map, Gun = gun, Operator = placement.Operator };
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
        }

        private sealed class MapPrototypes
        {
            internal readonly Dictionary<(ThingDef, ThingDef), Building_TurretGunCE> Guns = new();
            internal readonly Dictionary<Building_TurretGunCE, AmmoDef> DefaultAmmo = new();
        }

        [ThreadStatic] internal static Scope Current;
        private static readonly ConditionalWeakTable<Map, MapPrototypes> Prototypes = new();
        private static readonly AccessTools.FieldRef<Thing, Faction> FactionOf = AccessTools.FieldRefAccess<Thing, Faction>("factionInt");

        internal static Map MapFor(Thing thing) => thing != null && Current != null &&
            thing == Current.Gun ? Current.Map : thing?.Map;

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

        // Geometry queries share CE's verb, ammunition and fire-arc properties.
        internal static T WithGun<T>(Map map, Faction faction, EmplacementSpec placement,
            Func<Building_TurretGunCE, T> query)
        {
            Building_TurretGunCE gun = PreparePrototype(map, faction, placement, false);
            if (gun == null) return default;
            Scope previous = Current;
            Current = new Scope { Map = map, Gun = gun, Operator = placement.Operator };
            try { return query(gun); }
            finally { Current = previous; }
        }
    }
}
