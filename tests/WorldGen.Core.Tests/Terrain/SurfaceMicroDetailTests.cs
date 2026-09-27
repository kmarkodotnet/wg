using System;
using WorldGen.Core.Tectonics;
using WorldGen.Core.Terrain;
using Xunit;

namespace WorldGen.Core.Tests.Terrain
{
    /// <summary>
    /// ND-151: a mikro-részlet válasz-leképezésének tesztjei. A CLAUDE.md
    /// teszt-táblázata szerint: tisztaság, minden paraméter érdemben hat,
    /// élesetek, plauzibilitás. Ismert-válasz teszt NINCS (nincs külső
    /// referencia egy render-oldali leképezéshez), ezért a monotonitás és a
    /// származtatott konstansok ellenőrzése viszi a súlyt.
    /// </summary>
    public sealed class SurfaceMicroDetailTests
    {
        private const double SeaLevel = 0.0;

        [Fact]
        public void BaseFrequency_ContinuesTheModelOwnOctaveLadder()
        {
            // A létra alja a másodlagos relief-zaj utáni KÖVETKEZŐ oktáv -
            // nem szabadon választott szám.
            double lastModelOctave = CrustElevation.SecondaryNoiseFrequency
                * Math.Pow(2.0, CrustElevation.SecondaryNoiseOctaves - 1);
            Assert.Equal(lastModelOctave * 2.0, SurfaceMicroDetail.BaseFrequency, 12);
        }

        [Fact]
        public void MaxFrequency_MatchesTheFloatPrecisionBudget()
        {
            // (1/64 cella) / (2^-23 relatív float eps) = 2^17.
            const double floatEps = 1.0 / 8388608.0; // 2^-23
            Assert.Equal((1.0 / 64.0) / floatEps, SurfaceMicroDetail.MaxFrequency, 6);
        }

        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(0.05, 1200.0)]
        [InlineData(0.6, -3000.0)]
        [InlineData(0.31, 8000.0)]
        public void Evaluate_IsPure_RepeatedCallsAreBitIdentical(double slope, double elevation)
        {
            var a = SurfaceMicroDetail.Evaluate(slope, elevation, SeaLevel);
            for (int i = 0; i < 4; i++)
            {
                var b = SurfaceMicroDetail.Evaluate(slope, elevation, SeaLevel);
                Assert.Equal(a.NormalAmplitude, b.NormalAmplitude);
                Assert.Equal(a.AlbedoJitter, b.AlbedoJitter);
                Assert.Equal(a.FrequencyScale, b.FrequencyScale);
            }
        }

        [Fact]
        public void Slope_RaisesEveryComponent_PlainToCliff()
        {
            // Pontosan tengerszinten: itt a magassági tag EGZAKT nulla, tehát
            // a síkság a tiszta PlainsNormalAmplitude-ot adja.
            var plain = SurfaceMicroDetail.Evaluate(0.0, SeaLevel, SeaLevel);
            var cliff = SurfaceMicroDetail.Evaluate(0.9, SeaLevel, SeaLevel);
            Assert.True(cliff.NormalAmplitude > plain.NormalAmplitude * 4.0);
            Assert.True(cliff.AlbedoJitter > plain.AlbedoJitter);
            Assert.True(cliff.FrequencyScale > plain.FrequencyScale);
            Assert.Equal(SurfaceMicroDetail.PlainsNormalAmplitude, plain.NormalAmplitude, 12);
            Assert.Equal(SurfaceMicroDetail.RockNormalAmplitude, cliff.NormalAmplitude, 12);
        }

        [Fact]
        public void Altitude_AloneMakesAPlateauRocky()
        {
            // Lejtő NÉLKÜL, csak a magasságtól: a magashegyi plató kőzetes.
            var lowPlain = SurfaceMicroDetail.Evaluate(0.0, 100.0, SeaLevel);
            var highPlateau = SurfaceMicroDetail.Evaluate(0.0, 4000.0, SeaLevel);
            Assert.True(highPlateau.NormalAmplitude > lowPlain.NormalAmplitude * 4.0);
            Assert.Equal(SurfaceMicroDetail.RockNormalAmplitude, highPlateau.NormalAmplitude, 12);
        }

        [Fact]
        public void SeaLevel_ShiftsTheRockAltitudeWithIt()
        {
            // Ugyanaz az ABSZOLÚT magasság: az egyik esetben magashegy, a
            // másikban (megemelt tengerszint) parti síkság.
            var mountain = SurfaceMicroDetail.Evaluate(0.0, 3000.0, 0.0);
            var coastal = SurfaceMicroDetail.Evaluate(0.0, 3000.0, 2900.0);
            Assert.True(mountain.NormalAmplitude > coastal.NormalAmplitude * 4.0);
        }

