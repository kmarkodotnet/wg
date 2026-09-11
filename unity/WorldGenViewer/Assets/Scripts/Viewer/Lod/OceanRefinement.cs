namespace WorldGen.Viewer.Lod
{
    /// <summary>Mintavételes parti védelem; nem a teljes mező felső korlátja.</summary>
    public static class OceanRefinement
    {
        public static bool CanSkip(bool oceanicCenter, double seaRadiusSquared,
            double corner00RadiusSquared, double corner10RadiusSquared,
            double corner11RadiusSquared, double corner01RadiusSquared)
        {
            // Az egyenlő/hiányzó (NaN) minta bizonytalan: engedjük finomodni.
            return oceanicCenter && seaRadiusSquared > 0.0
                && corner00RadiusSquared < seaRadiusSquared
                && corner10RadiusSquared < seaRadiusSquared
                && corner11RadiusSquared < seaRadiusSquared
                && corner01RadiusSquared < seaRadiusSquared;
        }
    }
}
