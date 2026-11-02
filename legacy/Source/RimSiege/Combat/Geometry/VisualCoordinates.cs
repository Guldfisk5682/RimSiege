using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Geometry
{
    internal static class VisualCoordinates
    {
        public static float Height(float height, Map map) => IsoFloor.HasFloors(map)
            ? height * IsoFloor.StoreyHeight / CombatHeights.StoreyCe : height;

        public static Vector3 Projectile(Vector3 exact, float altitude, Map map)
        {
            return new Vector3(exact.x, altitude + Height(exact.y, map) / IsoView.HeightSquash, exact.z);
        }
    }
}
