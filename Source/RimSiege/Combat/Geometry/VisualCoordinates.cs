using RimSiege.Integration;
using UnityEngine;
using Verse;

namespace RimSiege.Combat.Geometry
{
    internal static class VisualCoordinates
    {
        public static Vector3 Projectile(Vector3 exact, float altitude, Map map)
        {
            float height = exact.y;
            if (IsoFloor.HasFloors(map))
                height = height * IsoFloor.StoreyHeight / CombatHeights.StoreyCe;
            return new Vector3(exact.x, altitude + height / IsoView.HeightSquash, exact.z);
        }
    }
}
