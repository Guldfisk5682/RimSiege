using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.Raids.Artillery
{
    // Extend only the native resource checks, leaving constructible selection at its normal radius.
    internal static class CampConstructionMaterials
    {
        private const float MaterialRadius = 25f;
        [ThreadStatic] internal static Pawn Current;

        internal static bool Applies(Pawn pawn, Thing target)
        {
            Lord lord = pawn?.GetLord();
            if (pawn?.mindState?.duty?.def != DutyDefOf.Build || lord?.CurLordToil is not LordToil_Siege ||
                lord.LordJob is not LordJob_Artillery artillery || target == null || target.Faction != pawn.Faction ||
                (target is not Frame && target is not Blueprint_Build)) return false;
            ArtilleryRaid raid = artillery.Raid;
            return raid?.Camp != null && ArtilleryCamp.Placements(raid.Camp).Any(item =>
                item.Frame == target || item.Blueprint == target ||
                (item.Cell == target.Position && item.Def == target.def.entityDefToBuild));
        }

        internal static float? Expand(Pawn pawn)
        {
            if (Current == null || pawn != Current || pawn.mindState.maxDistToSquadFlag <= 0f) return null;
            float previous = pawn.mindState.maxDistToSquadFlag;
            pawn.mindState.maxDistToSquadFlag = Math.Max(previous, MaterialRadius);
            return previous;
        }

        internal static void Restore(Pawn pawn, float? previous)
        {
            if (previous.HasValue) pawn.mindState.maxDistToSquadFlag = previous.Value;
        }

        internal sealed class AvailabilityState
        {
            internal Dictionary<int, bool> Cache;
            internal int Key;
            internal bool HadValue, Value;
            internal float? Radius;
        }

        [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanGetResources_NewTemp))]
        private static class CanGetResourcesScope
        {
            private static void Prefix(Thing thing, Pawn pawn, out Pawn __state)
            {
                __state = Current;
                Current = Applies(pawn, thing) ? pawn : null;
            }

            private static void Finalizer(Pawn __state) => Current = __state;
        }

        [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor")]
        private static class DeliverJobScope
        {
            private static void Prefix(Pawn pawn, IConstructible c, out Pawn __state)
            {
                __state = Current;
                Current = c is Thing thing && Applies(pawn, thing) ? pawn : null;
            }

            private static void Finalizer(Pawn __state) => Current = __state;
        }

        [HarmonyPatch(typeof(ItemAvailability), nameof(ItemAvailability.ThingsAvailableAnywhere))]
        private static class AvailabilityRadius
        {
            private static void Prefix(ThingDef need, Pawn pawn, Dictionary<int, bool> ___cachedResults,
                out AvailabilityState __state)
            {
                __state = null;
                float? radius = Expand(pawn);
                if (!radius.HasValue) return;
                Dictionary<int, bool> cache = ___cachedResults;
                int key = Gen.HashCombine(need.GetHashCode(), pawn.Faction);
                __state = new AvailabilityState { Cache = cache, Key = key, Radius = radius };
                __state.HadValue = cache.TryGetValue(key, out __state.Value);
                cache.Remove(key);
            }

            private static void Finalizer(Pawn pawn, AvailabilityState __state)
            {
                if (__state == null) return;
                __state.Cache.Remove(__state.Key);
                if (__state.HadValue) __state.Cache[__state.Key] = __state.Value;
                Restore(pawn, __state.Radius);
            }
        }

        [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ResourceValidator_NewTemp")]
        private static class ResourceRadius
        {
            private static void Prefix(Pawn pawn, out float? __state) => __state = Expand(pawn);
            private static void Finalizer(Pawn pawn, float? __state) => Restore(pawn, __state);
        }

        [HarmonyPatch(typeof(GenAI), nameof(GenAI.CanUseItemForWork))]
        private static class NearbyResourceRadius
        {
            private static void Prefix(Pawn p, out float? __state) => __state = Expand(p);
            private static void Finalizer(Pawn p, float? __state) => Restore(p, __state);
        }
    }
}
