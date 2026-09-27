using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using WorldGen.Core.Terrain;
using Xunit;

namespace WorldGen.Core.Tests
{
    /// <summary>
    /// ND-155 (M13): az égbolt-nyitottság (sky view factor) tesztjei.
    ///
    /// A LEGFONTOSABB TESZT ITT EGY CSAPDAZSINÓR, nem egy elvárás:
    /// <see cref="ReferenceLevelOpenness_IsEffectivelyOne_TheMeasurementBehindNd155"/>
    /// azt RÖGZÍTI, hogy a világmodell domborzatának a referencia-szinten
    /// gyakorlatilag NINCS okkludáló meredeksége — ezért került az AO a
    /// render-oldali mikro-relief sávjába. Ha valaha finomabb relief kerül a
    /// modellbe, ez a teszt elbukik, és pontosan arra mutat rá, hogy az AO
    /// makro sávját ekkor be KELL kötni.
    /// </summary>
    public class SurfaceSkyOpennessTests
    {
        [Fact]
        public void SectorVisibility_DownhillOrLevel_SeesTheWholeSky()
        {
            Assert.Equal(1.0, SurfaceSkyOpenness.SectorVisibility(0.0, 1000.0));
            Assert.Equal(1.0, SurfaceSkyOpenness.SectorVisibility(-500.0, 1000.0));
        }

        [Fact]
        public void SectorVisibility_MatchesCosineSquaredOfTheHorizonAngle()
        {
            // A ZÁRT ALAK visszaellenőrzése: cos²(atan(Δh/d)).
            double[] heights = { 1.0, 10.0, 100.0, 1000.0, 5000.0 };
            foreach (double dh in heights)
            {
                double d = 2000.0;
                double expected = Math.Pow(Math.Cos(Math.Atan(dh / d)), 2.0);
                Assert.Equal(expected, SurfaceSkyOpenness.SectorVisibility(dh, d), 12);
            }
        }

        [Fact]
        public void SectorVisibility_IsMonotoneAndBounded()
        {
            double previous = 1.0;
            for (int i = 1; i <= 50; i++)
            {
                double v = SurfaceSkyOpenness.SectorVisibility(i * 100.0, 1000.0);
                Assert.True(v < previous, "magasabb horizont kevesebb égboltot lát");
                Assert.InRange(v, 0.0, 1.0);
                previous = v;
            }
            // Degenerált távolság: a nulla távolságú, fölé emelkedő fal teljesen takar.
            Assert.Equal(0.0, SurfaceSkyOpenness.SectorVisibility(10.0, 0.0));
        }

        [Fact]
        public void TangentBasis_IsOrthonormalForEveryDirection()
        {
            var grid = DenseGridMetrics.Build(3);
            for (int c = 0; c < grid.CellCount; c++)
            {
                double ux = grid.CenterX[c], uy = grid.CenterY[c], uz = grid.CenterZ[c];
                SurfaceSkyOpenness.TangentBasis(ux, uy, uz,
                    out double tx, out double ty, out double tz,
                    out double bx, out double by, out double bz);
                Assert.Equal(1.0, Math.Sqrt(tx * tx + ty * ty + tz * tz), 9);
                Assert.Equal(1.0, Math.Sqrt(bx * bx + by * by + bz * bz), 9);
                Assert.Equal(0.0, tx * ux + ty * uy + tz * uz, 9);
                Assert.Equal(0.0, bx * ux + by * uy + bz * uz, 9);
                Assert.Equal(0.0, tx * bx + ty * by + tz * bz, 9);
            }
        }

        [Fact]
        public void FlatTerrain_IsFullyOpen()
        {
            var grid = DenseGridMetrics.Build(4);
            var elevation = new double[grid.CellCount];
            for (int c = 0; c < elevation.Length; c++)
                elevation[c] = 500.0;
            double[] openness = SurfaceSkyOpenness.Evaluate(grid, elevation, 0.0);
            foreach (double v in openness)
                Assert.Equal(1.0, v);
        }

        [Fact]
        public void BelowSeaLevel_IsExactlyOne()
        {
            var grid = DenseGridMetrics.Build(4);
            var elevation = new double[grid.CellCount];
            for (int c = 0; c < elevation.Length; c++)
                elevation[c] = -2000.0;
            // Egy kiugró csúcs a víz alatt SEM ad árnyékot a vízfelszínre.
            elevation[10] = 8000.0;
            double[] openness = SurfaceSkyOpenness.Evaluate(grid, elevation, 0.0);
            for (int c = 0; c < openness.Length; c++)
                if (c != 10)
                    Assert.Equal(1.0, openness[c]);
        }

