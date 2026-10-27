using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace RimSiege.Raids
{
    [DefOf]
    internal static class SiegeDefOf
    {
        public static ThingDef RS_LadderKit = null;
        static SiegeDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SiegeDefOf));
    }

    public sealed class LadderKit : ThingWithComps
    {
        public int RaidId;
        internal Pawn Receiver;
        internal int RetryAt;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref RaidId, "siegeRaidId");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            map.GetComponent<SiegeRaidMap>().Dropped.Add(this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            SiegeRaidMap raids = Map.GetComponent<SiegeRaidMap>();
            raids.Release(this);
            raids.Dropped.Remove(this);
            base.DeSpawn(mode);
        }
    }

    internal static class LadderInventory
    {
        private static readonly ConditionalWeakTable<Pawn, LadderKit> Held = new ConditionalWeakTable<Pawn, LadderKit>();

        internal static LadderKit Get(Pawn pawn) => Held.TryGetValue(pawn, out LadderKit kit) ? kit : null;

        internal static void Added(Pawn pawn, LadderKit kit)
        {
            Held.Remove(pawn);
            Held.Add(pawn, kit);
        }

        internal static void Removed(Pawn pawn) => Held.Remove(pawn);

        internal static void Restore(Pawn pawn)
        {
            Held.Remove(pawn);
            if (pawn.inventory != null)
                foreach (Thing item in pawn.inventory.innerContainer)
                    if (item is LadderKit kit) { Added(pawn, kit); break; }
        }

        internal static void Drop(Pawn pawn)
        {
            LadderKit kit = Get(pawn);
            if (kit != null && pawn.Spawned)
            {
                pawn.inventory.innerContainer.TryDrop(kit, pawn.Position, pawn.Map, ThingPlaceMode.Near, out Thing dropped);
                dropped?.SetForbidden(false);
            }
        }

        internal static void Consume(Pawn pawn, LadderKit kit)
        {
            pawn.inventory.innerContainer.Remove(kit);
            kit.Destroy(DestroyMode.Vanish);
        }
    }
}