        [Fact]
        public void Submerged_DampsTheDetail_ButNotToZero()
        {
            var land = SurfaceMicroDetail.Evaluate(0.5, 100.0, SeaLevel);
            var seabed = SurfaceMicroDetail.Evaluate(0.5, -4000.0, SeaLevel);
            Assert.Equal(land.NormalAmplitude * SurfaceMicroDetail.SubmergedNormalScale, seabed.NormalAmplitude, 12);
            Assert.Equal(land.AlbedoJitter * SurfaceMicroDetail.SubmergedAlbedoScale, seabed.AlbedoJitter, 12);
            Assert.True(seabed.NormalAmplitude > 0.0);
            Assert.True(seabed.AlbedoJitter > 0.0);
        }

        [Fact]
        public void SubmergenceTransition_IsSmooth_NoStepAtTheShoreline()
        {
            // A partvonalon átlépve nincs ugrás: a szomszédos minták közti
            // legnagyobb különbség jóval a teljes lépcső alatt marad. Ez az
            // ND-129/ND-149 hibaosztály (látható gyűrű a parton) kapuja.
            double previous = SurfaceMicroDetail.Evaluate(0.5, 400.0, SeaLevel).NormalAmplitude;
            double maxStep = 0.0;
            for (int i = 1; i <= 400; i++)
            {
                double elevation = 400.0 - i * 2.0; // +400 m -> -400 m, 2 m-es lépés
                double current = SurfaceMicroDetail.Evaluate(0.5, elevation, SeaLevel).NormalAmplitude;
                double step = Math.Abs(current - previous);
                if (step > maxStep) maxStep = step;
                previous = current;
            }
            double fullDrop = SurfaceMicroDetail.Evaluate(0.5, 400.0, SeaLevel).NormalAmplitude
                * (1.0 - SurfaceMicroDetail.SubmergedNormalScale);
            Assert.True(maxStep < fullDrop * 0.02, $"maxStep={maxStep} fullDrop={fullDrop}");
        }

        [Fact]
        public void NormalAmplitude_IsMonotoneInSlope()
        {
            double previous = -1.0;
            for (int i = 0; i <= 200; i++)
            {
                double slope = i / 200.0;
                double value = SurfaceMicroDetail.Evaluate(slope, 200.0, SeaLevel).NormalAmplitude;
                Assert.True(value >= previous - 1e-15, $"slope={slope}");
                previous = value;
            }
        }

        [Theory]
        [InlineData(-5.0)]
        [InlineData(0.0)]
        [InlineData(1.0)]
        [InlineData(1e9)]
        public void Smoothstep01_StaysClamped_OnEdgeInputs(double t)
        {
            double value = SurfaceMicroDetail.Smoothstep01(t);
            Assert.InRange(value, 0.0, 1.0);
        }

        [Theory]
        [InlineData(double.MaxValue)]
        [InlineData(double.MinValue)]
        [InlineData(0.0)]
        public void Evaluate_StaysInRange_OnExtremeElevations(double elevation)
        {
            var response = SurfaceMicroDetail.Evaluate(0.4, elevation, SeaLevel);
            Assert.InRange(response.NormalAmplitude, 0.0, SurfaceMicroDetail.RockNormalAmplitude);
            Assert.InRange(response.AlbedoJitter, 0.0, SurfaceMicroDetail.RockAlbedoJitter);
            Assert.InRange(response.FrequencyScale, 1.0, SurfaceMicroDetail.RockFrequencyScale);
        }

        [Fact]
        public void PhaseOffset_IsDeterministic_AndSeedDependent()
        {
            SurfaceMicroDetail.PhaseOffset(12345UL, out double x1, out double y1, out double z1);
            SurfaceMicroDetail.PhaseOffset(12345UL, out double x2, out double y2, out double z2);
            Assert.Equal(x1, x2);
            Assert.Equal(y1, y2);
            Assert.Equal(z1, z2);

            SurfaceMicroDetail.PhaseOffset(12346UL, out double x3, out double y3, out double z3);
            Assert.True(x1 != x3 || y1 != y3 || z1 != z3);
            foreach (double v in new[] { x1, y1, z1, x3, y3, z3 })
                Assert.InRange(v, 0.0, 1024.0);
        }
    }
}
