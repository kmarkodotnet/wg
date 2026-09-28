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
        public void MarchSteps_AreFixed_SoTheDeckDoesNotShiftWithTheCamera()
        {
            // FELHASZNÁLÓI VISSZAJELZÉS (2026-09-28): „ahogy mozgatom, adott
            // felhő magassága is ugrál". A menet az ABLAKOT osztja N részre,
            // tehát a lépésszám bármilyen nézetfüggősége eltolja a
            // mintavételi magasságokat. Ez a teszt azt rögzíti, hogy a
            // lépésszám KONSTANS - ha valaki visszavezetne egy kamera-függő
            // változatot, itt bukna el.
            Assert.Equal(32, CloudRaymarchPlan.MarchSteps);
            // A függőleges profil két átmeneti sávja legalább négy mintát kap.
            Assert.True(CloudRaymarchPlan.MarchSteps * WorldGen.Core.Climate.CloudVolume.ProfileBaseFadeFraction >= 4.0);
            Assert.True(CloudRaymarchPlan.MarchSteps * WorldGen.Core.Climate.CloudVolume.ProfileTopFadeFraction >= 4.0);
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
