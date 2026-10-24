namespace ThreeDSiege.Bridge
{
    /// <summary>
    /// Thin facade over Combat Extended vertical collision / shot height.
    /// Reuse CollisionVertical and ProjectileCE.shotHeight; do not rewrite ballistic integration.
    /// </summary>
    public static class CeHeight
    {
        // CE WallCollisionHeight is 2f; IsoRimZ Heights.Storey is 1.02f (wall units).
        // Mapping between these spaces is OPEN and must not be silently committed — see Specs/OPEN-QUESTIONS.md.
        public static bool Ready => false;
    }
}
