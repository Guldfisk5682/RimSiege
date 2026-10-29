using CombatExtended;
using HarmonyLib;
using RimSiege.Raids.Artillery;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSiege.Raids
{
    internal static class SiegeRoles
    {
        private static readonly AccessTools.FieldRef<LordJob_AssaultColony, bool> Sappers =
            AccessTools.FieldRefAccess<LordJob_AssaultColony, bool>("sappers");
        private static readonly AccessTools.FieldRef<LordJob_AssaultColony, bool> Breachers =
            AccessTools.FieldRefAccess<LordJob_AssaultColony, bool>("breachers");

        internal static bool Ordinary(Lord lord)
        {
            if (lord?.LordJob is LordJob_Artillery artillery) return artillery.IsNativeAssault;
            return lord?.LordJob is LordJob_AssaultColony assault && !Sappers(assault) && !Breachers(assault);
        }

        internal static bool Eligible(Pawn pawn, SiegeParty party) => pawn.Spawned && !pawn.Dead && !pawn.Downed &&
            ProjectZ.RaidClimb.Climber(pawn) && Ordinary(pawn.GetLord()) &&
            ProjectZ.RaidClimb.MayClimb(pawn, false) &&
            !party.Operators.Contains(pawn) && !pawn.Map.GetComponent<ArtilleryMap>().IsOperator(pawn) &&
            pawn.CurJobDef != JobDefOf.ManTurret && !Demolition(pawn);

        private static bool Demolition(Pawn pawn)
        {
            if (pawn.mindState?.duty?.def == DutyDefOf.Breaching || pawn.mindState?.duty?.def == DutyDefOf.Sapper)
                return true;
            return HasDemolitionWeapon(pawn);
        }

        internal static bool HasDemolitionWeapon(Pawn pawn)
        {
            if (pawn.equipment?.Primary != null && DemolitionWeapon(pawn.equipment.Primary)) return true;
            CompInventory inventory = pawn.TryGetComp<CompInventory>();
            if (inventory != null)
                foreach (ThingWithComps weapon in inventory.weapons)
                    if (DemolitionWeapon(weapon)) return true;
            return false;
        }

        internal static bool DemolitionWeapon(ThingWithComps weapon)
        {
            Verb verb = weapon.TryGetComp<CompEquippable>()?.PrimaryVerb;
            if (verb == null || verb.IsMeleeAttack || verb.IsIncendiary_Ranged()) return false;
            CompAmmoUser ammo = weapon.TryGetComp<CompAmmoUser>();
            if (ammo != null && ammo.UseAmmo && !ammo.HasAmmoOrMagazine) return false;
            ThingDef projectile = ammo?.CurAmmoProjectile ?? (verb is Verb_LaunchProjectileCE ce ? ce.Projectile : verb.GetProjectile());
            ProjectileProperties props = projectile?.projectile;
            return props != null && (verb.verbProps.ai_IsBuildingDestroyer || props.explosionRadius > 0f) && props.damageDef.harmsHealth &&
                props.GetDamageAmount(weapon) * props.damageDef.buildingDamageFactor * props.damageDef.buildingDamageFactorImpassable > 0f;
        }
    }
}
