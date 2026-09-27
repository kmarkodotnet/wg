using System;
using System.Collections.Generic;
using WorldGen.Core;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests
{
    /// <summary>
    /// ND-19 (A12/2) — a GEOMETRIA-eltolás motorfüggetlen része: a
    /// `SurfacePoint` (abszolút, double test-keretbeli pozíció) → origó-relatív
    /// `float32` átváltás, és az emit-lánc két SZERZŐDÉSE:
    ///
    /// 1. **Bit-azonosság bolygóközepű origónál** — ha nincs eltolás, minden
    ///    emittált csúcs bitre az, ami a puszta `(float)` cast lenne. Ez az a
    ///    garancia, amitől a statikus alap-réteg és a másodlagos rétegek képe
    ///    NEM változhat.
    /// 2. **Mért nyereség eltolt origónál** — az ND-19 A12 táblázatának
    ///    sorai (24 km → 2,21 mm, 1 km → 0,069 mm) TÉNYLEGESEN megjelennek a
    ///    csúcsokban, nem csak a naplóban.
    ///
    /// Plusz a `SurfacePoint`-on végzett új műveletek (kivonás, sugár, irány,
    /// egyenlőség) — ezekre épül a chunk-újrahasznosítás és a radiális bias.
    /// </summary>
    public class FloatingOriginGeometryTests
    {
        private const double ViewerRadiusUnits = 100.0;
        private static double MetersPerUnit => PlanetConstants.RadiusMeters / ViewerRadiusUnits;

        /// <summary>A felszín egy pontja: adott irányban, `radius + magasság méterben`.</summary>
        private static SurfacePoint SurfaceAt(double dx, double dy, double dz, double heightMeters)
        {
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double r = ViewerRadiusUnits + heightMeters / MetersPerUnit;
            return new SurfacePoint(dx / length * r, dy / length * r, dz / length * r);
        }

        // ---------------------------------------------------------------
        // 1. SZERZŐDÉS: bolygóközepű origó -> bitre a puszta cast
        // ---------------------------------------------------------------

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.37)]
        [InlineData(-12.5)]
        [InlineData(1234.567891234)]
        [InlineData(1e-9)]
        [InlineData(-7420000.5)]
        public void PlanetCentreOriginEmitsTheBarePlainCastBitForBit(double value)
        {
            RenderOrigin centre = RenderOrigin.PlanetCenter(1.0);
            var p = new SurfacePoint(value, value * 0.5, -value);
            centre.ToLocalVertex(p, out float lx, out float ly, out float lz);

            // BITRE, nem toleranciával: ez a szerződés lényege.
            Assert.Equal(BitConverter.SingleToInt32Bits((float)p.X), BitConverter.SingleToInt32Bits(lx));
            Assert.Equal(BitConverter.SingleToInt32Bits((float)p.Y), BitConverter.SingleToInt32Bits(ly));
            Assert.Equal(BitConverter.SingleToInt32Bits((float)p.Z), BitConverter.SingleToInt32Bits(lz));
        }

        [Fact]
        public void PlanetCentreOriginIsBitIdenticalOnAWholeSampledSurfaceSweep()
        {
            RenderOrigin centre = RenderOrigin.PlanetCenter(64.0);
            var random = new Random(19);
            for (int i = 0; i < 2000; i++)
            {
                SurfacePoint p = SurfaceAt(
                    random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1,
                    (random.NextDouble() - 0.5) * 16000.0);
                centre.ToLocalVertex(p, out float lx, out float ly, out float lz);
                Assert.Equal((float)p.X, lx);
                Assert.Equal((float)p.Y, ly);
                Assert.Equal((float)p.Z, lz);
            }
        }

        // ---------------------------------------------------------------
        // 2. SZERZŐDÉS: eltolt origó -> a MÉRT felbontás-nyereség
        // ---------------------------------------------------------------

        /// <summary>
        /// Az (1,1,1) irány szándékos: EGYETLEN tengely sem esik 0 közelébe.
        /// A 0 körül a `float32` ULP denormálisan kicsi, ezért egy
        /// tengely-irányú (pl. (1,0,0)) mintapont HAMISAN azt mutatná, hogy a
        /// bolygóközepű origó is felbontja a centiméteres részletet - pedig
        /// csak a nulla komponens tette. (Ez a teszt fejlesztés közben pont
        /// ezt a hamis zöldet fogta meg.)
        /// </summary>
        private static readonly double TangentLength = Math.Sqrt(2.0);

        private static SurfacePoint GenericSurfacePoint(double heightMeters)
            => SurfaceAt(1, 1, 1, heightMeters);

        /// <summary>Az (1,1,1)-re merőleges érintő: (1,-1,0)/sqrt(2).</summary>
        private static SurfacePoint AlongTangent(in SurfacePoint p, double metres)
        {
            double units = metres / MetersPerUnit / TangentLength;
            return new SurfacePoint(p.X + units, p.Y - units, p.Z);
        }

        /// <summary>
        /// A lényegi állítás: két, 10 cm-re levő felszíni pont bolygóközepű
        /// origóval UGYANARRA a float32 csúcsra kerekedik (tehát a részlet
        /// elveszik), kamera-illesztett origóval viszont KÜLÖN csúcs marad.
        /// Ez az, amit az A12 1. köre csak naplózni tudott.
        /// </summary>
        [Theory]
        [InlineData(28100.0, 0.25)]   // ~a mai legkozelebbi zoom (ND-19 tablazat "24 km" sora)
        [InlineData(1000.0, 0.0078125)]
        public void AnchoredOriginResolvesDetailThatThePlanetCentreOriginCollapses(
            double altitudeMeters, double expectedCellUnits)
        {
            double altitudeUnits = altitudeMeters / MetersPerUnit;
            double cell = FloatingOrigin.RecommendedCellUnits(altitudeUnits);
            Assert.Equal(expectedCellUnits, cell);

            SurfacePoint camera = GenericSurfacePoint(altitudeMeters);
            RenderOrigin anchored = FloatingOrigin.Snap(camera.X, camera.Y, camera.Z, cell);
            RenderOrigin centre = RenderOrigin.PlanetCenter(cell);

            SurfacePoint a = GenericSurfacePoint(0.0);
            SurfacePoint b = AlongTangent(a, 0.10);

            centre.ToLocalVertex(a, out float ax0, out float ay0, out float az0);
            centre.ToLocalVertex(b, out float bx0, out float by0, out float bz0);
            Assert.Equal(new[] { ax0, ay0, az0 }, new[] { bx0, by0, bz0 });   // 10 cm ELVESZETT

            anchored.ToLocalVertex(a, out float ax1, out float ay1, out float az1);
            anchored.ToLocalVertex(b, out float bx1, out float by1, out float bz1);
            Assert.NotEqual(new[] { ax1, ay1, az1 }, new[] { bx1, by1, bz1 }); // 10 cm MEGMARADT
        }

        /// <summary>
        /// A legkisebb MÉG LÁTHATÓ eltolás (a tényleges kvantálási lépés)
        /// méterben, bináris kereséssel - nem a zárt formulából, hanem a
        /// csúcsokból. Ez hitelesíti a naplózott `gainFactor`-t.
        /// </summary>
        private static double MeasuredStepMetres(in RenderOrigin origin, in SurfacePoint a)
        {
            origin.ToLocalVertex(a, out float ax, out float ay, out float az);
            double low = 0.0, high = 8.0; // 8 m felső korlát: a legrosszabb eset is alatta van
            for (int i = 0; i < 200; i++)
            {
                double mid = (low + high) * 0.5;
                origin.ToLocalVertex(AlongTangent(a, mid), out float mx, out float my, out float mz);
                if (mx != ax || my != ay || mz != az) high = mid; else low = mid;
            }
            return high;
        }

        /// <summary>
        /// A kvantálási lépés MÉRVE, méterben. Két állítás:
        /// - a kamera-illesztett lépés nem rosszabb, mint az ND-19 A12
        ///   táblázatának ZÁRT FORMULÁS legrosszabb esete (a rebase-küszöb
        ///   sugaránál számolt érték) - lehet jobb, hiszen a frissen illesztett
        ///   origó tengelyenként csak fél cellányira van;
        /// - a NYERESÉG az abszolút (bolygóközepű) úthoz képest legalább a
        ///   táblázat szerinti nagyságrend.
        /// </summary>
        [Theory]
        [InlineData(28100.0, 0.00222, 64.0)]
        [InlineData(1000.0, 0.00007, 1024.0)]
        public void MeasuredVertexQuantisationMatchesTheDecisionTable(
            double altitudeMeters, double worstCaseMetersFromTheTable, double minimumGain)
        {
            double altitudeUnits = altitudeMeters / MetersPerUnit;
            double cell = FloatingOrigin.RecommendedCellUnits(altitudeUnits);
            SurfacePoint camera = GenericSurfacePoint(altitudeMeters);
            RenderOrigin anchored = FloatingOrigin.Snap(camera.X, camera.Y, camera.Z, cell);
            RenderOrigin centre = RenderOrigin.PlanetCenter(cell);
            SurfacePoint a = GenericSurfacePoint(0.0);

            double anchoredStep = MeasuredStepMetres(in anchored, in a);
            double absoluteStep = MeasuredStepMetres(in centre, in a);

            // A zárt formula legrosszabb esete a MÉRT lépés FELSŐ korlátja.
            double formula = FloatingOrigin.ResolutionMeters(
                FloatingOrigin.LocalRadiusUnits(anchored, ViewerRadiusUnits), MetersPerUnit);
            Assert.InRange(formula, worstCaseMetersFromTheTable * 0.5, worstCaseMetersFromTheTable * 2.0);
            Assert.True(anchoredStep <= formula * TangentLength * 1.01,
                $"mert lepes {anchoredStep} m > zart formula {formula} m");

            Assert.True(absoluteStep / anchoredStep >= minimumGain,
                $"nyereseg {absoluteStep / anchoredStep:F1}x < elvart {minimumGain}x " +
                $"(abszolut {absoluteStep} m, illesztett {anchoredStep} m)");
        }

        /// <summary>
        /// A rács-illesztés miatt az origó komponensei EGÉSZ SZÁMÚ 2-hatvány
        /// többszörösök, ezért a `p - O` kivonás double-ban EGZAKT - a
        /// kerekítés kizárólag a végső castnál történik. Enélkül az eltolás
        /// maga vinne be hibát.
        /// </summary>
        [Fact]
        public void SubtractingASnappedOriginIsExactInDouble()
        {
            var random = new Random(20260927);
            foreach (double cell in new[] { 64.0, 1.0, 0.25, 1.0 / 1024.0 })
            {
                for (int i = 0; i < 500; i++)
                {
                    SurfacePoint p = SurfaceAt(
                        random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1,
                        (random.NextDouble() - 0.5) * 16000.0);
                    RenderOrigin o = FloatingOrigin.Snap(p.X, p.Y, p.Z, cell);
                    SurfacePoint local = o.ToLocalPoint(p);
                    // Visszaadva PONTOSAN az eredetit kell kapnunk.
                    Assert.Equal(p.X, local.X + o.X);
                    Assert.Equal(p.Y, local.Y + o.Y);
                    Assert.Equal(p.Z, local.Z + o.Z);
                    // És a maradék tengelyenként legfeljebb fél cella.
                    Assert.True(Math.Abs(local.X) <= cell * 0.5 + 1e-12);
                    Assert.True(Math.Abs(local.Y) <= cell * 0.5 + 1e-12);
                    Assert.True(Math.Abs(local.Z) <= cell * 0.5 + 1e-12);
                }
            }
        }

        // ---------------------------------------------------------------
        // A jelenet-konvenció algebrája (a két réteg UGYANAZT képezi le)
        // ---------------------------------------------------------------

        /// <summary>
        /// A két réteg-út egyenértékűsége DOUBLE-ban: a Planet
        /// (`abszolút csúcs`, `localPosition = A - s·R·O`) és a
        /// RefinedLayerRoot (`csúcs = p - O`, `localPosition = A`) UGYANARRA a
        /// szülő-téri pontra képez. Ez a design központi állítása; a
        /// `float32`-beli KÜLÖNBSÉGÜK az, ami miatt a finomított réteg a
        /// pontosabb (a durva úton a `s·R·p - s·R·O` kioltás ~0,57 m-t hagy).
        /// </summary>
        [Fact]
        public void BothLayerPathsMapToTheSameParentSpacePoint()
        {
            // 30 fokos forgatás a Y tengely körül (a spin analógja), s = 1,5, A = (3,-4,5).
            double angle = 30.0 * Math.PI / 180.0;
            double c = Math.Cos(angle), s = Math.Sin(angle);
            const double scale = 1.5;
            var authored = new SurfacePoint(3.0, -4.0, 5.0);
            SurfacePoint Rotate(in SurfacePoint v) => new SurfacePoint(
                c * v.X + s * v.Z, v.Y, -s * v.X + c * v.Z);

            var p = new SurfacePoint(61.25, -12.5, 78.125);
            RenderOrigin o = FloatingOrigin.Snap(p.X + 0.1, p.Y - 0.05, p.Z + 0.2, 0.25);

            // Planet-út: abszolút csúcs, eltolt localPosition.
            SurfacePoint viaPlanet = Rotate(p) * scale + (authored - Rotate(new SurfacePoint(o.X, o.Y, o.Z)) * scale);
            // Finomított út: origó-relatív csúcs, eredeti localPosition.
            SurfacePoint viaRefined = Rotate(o.ToLocalPoint(p)) * scale + authored;

            Assert.Equal(viaPlanet.X, viaRefined.X, 12);
            Assert.Equal(viaPlanet.Y, viaRefined.Y, 12);
            Assert.Equal(viaPlanet.Z, viaRefined.Z, 12);
        }

        // ---------------------------------------------------------------
        // SurfacePoint: az emit-lánc új műveletei
        // ---------------------------------------------------------------

        [Fact]
        public void SubtractionMagnitudeAndDirectionAreExactOnRepresentableValues()
        {
            var a = new SurfacePoint(3.0, 4.0, 0.0);
            var b = new SurfacePoint(1.0, 0.5, -0.25);
            SurfacePoint d = a - b;
            Assert.Equal(2.0, d.X);
            Assert.Equal(3.5, d.Y);
            Assert.Equal(0.25, d.Z);

            Assert.Equal(25.0, a.SqrMagnitude);
            Assert.Equal(5.0, a.Magnitude);
            SurfacePoint n = a.Normalized;
            Assert.Equal(0.6, n.X, 15);
            Assert.Equal(0.8, n.Y, 15);
            Assert.Equal(1.0, n.Magnitude, 15);
        }

        [Fact]
        public void NormalizedOfTheZeroVectorIsZeroNotNaN()
        {
            SurfacePoint n = default(SurfacePoint).Normalized;
            Assert.Equal(0.0, n.X);
            Assert.Equal(0.0, n.Y);
            Assert.Equal(0.0, n.Z);
        }

        /// <summary>
        /// A radiális bias képlete (PlanetGridMesh.ApplyRadialBias) csak
        /// ABSZOLÚT ponton értelmes: az irány a bolygóközéptől mutat kifelé.
        /// Ugyanaz a bias origó-relatív ponton MÁS irányba tolna - ez a teszt
        /// azt rögzíti, miért kell a bias a float32-re váltás ELŐTT.
        /// </summary>
        [Fact]
        public void RadialBiasDirectionDiffersBetweenAbsoluteAndOriginRelativeSpace()
        {
            SurfacePoint p = SurfaceAt(1, 0.2, -0.3, 0);
            RenderOrigin o = FloatingOrigin.Snap(p.X, p.Y, p.Z, 0.25);
            SurfacePoint absoluteDirection = p.Normalized;
            SurfacePoint relativeDirection = o.ToLocalPoint(p).Normalized;

            double dot = absoluteDirection.X * relativeDirection.X
                + absoluteDirection.Y * relativeDirection.Y
                + absoluteDirection.Z * relativeDirection.Z;
            // Nem csak "kicsit mas": a ket irany semmilyen ertelmes kapcsolatban
            // nem all egymassal (a lokalis maradek iranya a racs-illesztes
            // maradeka, nem a radialis irany).
            Assert.True(Math.Abs(dot) < 0.99, "a lokalis irany nem hasznalhato radialis iranyként; dot=" + dot);
        }

        [Fact]
        public void EqualityIsBitExactAndHashesAgree()
        {
            var a = new SurfacePoint(1.0, 2.0, 3.0);
            var b = new SurfacePoint(1.0, 2.0, 3.0);
            // A LEGKISEBB abrazolhato kulonbseg: a `double.Epsilon` hozzaadasa
            // 3.0-hoz nem valtoztat semmit (denormal lepes egy 2^1 oktavban).
            var c = new SurfacePoint(1.0, 2.0, Math.BitIncrement(3.0));
            Assert.True(a.Equals(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals(c));
            Assert.False(a.Equals("nem SurfacePoint"));

            // A chunk-ujrahasznositas ezt az utat hasznalja.
            var previous = new List<SurfacePoint> { a, c };
            Assert.True(DynamicMeshChunking.SamePositions(previous, new List<SurfacePoint> { b, c }));
            Assert.False(DynamicMeshChunking.SamePositions(previous, new List<SurfacePoint> { b, a }));
        }

        [Fact]
        public void LerpEndpointsAreExactSoGeomorphDoesNotDriftTheSharedCorner()
        {
            var a = new SurfacePoint(100.5, -2.25, 7.125);
            var b = new SurfacePoint(100.75, -2.0, 7.0);
            SurfacePoint at0 = SurfacePoint.Lerp(a, b, 0.0);
            SurfacePoint at1 = SurfacePoint.Lerp(a, b, 1.0);
            Assert.Equal(a.X, at0.X);
            Assert.Equal(a.Y, at0.Y);
            Assert.Equal(a.Z, at0.Z);
            Assert.Equal(b.X, at1.X);
            Assert.Equal(b.Y, at1.Y);
            Assert.Equal(b.Z, at1.Z);
        }

        /// <summary>
        /// A geomorph-blend DOUBLE-ban fut (korábban `Vector3.Lerp`, float32).
        /// A teszt azt fogja meg, hogy a blend KÖZBEN sem esik vissza a
        /// pontosság: egy 10 cm-es részlet a morph felénél is megmarad.
        /// </summary>
        [Fact]
        public void GeomorphBlendKeepsSubMetreDetailWithAnAnchoredOrigin()
        {
            double altitudeUnits = 28100.0 / MetersPerUnit;
            double cell = FloatingOrigin.RecommendedCellUnits(altitudeUnits);
            SurfacePoint camera = SurfaceAt(1, 0, 0, 28100.0);
            RenderOrigin anchored = FloatingOrigin.Snap(camera.X, camera.Y, camera.Z, cell);

            SurfacePoint coarse = SurfaceAt(1, 0, 0, 0);
            SurfacePoint fineA = SurfaceAt(1, 0, 0, 200.0);
            var fineB = new SurfacePoint(fineA.X, fineA.Y + 0.10 / MetersPerUnit, fineA.Z);

            SurfacePoint midA = SurfacePoint.Lerp(coarse, fineA, 0.5);
            SurfacePoint midB = SurfacePoint.Lerp(coarse, fineB, 0.5);
            anchored.ToLocalVertex(midA, out _, out float ay, out _);
            anchored.ToLocalVertex(midB, out _, out float by, out _);
            Assert.NotEqual(ay, by);
        }
    }
}
