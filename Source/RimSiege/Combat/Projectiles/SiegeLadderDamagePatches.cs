using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimSiege.Integration;
using RimWorld;
using Verse;

namespace RimSiege.Combat.Projectiles
{
    internal static class SiegeLadderDamage
    {
        internal const float BulletFactor = 0.1f;
        internal const float MeleeFactor = 1.0f;
        [ThreadStatic] internal static (Thing target, Thing attacker, float factor) Current;
    }

    [HarmonyPatch]
    internal static class SiegeLadderMeleeDamageScopePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            CeAccess.Methods(typeof(Verb_MeleeAttack), "ApplyMeleeDamageToTarget");

        private static void Prefix(Verb_MeleeAttack __instance, LocalTargetInfo target,
            out (Thing target, Thing attacker, float factor) __state)
        {
            __state = SiegeLadderDamage.Current;
            SiegeLadderDamage.Current = IsoSiegeLadders.Is(target.Thing)
                ? (target.Thing, __instance.Caster, SiegeLadderDamage.MeleeFactor) : default;
        }

        private static void Finalizer((Thing target, Thing attacker, float factor) __state) => SiegeLadderDamage.Current = __state;
    }

    [HarmonyPatch]
    internal static class SiegeLadderBulletDamageScopePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => CeAccess.Methods(typeof(BulletCE), nameof(BulletCE.Impact))
            .Where(m => m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(Thing));

        private static void Prefix(BulletCE __instance, Thing __0,
            out (Thing target, Thing attacker, float factor) __state)
        {
            __state = SiegeLadderDamage.Current;
            SiegeLadderDamage.Current = IsoSiegeLadders.Is(__0)
                ? (__0, __instance.launcher, SiegeLadderDamage.BulletFactor) : default;
        }

        private static void Finalizer((Thing target, Thing attacker, float factor) __state) => SiegeLadderDamage.Current = __state;
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class SiegeLadderDamagePatch
    {
        private static void Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            if (!IsoSiegeLadders.Is(__instance)) return;
            if (!dinfo.Def.harmsHealth || dinfo.Def.isExplosive ||
                (dinfo.Def.armorCategory != DamageArmorCategoryDefOf.Sharp && dinfo.Def.armorCategory != DamageDefOf.Blunt.armorCategory)) return;
            float factor;
            if (SiegeLadderDamage.Current.target == __instance && SiegeLadderDamage.Current.attacker == dinfo.Instigator)
                factor = SiegeLadderDamage.Current.factor;
            else if (dinfo.Def.isRanged && !dinfo.Def.isExplosive && dinfo.Def.armorCategory == DamageArmorCategoryDefOf.Sharp)
                factor = SiegeLadderDamage.BulletFactor;
            else return; // Explosions, fire and environmental damage retain their native rules.
            dinfo.SetAmount(dinfo.Amount * factor);
        }
    }
}
