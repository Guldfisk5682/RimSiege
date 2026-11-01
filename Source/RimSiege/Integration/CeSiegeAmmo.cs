using System.Linq;
using CombatExtended;
using RimWorld;
using Verse;

namespace RimSiege.Integration
{
    // CE's siege selector is gated by vanilla NeedsShells, which excludes
    // CompAmmoUser turrets such as the flak cannon. Apply the same CE ammo-set
    // predicates and weights for those turrets, without changing their defs.
    internal static class CeSiegeAmmo
    {
        internal static ThingDef Select(CompProperties_AmmoUser props, Faction faction)
        {
            if (props?.ammoSet?.ammoTypes == null) return null;
            bool allowToxGas = ModsConfig.BiotechActive && faction.def == FactionDefOf.PirateWaster;
            return props.ammoSet.ammoTypes
                .Where(link => link.ammo.spawnAsSiegeAmmo)
                .Where(link =>
                {
                    DamageDef explosive = link.projectile.GetCompProperties<CompProperties_ExplosiveCE>()?.explosiveDamageType ??
                        link.projectile.GetCompProperties<CompProperties_Explosive>()?.explosiveDamageType;
                    DamageDef direct = link.projectile.projectile.damageDef;
                    return (explosive != null || direct != null) && explosive != DamageDefOf.EMP && direct != DamageDefOf.EMP &&
                        (allowToxGas || !ModsConfig.BiotechActive || (explosive != DamageDefOf.ToxGas && direct != DamageDefOf.ToxGas)) &&
                        link.ammo != ThingDefOf.Shell_AntigrainWarhead && link.ammo.techLevel <= faction.def.techLevel;
                })
                .Select(link => link.ammo)
                .TryRandomElementByWeight(ammo => ammo.generateAllowChance, out AmmoDef result) ? result : null;
        }
    }
}
