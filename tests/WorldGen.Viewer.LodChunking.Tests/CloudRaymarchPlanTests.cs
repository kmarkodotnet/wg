using System;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests
{
    /// <summary>
    /// ND-154: a felhő-raymarch nézetfüggő lépéstervének tesztjei.
    /// </summary>
    public class CloudRaymarchPlanTests
    {
        [Fact]
        public void ShellPadding_ContainsTheShellAndTightensWithTheMesh()
        {
            for (int level = 0; level <= 8; level++)
            {
                double padding = CloudRaymarchPlan.ShellPaddingFactor(level);
                Assert.True(padding >= 1.0, $"level {level}: a burkolónak TARTALMAZNIA kell a héjat ({padding})");
            }
            // Finomabb mesh → kevesebb nagyítás kell.
            for (int level = 0; level < 8; level++)
                Assert.True(CloudRaymarchPlan.ShellPaddingFactor(level + 1) < CloudRaymarchPlan.ShellPaddingFactor(level));
            Assert.Equal(CloudRaymarchPlan.ShellPaddingFactor(0), CloudRaymarchPlan.ShellPaddingFactor(-3), 12);
        }

        [Fact]
        public void ShellPadding_MatchesTheClosedForm()
        {
            // 1/cos(félátló), ahol a félátló a kockagömb-quad szögméretének
            // fele gyök2-szer.
            const int level = 3;
            double cellAngle = (Math.PI * 0.5) / (1 << level);
            double expected = 1.0 / Math.Cos(cellAngle * 0.5 * Math.Sqrt(2.0));
            Assert.Equal(expected, CloudRaymarchPlan.ShellPaddingFactor(level), 12);
            // A dokumentált szám a level 3-as héj-meshre.
            Assert.Equal(1.0097163, CloudRaymarchPlan.ShellPaddingFactor(3), 7);
        }

        [Fact]
        public void ShellPadding_KeepsTheQuadCornersOutsideTheShell()
        {
            // A LÉNYEG: a nagyított mesh SAROKPONTJAI a héjon kívül vannak
            // (a sarok a legtávolabbi pont), tehát a raymarch sosem vesztheti
            // el a héj peremét a sziluettnél.
            const int level = 3;
            double padding = CloudRaymarchPlan.ShellPaddingFactor(level);
            Assert.True(padding * Math.Cos((Math.PI * 0.5) / (1 << level) * 0.5 * Math.Sqrt(2.0)) >= 1.0 - 1e-12);
        }

        [Fact]
        public void ViewSteps_AreClampedToTheDocumentedRange()
        {
            // Nagyon távoli kamera: a héj néhány pixel, a minimum elég.
            Assert.Equal(CloudRaymarchPlan.MinViewSteps,
                CloudRaymarchPlan.ViewSteps(1.0e7, 100.32, 0.32, 1.0, 900));
            // A héjon belül: a maximum.
            Assert.Equal(CloudRaymarchPlan.MaxViewSteps,
                CloudRaymarchPlan.ViewSteps(100.1, 100.32, 0.32, 1.0, 900));
        }

        [Fact]
        public void ViewSteps_DecreaseWithDistance()
        {
            int previous = int.MaxValue;
            for (int i = 0; i < 40; i++)
            {
                double distance = 100.4 + i * 40.0;
                int steps = CloudRaymarchPlan.ViewSteps(distance, 100.32, 0.32, 1.0, 900);
                Assert.InRange(steps, CloudRaymarchPlan.MinViewSteps, CloudRaymarchPlan.MaxViewSteps);
                Assert.True(steps <= previous, $"a távolodással nem növekedhet a lépésszám ({distance}: {steps} > {previous})");
                previous = steps;
            }
        }

        [Fact]
        public void ViewSteps_RiseWithResolutionAndFallWithFieldOfView()
        {
            int lowRes = CloudRaymarchPlan.ViewSteps(140.0, 100.32, 0.32, 1.0, 200);
            int highRes = CloudRaymarchPlan.ViewSteps(140.0, 100.32, 0.32, 1.0, 2000);
            Assert.True(highRes >= lowRes, "több pixel → több lépés");

            int narrow = CloudRaymarchPlan.ViewSteps(140.0, 100.32, 0.32, 0.3, 900);
            int wide = CloudRaymarchPlan.ViewSteps(140.0, 100.32, 0.32, 1.4, 900);
            Assert.True(narrow >= wide, "szűkebb látószög → nagyobb nagyítás → több lépés");
        }

        [Fact]
        public void ShellPixels_IsZeroForDegenerateInput()
        {
            Assert.Equal(0.0, CloudRaymarchPlan.ShellPixels(200.0, 100.0, 0.0, 1.0, 900));
            Assert.Equal(0.0, CloudRaymarchPlan.ShellPixels(200.0, 100.0, 0.3, 1.0, 0));
            Assert.Equal(0.0, CloudRaymarchPlan.ShellPixels(200.0, 100.0, 0.3, 0.0, 900));
        }

        [Fact]
        public void ViewSteps_HandleDegenerateInputWithTheMinimum()
        {
            Assert.Equal(CloudRaymarchPlan.MinViewSteps,
                CloudRaymarchPlan.ViewSteps(double.NaN, 100.32, 0.32, 1.0, 900));
            Assert.Equal(CloudRaymarchPlan.MinViewSteps,
                CloudRaymarchPlan.ViewSteps(200.0, 100.32, 0.0, 1.0, 900));
        }

        [Fact]
        public void TheCutoffIsAVisibleQuantisationFloor()
        {
            // 1% áteresztés a 8 bites kimeneten ~2,5 kvantálási szint - a
            // korai kilépés tehát nem látható különbséget vág le.
            Assert.True(CloudRaymarchPlan.TransmittanceCutoff * 255.0 < 3.0);
            Assert.True(CloudRaymarchPlan.TransmittanceCutoff > 0.0);
        }
    }
}
