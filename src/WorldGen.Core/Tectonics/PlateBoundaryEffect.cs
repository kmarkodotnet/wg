namespace WorldGen.Core.Tectonics
{
    /// <summary>
    /// M4 lemezhatár-hatás — hegység-proxy (§4.3, docs/05-milestones.md).
    ///
    /// HATÓKÖR (dokumentált egyszerűsítés): a lemezek NEM mozognak M4-ben
    /// (ld. §4 hatókör-szakasz), ezért nincs valódi konvergens/divergens/
    /// transform megkülönböztetés — az M10 ("Deep time") adja majd hozzá a
    /// sebesség-adatot, ami ezt lehetővé tenné. Helyette EGYSÉGES,
    /// "határ-közeli kiemelkedés": minél közelebb van egy tile a két lemez
    /// közötti határhoz, annál nagyobb az uplift-bónusz.
    ///
    /// A "távolság a határtól" a két legközelebbi lemez-mag dot-product-
    /// jainak KÜLÖNBSÉGÉBŐL (gap) jön — nincs explicit Voronoi-él
    /// konstrukció, nincs transzcendens függvény, tehát BITPONTOS marad
    /// minden platformon (nem kell ND-kockázatot vállalni, szemben a
    /// csillagászati modullal, ld. ND-26).
    /// </summary>
    public static class PlateBoundaryEffect
    {
        public const double DefaultGapScale = 0.04;
        public const double DefaultUpliftMaxMeters = 1500.0;

        /// <summary>A két legnagyobb dot-product egy pozíció és a lemez-magok között.</summary>
        public static void TwoBestDots(
            double x, double y, double z, (double X, double Y, double Z)[] seeds,
            out double best, out double second)
        {
            best = double.NegativeInfinity;
            second = double.NegativeInfinity;
            for (int i = 0; i < seeds.Length; i++)
            {
                double d = x * seeds[i].X + y * seeds[i].Y + z * seeds[i].Z;
                if (d > best)
                {
                    second = best;
                    best = d;
                }
                else if (d > second)
                {
                    second = d;
                }
            }
        }

        /// <summary>Határ-közeli kiemelkedés-bónusz: minél kisebb a gap, annál nagyobb.</summary>
        public static double BoundaryUplift(
            double x, double y, double z, (double X, double Y, double Z)[] seeds,
            double gapScale = DefaultGapScale, double upliftMax = DefaultUpliftMaxMeters)
        {
            TwoBestDots(x, y, z, seeds, out double best, out double second);
            double gap = best - second;
            if (gap >= gapScale)
                return 0.0;
            return upliftMax * (1.0 - gap / gapScale);
        }

        /// <summary>Alap-eleváció (§4.2) + határ-közeli uplift-bónusz (§4.3).</summary>
        public static double ElevationWithBoundary(
            ulong worldSeed, int plateId, ulong tileIdValue,
            double x, double y, double z, (double X, double Y, double Z)[] seeds,
            out bool isOceanic,
            double gapScale = DefaultGapScale, double upliftMax = DefaultUpliftMaxMeters)
        {
            double baseElevation = CrustElevation.BaseElevation(worldSeed, plateId, x, y, z, out isOceanic);
            double uplift = BoundaryUplift(x, y, z, seeds, gapScale, upliftMax);
            return baseElevation + uplift;
        }
    }
}
