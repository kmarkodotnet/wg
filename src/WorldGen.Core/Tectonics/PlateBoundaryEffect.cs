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
    /// ND-32 (docs/04-decisions.md, felhasználói vizuális visszajelzés
    /// alapján): az uplift MOST MÁR kéreg-típus-tudatos — óceáni-óceáni
    /// határon jelentősen (nem nullára, hanem egy tört részére) csökkentett,
    /// mert a valóságban ott vulkáni szigetívek (keskeny, alacsonyabb
    /// relief) épülnek, nem kontinentális-ütközés-léptékű hegyláncok. Ez
    /// javítja a korábban vizuálisan észlelt hibát: az óceáni lemezek
    /// határainál az egységes uplift a tengerszint fölé emelte a tile-okat,
    /// irreálisan "szárazföldnek" tűnő sávot húzva óceán közepén.
    ///
    /// A "távolság a határtól" a két legközelebbi lemez-mag dot-product-
    /// jainak KÜLÖNBSÉGÉBŐL (gap) jön — nincs explicit Voronoi-él
    /// konstrukció, nincs transzcendens függvény, tehát BITPONTOS marad
    /// minden platformon (nem kell ND-kockázatot vállalni, szemben a
    /// csillagászati modullal, ld. ND-26).
    ///
    /// ND-35 (docs/04-decisions.md, felhasználói vizuális visszajelzés):
    /// az uplift MOST MÁR a <see cref="CrustElevation.MountainMask"/>
    /// ugyanazon regionális maszkjával van szorozva, mint a domborzat
    /// fraktál-részlete — enélkül a parti sáv MINDIG maximális
    /// kiemelkedést kapott (hiszen az óceán-kontinens határ is
    /// lemezhatár), irreálisan magas "falat" húzva a tengerszint fölé
    /// szinte minden parton, függetlenül attól, hogy ott sík vidéknek
    /// vagy hegyvidéknek kellene lennie.
    /// </summary>
    public static class PlateBoundaryEffect
    {
        public const double DefaultGapScale = 0.04;
        public const double DefaultUpliftMaxMeters = 1500.0;
        public const double DefaultOceanicOceanicUpliftFactor = 0.15;

        /// <summary>A két legnagyobb dot-product egy pozíció és a lemez-magok között.</summary>
        public static void TwoBestDots(
            double x, double y, double z, (double X, double Y, double Z)[] seeds,
            out double best, out double second)
        {
            TwoBestDots(x, y, z, seeds, out best, out second, out _, out _);
        }

        /// <summary>Ugyanaz, mint a másik <c>TwoBestDots</c> túlterhelés,
        /// de a két legközelebbi lemez INDEXÉT is visszaadja (kéreg-típus lekérdezéséhez).</summary>
        public static void TwoBestDots(
            double x, double y, double z, (double X, double Y, double Z)[] seeds,
            out double best, out double second, out int bestIndex, out int secondIndex)
        {
            best = double.NegativeInfinity;
            second = double.NegativeInfinity;
            bestIndex = -1;
            secondIndex = -1;
            for (int i = 0; i < seeds.Length; i++)
            {
                double d = x * seeds[i].X + y * seeds[i].Y + z * seeds[i].Z;
                if (d > best)
                {
                    second = best;
                    secondIndex = bestIndex;
                    best = d;
                    bestIndex = i;
                }
                else if (d > second)
                {
                    second = d;
                    secondIndex = i;
                }
            }
        }

        /// <summary>
        /// Határ-közeli kiemelkedés-bónusz: minél kisebb a gap, annál nagyobb.
        /// Óceáni-óceáni határon <see cref="DefaultOceanicOceanicUpliftFactor"/>
        /// szorzóval csökkentve (ND-32); a <see cref="CrustElevation.MountainMask"/>
        /// regionális maszkjával is szorozva (ND-35) - sík régióban a
        /// parti sáv sem kap maximális kiemelkedést.
        /// </summary>
        public static double BoundaryUplift(
            ulong worldSeed, double x, double y, double z, (double X, double Y, double Z)[] seeds,
            double gapScale = DefaultGapScale, double upliftMax = DefaultUpliftMaxMeters,
            double oceanicOceanicUpliftFactor = DefaultOceanicOceanicUpliftFactor)
        {
            TwoBestDots(x, y, z, seeds, out double best, out double second, out int bestIndex, out int secondIndex);
            double gap = best - second;
            if (gap >= gapScale)
                return 0.0;

            double rawUplift = upliftMax * (1.0 - gap / gapScale);

            bool bestOceanic = CrustElevation.IsOceanic(worldSeed, bestIndex);
            bool secondOceanic = secondIndex >= 0 && CrustElevation.IsOceanic(worldSeed, secondIndex);
            if (bestOceanic && secondOceanic)
                rawUplift *= oceanicOceanicUpliftFactor;

            double mask = CrustElevation.MountainMask(worldSeed, x, y, z);
            return rawUplift * mask;
        }

        /// <summary>Alap-eleváció (§4.2) + határ-közeli uplift-bónusz (§4.3).</summary>
        public static double ElevationWithBoundary(
            ulong worldSeed, int plateId, ulong tileIdValue,
            double x, double y, double z, (double X, double Y, double Z)[] seeds,
            out bool isOceanic,
            double gapScale = DefaultGapScale, double upliftMax = DefaultUpliftMaxMeters)
        {
            double baseElevation = CrustElevation.BaseElevation(worldSeed, plateId, x, y, z, out isOceanic);
            double uplift = BoundaryUplift(worldSeed, x, y, z, seeds, gapScale, upliftMax);
            return baseElevation + uplift;
        }
    }
}
