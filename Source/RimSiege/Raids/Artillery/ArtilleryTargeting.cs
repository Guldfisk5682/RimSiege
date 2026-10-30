using System.Runtime.CompilerServices;
using CombatExtended;
using HarmonyLib;
using RimSiege.Integration;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    // Native CE owns turning, warmup, reload and firing. Both successful and
    // unsuccessful searches are throttled here, without a separate tick loop.
    [HarmonyPatch(typeof(Building_TurretGunCE), nameof(Building_TurretGunCE.TryFindNewTarget))]
    internal static class ArtilleryTargeting
    {
        private sealed class RaidWindow
        {
            internal ArtilleryTargets Candidates;
            internal int NextRefreshTick;
        }

        private sealed class GunSearch
        {
            internal ArtilleryTargets Candidates;
            internal SearchInputs Inputs;
            internal ArtilleryTargetResult Result;
            internal int NextSearchTick;
        }

        // Building rotation fixes the allowed arc; the moving barrel angle is deliberately absent.
        private readonly record struct SearchInputs(ThingDef Ammo, ThingDef SelectedAmmo, Pawn Operator,
            IntVec3 Position, Rot4 BaseRotation, float ArcCenter, float ArcSpan, bool ArcOn, bool ArcSetting);

        private static readonly ConditionalWeakTable<ArtilleryRaid, RaidWindow> Windows = new();
        private static readonly ConditionalWeakTable<Building_TurretGunCE, GunSearch> Searches = new();
        private static readonly AccessTools.FieldRef<CompFireArc, bool> ArcOn =
            AccessTools.FieldRefAccess<CompFireArc, bool>("turnedOn");

        internal static void ForgetGun(Thing gun)
        {
            if (gun is Building_TurretGunCE turret) Searches.Remove(turret);
        }

        internal static void ForgetRaid(ArtilleryRaid raid) => Windows.Remove(raid);

        private static ArtilleryTargets Candidates(ArtilleryRaid raid, Map map, int tick)
        {
            RaidWindow window = Windows.GetValue(raid, static _ => new RaidWindow());
            if (window.Candidates == null || tick >= window.NextRefreshTick || window.Candidates.Goal != raid.Camp.Goal)
            {
                window.Candidates = new ArtilleryTargets(map, raid.Camp.Goal, SupportRoute.For(raid));
                window.NextRefreshTick = tick + ArtilleryTargets.SearchInterval;
            }
            return window.Candidates;
        }

        private static bool Prefix(Building_TurretGunCE __instance, LocalTargetInfo ___forcedTarget,
            ref LocalTargetInfo __result, out bool __state)
        {
            __state = false;
            ArtilleryRaid raid = ArtilleryRegistry.Find(__instance);
            if (raid == null || ___forcedTarget.IsValid) return true;
            // Mechanoid direct-fire: let CE's 3D TryFindNewTarget run, then wall-fallback in Postfix.
            if (raid.Kind == ArtilleryRaidKind.Mechanoid &&
                CeArtillerySight.IsDirectFire(__instance.def, __instance.CompAmmo?.CurrentAmmo))
            {
                __state = true;
                return true;
            }
            Verb verb = __instance.AttackVerb;
            if (verb == null || !verb.Available())
            {
                __result = LocalTargetInfo.Invalid;
                __instance.ResetCurrentTarget();
                return false;
            }
            int tick = Find.TickManager.TicksGame;
            ArtilleryTargets candidates = Candidates(raid, __instance.Map, tick);
            CompFireArc arc = __instance.CompFireArc;
            var inputs = new SearchInputs(__instance.CompAmmo?.CurrentAmmo, __instance.CompAmmo?.SelectedAmmo,
                CeArtillerySight.OperatorFor(__instance), __instance.Position, __instance.Rotation,
                arc?.CenterAngle ?? 0f, arc?.Span ?? 0f, arc != null && ArcOn(arc), Controller.settings.EnableArcOfFire);
            GunSearch search = Searches.GetValue(__instance, static _ => new GunSearch());
            bool reuse = search.Candidates == candidates && search.Inputs == inputs && tick < search.NextSearchTick;
            if (reuse && search.Result.HasTarget)
                reuse = candidates.Filter(__instance, search.Result.Target.Thing, verb.EffectiveRange,
                    search.Result.Target.Thing is not Building building || !(building.def.IsWall || building.def.IsDoor))
                    == ArtilleryTargetReason.GeometryPassed;
            if (!reuse)
            {
                search.Candidates = candidates;
                search.Inputs = inputs;
                search.Result = candidates.Find(__instance);
                search.NextSearchTick = tick + ArtilleryTargets.SearchInterval;
            }
            __result = search.Result.Target;
            if (!search.Result.HasTarget) __instance.ResetCurrentTarget();
            // Do not fall through to CE's every-tick native search on an empty result.
            return false;
        }

        private static void Postfix(Building_TurretGunCE __instance, bool __state, ref LocalTargetInfo __result)
        {
            if (!__state || __result.IsValid) return;
            Verb verb = __instance.AttackVerb;
            if (verb == null || !verb.Available()) return;
            ArtilleryRaid raid = ArtilleryRegistry.Find(__instance);
            if (raid == null) return;
            int tick = Find.TickManager.TicksGame;
            ArtilleryTargets candidates = Candidates(raid, __instance.Map, tick);
            CompFireArc arc = __instance.CompFireArc;
            var inputs = new SearchInputs(__instance.CompAmmo?.CurrentAmmo, __instance.CompAmmo?.SelectedAmmo,
                CeArtillerySight.OperatorFor(__instance), __instance.Position, __instance.Rotation,
                arc?.CenterAngle ?? 0f, arc?.Span ?? 0f, arc != null && ArcOn(arc), Controller.settings.EnableArcOfFire);
            GunSearch search = Searches.GetValue(__instance, static _ => new GunSearch());
            bool reuse = search.Candidates == candidates && search.Inputs == inputs && tick < search.NextSearchTick;
            if (reuse && search.Result.HasTarget)
                reuse = candidates.Filter(__instance, search.Result.Target.Thing, verb.EffectiveRange, fallback: false)
                    == ArtilleryTargetReason.GeometryPassed;
            if (!reuse)
            {
                search.Candidates = candidates;
                search.Inputs = inputs;
                search.Result = candidates.FindWalls(__instance);
                search.NextSearchTick = tick + ArtilleryTargets.SearchInterval;
            }
            __result = search.Result.Target;
            if (!search.Result.HasTarget) __instance.ResetCurrentTarget();
        }
    }

    // Placed after combat and the existing ladder duty, before ordinary trashing.
    public sealed class JobGiver_ArtilleryDemolition : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            Map map = pawn.Map;
            if (map == null || pawn.Downed || pawn.Dead || LadderInventory.Get(pawn) != null ||
                map.GetComponent<SiegeRaidMap>().RecoveryFor(pawn) != null) return null;
            ArtilleryMap artillery = map.GetComponent<ArtilleryMap>();
            ArtilleryRaid raid = artillery.ForPawn(pawn);
            Lord lord = pawn.GetLord();
            bool assaulting = raid != null && (lord == raid.AssaultLord && lord != null ||
                lord == raid.CampLord && lord?.LordJob is LordJob_Artillery native && native.IsNativeAssault);
            if (!assaulting || artillery.IsOperator(pawn)) return null;
            SupportRoute route = SupportRoute.For(raid);
            if (route == null) return null;

            ThingWithComps equipped = pawn.equipment?.Primary;
            if (equipped == null || !SiegeRoles.DemolitionWeapon(equipped))
            {
                CompInventory inventory = pawn.TryGetComp<CompInventory>();
                if (inventory == null) return null;
                foreach (ThingWithComps weapon in inventory.weapons)
                {
                    if (!SiegeRoles.DemolitionWeapon(weapon) || !EquipmentUtility.CanEquip(weapon, pawn)) continue;
                    Verb candidate = weapon.TryGetComp<CompEquippable>()?.PrimaryVerb;
                    if (candidate == null) continue;
                    foreach (Thing target in route.Targets())
                    {
                        if (!pawn.HostileTo(target)) continue;
                        float distance = IsoFloor.HasFloors(map) ? IsoFloor.RangeDistance(pawn.Position, target.Position, map)
                            : (pawn.Position - target.Position).LengthHorizontal;
                        if (distance <= candidate.verbProps.range + System.Math.Max(target.def.Size.x, target.def.Size.z))
                            return JobMaker.MakeJob(CE_JobDefOf.EquipFromInventory, weapon);
                    }
                }
                return null;
            }

            Verb verb = BreachingUtility.FindVerbToUseForBreaching(pawn);
            if (verb == null || verb.IsMeleeAttack) return null;
            foreach (Thing target in route.Targets())
            {
                if (!pawn.HostileTo(target) || !BreachingUtility.CanDamageTarget(verb, target)) continue;
                IntVec3 position = pawn.Position;
                if (!verb.CanHitTargetFrom(position, target))
                {
                    bool found;
                    if (BreachingUtility.LordDataFor(pawn.GetLord()) != null)
                        found = BreachingUtility.TryFindCastPosition(pawn, verb, target, out position);
                    else
                        found = CastPositionFinder.TryFindCastPosition(new CastPositionRequest
                        {
                            caster = pawn, target = target, verb = verb, maxRangeFromTarget = verb.EffectiveRange
                        }, out position);
                    if (!found || !position.IsValid || !pawn.CanReach(position, PathEndMode.OnCell, Danger.Deadly) ||
                        !verb.CanHitTargetFrom(position, target)) continue;
                }
                Job job = JobMaker.MakeJob(JobDefOf.UseVerbOnThing, target, position);
                job.verbToUse = verb;
                job.preventFriendlyFire = true;
                BreachingUtility.FinalizeTrashJob(job);
                return job;
            }
            return null;
        }
    }
}
