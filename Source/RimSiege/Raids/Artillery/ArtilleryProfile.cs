using System.Collections.Generic;
using CombatExtended;
using RimSiege.Integration;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSiege.Raids.Artillery
{
    public sealed class ArtilleryProfileDef : Def
    {
        public string kind;
        public float personnelFactor = 1f;
        public float equipmentFraction = 0.3f;
        public SimpleCurve totalPieces = new SimpleCurve();
        public List<ArtilleryGunRule> guns = new List<ArtilleryGunRule>();

        internal List<ArtilleryPlacement> Configure(float raidPoints, float pawnPoints, Faction faction)
        {
            var result = new List<ArtilleryPlacement>();
            var used = new Dictionary<ArtilleryGunRule, int>();
            float remaining = pawnPoints * equipmentFraction;
            int limit = Mathf.FloorToInt(totalPieces.Evaluate(raidPoints));
            while (result.Count < limit)
            {
                var choices = new List<ArtilleryGunRule>();
                foreach (ArtilleryGunRule rule in guns)
                {
                    used.TryGetValue(rule, out int count);
                    if (rule.turret != null && rule.turret.techLevel <= faction.def.techLevel &&
                        rule.cost <= remaining && count < Mathf.FloorToInt(rule.count.Evaluate(raidPoints)))
                        choices.Add(rule);
                }
                if (choices.Count == 0) break;
                ArtilleryGunRule selected = choices.RandomElementByWeight(g => g.weight);
                ThingDef ammo = selected.ammo ?? SelectAmmo(selected.turret, faction, selected.defaultAmmo);
                CompProperties_AmmoUser ammoProps = selected.turret.building.turretGunDef.GetCompProperties<CompProperties_AmmoUser>();
                if (ammo == null && ammoProps?.ammoSet != null && AmmoUtility.IsAmmoSystemActive(ammoProps.ammoSet))
                {
                    // No usable ammunition: remove this rule from this selection, then
                    // try another gun instead of accepting an unsupplied emplacement.
                    used[selected] = Mathf.FloorToInt(selected.count.Evaluate(raidPoints));
                    continue;
                }
                result.Add(new ArtilleryPlacement
                {
                    Def = selected.turret,
                    Stuff = selected.turret.MadeFromStuff ? ThingDefOf.Steel : null,
                    Ammo = ammo
                });
                used.TryGetValue(selected, out int previous);
                used[selected] = previous + 1;
                remaining -= selected.cost;
            }
            return result;
        }

        internal static ThingDef SelectAmmo(ThingDef turret, Faction faction, bool defaultAmmo)
        {
            if (defaultAmmo)
            {
                CompProperties_AmmoUser props = turret.building.turretGunDef.GetCompProperties<CompProperties_AmmoUser>();
                ThingDef projectile = turret.building.turretGunDef.Verbs[0].defaultProjectile;
                foreach (AmmoLink link in props.ammoSet.ammoTypes)
                    if (link.projectile == projectile) return link.ammo;
                return null;
            }
            CompProperties_AmmoUser ammo = turret.building.turretGunDef.GetCompProperties<CompProperties_AmmoUser>();
            if (ammo != null && !TurretGunUtility.NeedsShells(turret)) return CeSiegeAmmo.Select(ammo, faction);
            return TurretGunUtility.TryFindRandomShellDef(turret, allowEMP: false,
                allowToxGas: ModsConfig.BiotechActive && faction.def == FactionDefOf.PirateWaster,
                mustHarmHealth: true, techLevel: faction.def.techLevel,
                allowAntigrainWarhead: false, faction: faction);
        }
    }

    public sealed class ArtilleryGunRule
    {
        public ThingDef turret;
        // Explicit ammunition overrides defaultAmmo and the siege ammo roll.
        public ThingDef ammo;
        // Configuration units use the vanilla mortar's 60-point basis, not market silver.
        public float cost = 60f;
        public float weight = 1f;
        public bool defaultAmmo;
        public SimpleCurve count = new SimpleCurve();
    }
}