        [Fact]
        public void ASinglePeak_OccludesItsNeighboursButNotItself()
        {
            var grid = DenseGridMetrics.Build(5);
            var elevation = new double[grid.CellCount];
            // Egy nagyon meredek, egyetlen cellás csúcs - a nyitottság
            // ELŐJELÉT és HATÓKÖRÉT teszteli, nem a valós domborzatot.
            const int peak = 300;
            elevation[peak] = 2_000_000.0;
            double[] openness = SurfaceSkyOpenness.Evaluate(grid, elevation, -1.0);

            Assert.Equal(1.0, openness[peak]); // a csúcs maga semmit nem lát maga fölött
            int occluded = 0;
            for (int c = 0; c < openness.Length; c++)
            {
                Assert.InRange(openness[c], 0.0, 1.0);
                if (openness[c] < 0.999) occluded++;
            }
            Assert.True(occluded > 0, "a csúcs környezetének árnyékot kell kapnia");
            // A hatókör VÉGES: a gyűrűk a cellaméret 1..4-szeresében vannak,
            // tehát a rács túlnyomó része érintetlen.
            Assert.True(occluded < openness.Length / 10, $"túl nagy hatókör: {occluded}");
        }

        [Fact]
        public void ReliefExaggeration_DeepensTheOcclusion()
        {
            var grid = DenseGridMetrics.Build(5);
            var elevation = new double[grid.CellCount];
            elevation[300] = 500_000.0;
            double[] plain = SurfaceSkyOpenness.Evaluate(grid, elevation, -1.0, 1.0);
            double[] exaggerated = SurfaceSkyOpenness.Evaluate(grid, elevation, -1.0, 8.0);
            double plainMin = 1.0, exagMin = 1.0;
            foreach (double v in plain) plainMin = Math.Min(plainMin, v);
            foreach (double v in exaggerated) exagMin = Math.Min(exagMin, v);
            Assert.True(exagMin < plainMin, "a nagyított domborzat több égboltot vág el");
        }

        [Fact]
        public void Evaluate_IsPure_RepeatedCallsAreBitIdentical()
        {
            var grid = DenseGridMetrics.Build(4);
            var elevation = new double[grid.CellCount];
            for (int c = 0; c < elevation.Length; c++)
                elevation[c] = 100.0 + (c % 37) * 250.0;
            double[] first = SurfaceSkyOpenness.Evaluate(grid, elevation, 0.0, 4.0);
            double[] second = SurfaceSkyOpenness.Evaluate(grid, elevation, 0.0, 4.0);
            for (int c = 0; c < first.Length; c++)
                Assert.Equal(first[c], second[c]);
        }

        [Fact]
        public void Evaluate_ValidatesItsArguments()
        {
            var grid = DenseGridMetrics.Build(3);
            Assert.Throws<ArgumentNullException>(() => SurfaceSkyOpenness.Evaluate(null!, new double[grid.CellCount], 0.0));
            Assert.Throws<ArgumentNullException>(() => SurfaceSkyOpenness.Evaluate(grid, null!, 0.0));
            Assert.Throws<ArgumentException>(() => SurfaceSkyOpenness.Evaluate(grid, new double[3], 0.0));
        }

        /// <summary>
        /// AZ ND-155 DÖNTÉST MEGALAPOZÓ MÉRÉS, tesztként rögzítve. A
        /// referencia-szintű (level 6) eleváció-mezőn a szárazföldi
        /// nyitottság-átlag 0,999999 — a relief-létra ~1564 km hullámhossznál
        /// véget ér (ld. <see cref="SurfaceMicroDetail.BaseFrequency"/>), tehát
        /// makro-léptéken okkludáló domborzat NEM LÉTEZIK.
        ///
        /// HA EZ A TESZT ELBUKIK, az nem hiba, hanem HÍR: a modell finomabb
        /// reliefet kapott, és ezzel az AO makro sávja értelmet nyert — akkor a
        /// <c>CloudSkyAtlas</c> A csatornáját a terep-shaderben fel kell
        /// erősíteni (ma szándékosan a mért, ~1-es értéket alkalmazza).
        /// </summary>
        [Fact]
        public void ReferenceLevelOpenness_IsEffectivelyOne_TheMeasurementBehindNd155()
        {
            const int level = 6;
            const ulong seed = 0xA7C944210000UL;
            Dictionary<TileId, double> field = SeaLevelCalibration.ComputeElevationField(seed, 20, level);
            double seaLevel = SeaLevelCalibration.CalibrateSeaLevel(field.Values, 0.65);

            DenseGridMetrics grid = DenseGridMetrics.Build(level);
            var elevation = new double[grid.CellCount];
            foreach (KeyValuePair<TileId, double> kv in field)
            {
                kv.Key.GetUV(out uint u, out uint v);
                elevation[DenseGridMetrics.Index(kv.Key.Face, (int)u, (int)v, grid.Side)] = kv.Value;
            }

            double[] openness = SurfaceSkyOpenness.Evaluate(grid, elevation, seaLevel);
            double sum = 0.0, min = 1.0;
            int land = 0;
            for (int c = 0; c < openness.Length; c++)
            {
                if (elevation[c] < seaLevel) continue;
                land++;
                sum += openness[c];
                if (openness[c] < min) min = openness[c];
            }

            Assert.True(land > 1000, $"a világnak van szárazföldje: {land}");
            double mean = sum / land;
            Assert.True(mean > 0.9999, $"a mért szárazföldi nyitottság-átlag {mean:F8} - ha 0,9999 alá esett, a modell finomabb reliefet kapott, és az AO makro sávját be kell kötni (ld. ND-155)");
            Assert.True(min > 0.99, $"a mért minimum {min:F8}");
        }
    }
}
