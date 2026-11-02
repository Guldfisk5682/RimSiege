using IsoRimCEPackZ;
using RimWorld;

namespace RimSiege.Integration
{
    internal sealed class CeSurfaceTerrain : ISurfaceExplosionRule
    {
        public bool ShouldDamageTerrain(SurfaceExplosionContext context)
        {
            var contact = context.Contact;
            if (contact.IncidentSide != SurfaceSide.Below || !contact.HasFloor ||
                context.Explosion.Map != contact.Map || context.Explosion.damType != DamageDefOf.Bomb) return false;
            var grid = contact.Map.terrainGrid;
            return grid.TopTerrainAt(contact.SurfaceCell).destroyOnBombDamageThreshold >= 0f ||
                grid.UnderTerrainAt(contact.SurfaceCell)?.destroyOnBombDamageThreshold >= 0f;
        }
    }
}
