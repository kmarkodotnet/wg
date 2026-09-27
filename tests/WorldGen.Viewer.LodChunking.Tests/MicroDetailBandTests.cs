using System;
using WorldGen.Core.Terrain;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests
{
    /// <summary>
    /// ND-151: a mikro-részlet nézetfüggő sávválasztásának tesztjei. A
    /// legfontosabb közülük a FOLYTONOSSÁG: ha a sávváltás ugrik, zoomolás
    /// közben az egész felszíni textúra pattog - ez pontosan az a hibaosztály,
    /// amit a felhasználó az ND-147/ND-148 körökben "akadásként/lemaradásként"
    /// jelzett, csak itt szín/árnyalat formában jelentkezne.
    /// </summary>
    public sealed class MicroDetailBandTests
    {
        private const double PlanetRadius = 10.0;
        private const double Fov = 0.6981317007977318; // 40 fok
        private const double PixelHeight = 1080.0;

        [Fact]
        public void FarView_FadesTheDetailOut()
        {
            // Bolygó-nézet: a kamera 30 rádiusz távolra - a létra alja is
            // pixel alatti, tehát a részlet nem látszik.
            var band = MicroDetailBand.Select(PlanetRadius * 30.0, PlanetRadius, Fov, PixelHeight);
            Assert.Equal(0.0, band.Visibility);
        }

        [Fact]
        public void CloseView_MakesTheDetailFullyVisible()
        {
            // 1 km-es nagyságrendű magasság egy 10 egység rádiuszú bolygón.
            var band = MicroDetailBand.Select(PlanetRadius * 1.0002, PlanetRadius, Fov, PixelHeight);
            Assert.Equal(1.0, band.Visibility);
            Assert.True(band.BaseFrequency > SurfaceMicroDetail.BaseFrequency);
        }

        [Fact]
        public void Visibility_IsMonotoneWhileApproaching()
        {
            double previous = -1.0;
            for (int i = 0; i <= 200; i++)
            {
                // 30 R -> 1.0001 R, logaritmikus lépésekkel.
                double t = i / 200.0;
                double distance = PlanetRadius * (1.0001 + (30.0 - 1.0001) * Math.Pow(0.5, t * 20.0));
                double visibility = MicroDetailBand.Select(distance, PlanetRadius, Fov, PixelHeight).Visibility;
                Assert.True(visibility >= previous - 1e-12, $"i={i} distance={distance}");
                previous = visibility;
            }
        }

        [Fact]
        public void BaseFrequency_NeverExceedsThePrecisionBudget()
        {
            // Még a felszín alatti kamerapozíció mellett sem: a legfinomabb
            // oktáv a MaxFrequency-n áll meg.
            double finestFactor = Math.Pow(SurfaceMicroDetail.Lacunarity, SurfaceMicroDetail.Octaves - 1);
            foreach (double distance in new[] { 0.0, PlanetRadius * 0.5, PlanetRadius, PlanetRadius * 1.000001 })
            {
                var band = MicroDetailBand.Select(distance, PlanetRadius, Fov, PixelHeight);
                Assert.True(band.BaseFrequency * finestFactor <= SurfaceMicroDetail.MaxFrequency * (1.0 + 1e-9),
                    $"distance={distance} base={band.BaseFrequency}");
            }
        }

        [Fact]
        public void BandCrossing_IsContinuous_TheLadderShiftChangesNothing()
        {
            // frac = 1 az n. sávban UGYANAZT a normált súly-vektort adja a
            // (2B, 4B, 8B) frekvenciákon, mint frac = 0 az (n+1). sávban -
            // ez a sávváltás folytonosságának bizonyítéka.
            double[] endOfBand = MicroDetailBand.OctaveWeights(1.0);
            double[] startOfNext = MicroDetailBand.OctaveWeights(0.0);
            Assert.Equal(0.0, endOfBand[0], 12);
            Assert.Equal(0.0, startOfNext[startOfNext.Length - 1], 12);
            for (int k = 0; k + 1 < endOfBand.Length; k++)
                Assert.Equal(startOfNext[k], endOfBand[k + 1], 12);
        }

        [Fact]
        public void OctaveWeights_AreNormalizedAndDecreasing()
        {
            foreach (double frac in new[] { 0.0, 0.25, 0.5, 0.75, 0.999 })
            {
                double[] w = MicroDetailBand.OctaveWeights(frac);
                double sum = 0.0;
                foreach (double v in w) sum += v;
                Assert.Equal(1.0, sum, 12);
                // Az első (elhalványuló) oktávot kivéve az amplitúdó csökken.
                for (int k = 1; k + 1 < w.Length; k++)
                    Assert.True(w[k] >= w[k + 1] - 1e-12, $"frac={frac} k={k}");
            }
        }

        [Theory]
        [InlineData(0.0, 0.0, 0.0, 0.0)]                       // minden érvénytelen
        [InlineData(20.0, -1.0, Fov, PixelHeight)]             // negatív rádiusz
        [InlineData(20.0, PlanetRadius, 0.0, PixelHeight)]     // nulla FOV
        [InlineData(20.0, PlanetRadius, Math.PI, PixelHeight)] // 180 fokos FOV
        [InlineData(20.0, PlanetRadius, Fov, 0.0)]             // nulla pixelmagasság
        [InlineData(double.NaN, PlanetRadius, Fov, PixelHeight)]
        public void InvalidInputs_DisableTheDetail_WithoutThrowing(
            double distance, double radius, double fov, double pixelHeight)
        {
            var band = MicroDetailBand.Select(distance, radius, fov, pixelHeight);
            Assert.Equal(0.0, band.Visibility);
            Assert.Equal(SurfaceMicroDetail.BaseFrequency, band.BaseFrequency, 12);
            Assert.Equal(0.0, band.Fraction, 12);
        }

        [Fact]
        public void Fraction_StaysInUnitInterval_AcrossTheWholeApproach()
        {
            for (int i = 0; i <= 500; i++)
            {
                double distance = PlanetRadius * (1.0 + 29.0 * Math.Pow(0.5, i / 25.0));
                var band = MicroDetailBand.Select(distance, PlanetRadius, Fov, PixelHeight);
                Assert.InRange(band.Fraction, 0.0, 1.0);
                Assert.InRange(band.Visibility, 0.0, 1.0);
                Assert.True(band.BaseFrequency >= SurfaceMicroDetail.BaseFrequency);
            }
        }
    }
}
