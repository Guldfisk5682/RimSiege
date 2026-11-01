using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimSiege.Raids
{
    // The native group maker still owns the available kinds, xenotypes, costs,
    // weights and the required first sapper. These are potential loadouts:
    // actual equipment and ammunition are generated afterwards by the game/CE.
    [HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.GetOptions))]
    internal static class SapperCompositionPatch
    {
        private static readonly Dictionary<(PawnKindDef, XenotypeDef, bool), bool> Potential = new();

        private static bool ProjectileCanDemolish(VerbProperties verb, ThingDef projectile, ThingDef weapon, ThingDef stuff)
        {
            ProjectileProperties properties = projectile?.projectile;
            DamageDef damage = properties?.damageDef;
            return properties != null && damage != null && !verb.ai_BeamIsIncendiary && !properties.ai_IsIncendiary &&
                (verb.ai_IsBuildingDestroyer || properties.explosionRadius > 0f) && damage.harmsHealth &&
                properties.GetDamageAmount(weapon, stuff) * damage.buildingDamageFactor * damage.buildingDamageFactorImpassable > 0f;
        }

        internal static bool WeaponCanDemolish(ThingStuffPair pair)
        {
            ThingDef weapon = pair.thing;
            if (!weapon.IsRangedWeapon || weapon.Verbs.NullOrEmpty() || pair.Commonality <= 0f || pair.Price <= 0f || weapon.generateAllowChance <= 0f)
                return false;
            VerbProperties verb = weapon.Verbs[0];
            if (verb.IsMeleeAttack) return false;
            CompProperties_AmmoUser ammo = weapon.GetCompProperties<CompProperties_AmmoUser>();
            if (ammo?.ammoSet != null && AmmoUtility.IsAmmoSystemActive(ammo.ammoSet))
                return ammo.ammoSet.ammoTypes.Any(link => link.ammo != null && link.ammo.generateAllowChance > 0f &&
                    ProjectileCanDemolish(verb, link.projectile, weapon, pair.stuff));
            return ProjectileCanDemolish(verb, verb.defaultProjectile, weapon, pair.stuff);
        }

        private static bool KindCanDemolish(PawnGenOptionWithXenotype option)
        {
            PawnKindDef kind = option.Option.kind;
            if (!kind.RaceProps.ToolUser || PawnWeaponGenerator.AllWeaponPairs == null) return false;
            var key = (kind, option.Xenotype, Controller.settings.EnableAmmoSystem);
            if (Potential.TryGetValue(key, out bool result)) return result;
            result = !kind.weaponTags.NullOrEmpty() && PawnWeaponGenerator.AllWeaponPairs.Any(pair => pair.Price <= kind.weaponMoney.max &&
                (kind.weaponStuffOverride == null || pair.stuff == kind.weaponStuffOverride) &&
                (pair.stuff == null || pair.stuff.stuffProps.allowedInStuffGeneration) &&
                (option.Xenotype?.forbiddenWeaponClasses.NullOrEmpty() != false || pair.thing.weaponClasses.NullOrEmpty() ||
                    !pair.thing.weaponClasses.Any(option.Xenotype.forbiddenWeaponClasses.Contains)) &&
                pair.thing.weaponTags.Any(kind.weaponTags.Contains) && WeaponCanDemolish(pair));
            Potential[key] = result;
            return result;
        }

        private static void Postfix(PawnGroupMakerParms groupParms, List<PawnGenOptionWithXenotype> chosenOptions,
            ref List<PawnGenOptionWithXenotype> __result)
        {
            if (!SapperGeneration.Applies(groupParms) ||
                chosenOptions == null || __result.Count == 0) return;

            int wanted = (2 * (chosenOptions.Count + 1) + 4) / 5;
            int selected = chosenOptions.Count(KindCanDemolish);
            bool needDemolition = selected < wanted;
            if (!__result.Any(option => option.SelectionWeight > 0f && KindCanDemolish(option) == needDemolition)) return;
            __result.RemoveAll(option => KindCanDemolish(option) != needDemolition);
        }
    }

    internal static class SapperGeneration
    {
        internal sealed class Context
        {
            internal IncidentParms Parms;
            internal int GroupDepth, PawnDepth, Total, Demolition;
        }

        [ThreadStatic] internal static Context Current;

        internal static bool Applies(PawnGroupMakerParms group) => Current != null && SiegeRaidMap.Enabled &&
            group?.faction != null && group.faction == Current.Parms.faction &&
            group.raidStrategy == Current.Parms.raidStrategy &&
            group.raidStrategy?.Worker is RaidStrategyWorker_ImmediateAttackSappers;

        internal static bool ChooseWeapon(IEnumerable<ThingStuffPair> source, Func<ThingStuffPair, float> weight,
            out ThingStuffPair result, Pawn pawn)
        {
            Context context = Current;
            if (context != null && context.GroupDepth == 1 && context.PawnDepth == 1 && pawn.Faction == context.Parms.faction &&
                context.Demolition < (2 * (context.Total + 1) + 4) / 5)
            {
                List<ThingStuffPair> demolition = source.Where(pair => SapperCompositionPatch.WeaponCanDemolish(pair) && weight(pair) > 0f).ToList();
                if (demolition.Count != 0) return demolition.TryRandomElementByWeight(weight, out result);
            }
            return source.TryRandomElementByWeight(weight, out result);
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo))]
    internal static class SapperIncidentScope
    {
        private static void Prefix(IncidentParms parms, out SapperGeneration.Context __state)
        {
            __state = SapperGeneration.Current;
            SapperGeneration.Current = SiegeRaidMap.Enabled ? new SapperGeneration.Context { Parms = parms } : null;
        }

        private static void Finalizer(SapperGeneration.Context __state) => SapperGeneration.Current = __state;
    }

    [HarmonyPatch(typeof(PawnGroupKindWorker_Normal), "GeneratePawns")]
    internal static class SapperGroupScope
    {
        private static void Prefix(PawnGroupMakerParms parms, out SapperGeneration.Context __state)
        {
            __state = SapperGeneration.Applies(parms) ? SapperGeneration.Current : null;
            if (__state != null) __state.GroupDepth++;
        }

        private static void Finalizer(SapperGeneration.Context __state)
        {
            if (__state != null) __state.GroupDepth--;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    internal static class SapperPawnScope
    {
        private static void Prefix(out SapperGeneration.Context __state)
        {
            __state = SapperGeneration.Current;
            if (__state?.GroupDepth > 0) __state.PawnDepth++;
            else __state = null;
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PawnGenerationRequest request, Pawn __result, SapperGeneration.Context __state)
        {
            if (__state == null || __state.GroupDepth != 1 || __state.PawnDepth != 1 || __result == null ||
                request.Faction != __state.Parms.faction || __result.Faction != __state.Parms.faction) return;
            __state.Total++;
            if (SiegeRoles.HasDemolitionWeapon(__result)) __state.Demolition++;
        }

        private static void Finalizer(SapperGeneration.Context __state)
        {
            if (__state != null) __state.PawnDepth--;
        }
    }

    [HarmonyPatch(typeof(PawnWeaponGenerator), nameof(PawnWeaponGenerator.TryGenerateWeaponFor))]
    internal static class SapperWeaponChoice
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction code in instructions)
            {
                if (code.operand is MethodInfo method && method.Name == nameof(GenCollection.TryRandomElementByWeight) &&
                    method.IsGenericMethod && method.GetGenericArguments()[0] == typeof(ThingStuffPair))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code).MoveBlocksFrom(code);
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(SapperGeneration), nameof(SapperGeneration.ChooseWeapon));
                }
                yield return code;
            }
        }
    }
}
