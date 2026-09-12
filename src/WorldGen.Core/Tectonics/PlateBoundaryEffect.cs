using WorldGen.Core.Terrain;

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
    ///
    /// ND-36 (docs/04-decisions.md, domain warping): a "gap" (határ-
    /// közelség) számítása a <see cref="DomainWarp.WarpPosition"/>-nal
    /// TORZÍTOTT pozíción történik — így maga a határvonal (és az
    /// uplift-zóna) is organikusan hullámzik, nem a nyers Voronoi-cellák
    /// éles, nagykör-ív-szerű határa mentén fut. A <see cref="CrustElevation.MountainMask"/>
    /// viszont SZÁNDÉKOSAN a NYERS pozíciót kapja: az egy regionális
    /// hegyvidékiség-textúra-maszk, a terep-részlet része, nem a
    /// lemez-topológia — nem indokolt ugyanazzal a warp-torzítással
    /// összekötni.
    /// </summary>
    public static class PlateBoundaryEffect
    {
        public const double DefaultGapScale = 0.04;
        public const double DefaultUpliftMaxMeters = 1000.0;
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
        ///
        /// Ez az overload MAGA számolja ki a <see cref="DomainWarp.WarpPosition"/>-t
        /// a nyers (x,y,z)-ből. Ha a hívó már rendelkezik a warpolt pozícióval
        /// (pl. az <c>AssignPlate</c>-hez is felhasználta), használja a
        /// <see cref="BoundaryUpliftFromWarped"/> overloadot, hogy a warpot
        /// ne kelljen ugyanarra a pontra kétszer kiszámolni (ND-39 "C" opció).
        /// </summary>
        public static double BoundaryUplift(
            ulong worldSeed, double x, double y, double z, (double X, double Y, double Z)[] seeds,
            double gapScale = DefaultGapScale, double upliftMax = DefaultUpliftMaxMeters,
            double oceanicOceanicUpliftFactor = DefaultOceanicOceanicUpliftFactor)
        {
            DomainWarp.WarpPosition(worldSeed, x, y, z, out double wx, out double wy, out double wz);
            return BoundaryUpliftFromWarped(worldSeed, x, y, z, wx, wy, wz, seeds, gapScale, upliftMax, oceanicOceanicUpliftFactor);
        }

        /// <summary>
        /// Ugyanaz, mint <see cref="BoundaryUplift"/>, de a MÁR KISZÁMÍTOTT
        /// warpolt <paramref name="wx"/>/<paramref name="wy"/>/<paramref name="wz"/>
        /// pozíciót kapja paraméterként, ahelyett hogy saját maga újraszámolná
        /// a <see cref="DomainWarp.WarpPosition"/>-t (ND-39 "C" opció -
        /// warp-hoisting, tisztán teljesítmény-refaktor, bitre azonos
        /// eredményt ad, mint a warpot belül újraszámoló <see cref="BoundaryUplift"/>).
        /// A nyers <paramref name="x"/>/<paramref name="y"/>/<paramref name="z"/>
        /// TOVÁBBRA IS kell a <see cref="CrustElevation.MountainMask"/>
        /// hívásához - az SZÁNDÉKOSAN a nyers pozíciót kapja (ld. osztály-doc, ND-36).
        /// </summary>
        public static double BoundaryUpliftFromWarped(
            ulong worldSeed, double x, double y, double z,
            double wx, double wy, double wz, (double X, double Y, double Z)[] seeds,
            double gapScale = DefaultGapScale, double upliftMax = DefaultUpliftMaxMeters,
            double oceanicOceanicUpliftFactor = DefaultOceanicOceanicUpliftFactor)
        {
            TwoBestDots(wx, wy, wz, seeds, out double best, out double second, out int bestIndex, out int secondIndex);
            if (best - second >= gapScale)
                return 0.0;
            double mountainMask = CrustElevation.MountainMask(worldSeed, x, y, z);
            return BoundaryUpliftFromNearestPlates(
                worldSeed, best, second, bestIndex, secondIndex, mountainMask,
                gapScale, upliftMax, oceanicOceanicUpliftFactor);
        }

        /// <summary>
        /// Már meghatározott két legközelebbi lemez és már kiszámolt
        /// MountainMask alapján adja az upliftet. Az ND-63 exact cache-útja
        /// ezzel nem ismétli meg sem a seed-szkennelést, sem a maszk zaját.
        /// </summary>
        public static double BoundaryUpliftFromNearestPlates(
            ulong worldSeed,
            double best, double second, int bestIndex, int secondIndex,
            double mountainMask,
            double gapScale = DefaultGapScale, double upliftMax = DefaultUpliftMaxMeters,
            double oceanicOceanicUpliftFactor = DefaultOceanicOceanicUpliftFactor)
        {
            double gap = best - second;
            if (gap >= gapScale)
                return 0.0;

            double rawUplift = upliftMax * (1.0 - gap / gapScale);

            bool bestOceanic = CrustElevation.IsOceanic(worldSeed, bestIndex);
            bool secondOceanic = secondIndex >= 0 && CrustElevation.IsOceanic(worldSeed, secondIndex);
            if (bestOceanic && secondOceanic)
                rawUplift *= oceanicOceanicUpliftFactor;
            return rawUplift * mountainMask;
        }

        /// <summary>
        /// Alap-eleváció (§4.2) + határ-közeli uplift-bónusz (§4.3). Ez az
        /// overload MAGA számolja ki a warpot - ld. <see cref="BoundaryUplift"/>
        /// megjegyzését az <see cref="ElevationWithBoundaryFromWarped"/>
        /// preferálásáról, ha a hívó már ismeri a warpolt pozíciót.
        /// </summary>
        public static double ElevationWithBoundary(
            ulong worldSeed, int plateId, ulong tileIdValue,
            double x, double y, double z, (double X, double Y, double Z)[] seeds,
            out bool isOceanic,
            double gapScale = DefaultGapScale, double upliftMax = DefaultUpliftMaxMeters)
        {
            DomainWarp.WarpPosition(worldSeed, x, y, z, out double wx, out double wy, out double wz);
            return ElevationWithBoundaryFromWarped(
                worldSeed, plateId, tileIdValue, x, y, z, wx, wy, wz, seeds, out isOceanic, gapScale, upliftMax);
        }

        /// <summary>
        /// Ugyanaz, mint <see cref="ElevationWithBoundary"/>, de a MÁR
        /// KISZÁMÍTOTT warpolt pozíciót kapja paraméterként (ND-39 "C" opció -
        /// warp-hoisting). Jellemzően akkor hívandó, ha a hívó ugyanerre a
        /// pontra már meghívta a <see cref="DomainWarp.WarpPosition"/>-t az
        /// <c>AssignPlate</c>-hez (pl. <see cref="SeaLevelCalibration"/>,
        /// a Unity <c>PlanetGridMesh.ComputeDisplacedRadius</c>) - így a warp
        /// (3 független, oktávonkénti fBm-kiértékelés, ld. ND-39) pontonként
        /// csak EGYSZER fut le, nem kétszer.
        /// </summary>
        public static double ElevationWithBoundaryFromWarped(
            ulong worldSeed, int plateId, ulong tileIdValue,
            double x, double y, double z,
            double wx, double wy, double wz, (double X, double Y, double Z)[] seeds,
            out bool isOceanic,
            double gapScale = DefaultGapScale, double upliftMax = DefaultUpliftMaxMeters)
        {
            TwoBestDots(
                wx, wy, wz, seeds,
                out double best, out double second, out int bestIndex, out int secondIndex);
            CrustElevation.ComputeNoiseBasis(
                worldSeed, x, y, z,
                out double primaryNoise, out double mountainMask, out double secondaryNoise);
            double baseElevation = CrustElevation.BlendedBaseElevationFromNoiseBasis(
                worldSeed, best, second, bestIndex, secondIndex,
                primaryNoise, mountainMask, secondaryNoise, out isOceanic);
            double uplift = BoundaryUpliftFromNearestPlates(
                worldSeed, best, second, bestIndex, secondIndex, mountainMask,
                gapScale, upliftMax);
            return baseElevation + uplift;
        }
    }
}
