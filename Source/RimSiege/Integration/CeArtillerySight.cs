using System.Linq;
using CombatExtended;
using IsoRimCEPackZ;
using RimWorld;
using Verse;

namespace RimSiege.Integration
{
    internal readonly record struct EmplacementSpec(ThingDef Def, ThingDef Stuff, ThingDef Ammo,
        IntVec3 Cell, Rot4 Rotation, Pawn Operator);

    // Artillery owns selection policy; CEPack owns native prototype state and all geometry queries.
    internal static class CeArtillerySight
    {
        internal static GeometryStatus Open(Map map, Faction faction, EmplacementSpec placement, out IPlacementQuery query) =>
            Api.OpenPlacement(new(map, placement.Def, placement.Stuff, placement.Ammo, placement.Cell,
                placement.Rotation, faction, placement.Operator), out query);

        internal static float EffectiveRange(Map map, Faction faction, EmplacementSpec placement)
        {
            if (Open(map, faction, placement, out var query) != GeometryStatus.Success) return 0f;
            using (query) return query.ReadWeapon(out var weapon) == GeometryStatus.Success ? weapon.EffectiveRange : 0f;
        }
        internal static Pawn OperatorFor(Thing thing) => CE_Utility.TryGetTurretOperator(thing);

        // Profiles have definitions before a map candidate exists. Read CE's own ammo configuration for classification.
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
    }
}
