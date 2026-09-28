using System;
using WorldGen.Core.Climate;
using WorldGen.Core.Terrain;
using Xunit;

namespace WorldGen.Core.Tests
{
    /// <summary>
    /// ND-154 (M13): a térfogati felhő render-paramétereinek tesztjei.
    ///
    /// A <see cref="CloudVolume"/> render-oldali (a GPU-n futó raymarch
    /// számainak EGYETLEN igazságforrása), ezért nincs hozzá külső KAT-vektor;
    /// amit tesztelni KELL, az (a) a tisztaság, (b) az I3-garanciák
    /// (nulla lefedettség → egzaktul nulla felhő; nulla lefedettség → egzaktul
    /// 1-es napfény-szorzó), (c) a származtatott konstansok visszaellenőrzése
    /// és (d) az élesetek.
    /// </summary>
    public class CloudVolumeTests
    {
        // ------------------------------------------------------------------
        // Lefedettség
        // ------------------------------------------------------------------

        [Fact]
        public void ShapeCoverage_BelowFloor_IsExactlyZero()
        {
            Assert.Equal(0.0, CloudVolume.ShapeCoverage(0.5, 1.0, 2.0, 1.3));
            Assert.Equal(0.0, CloudVolume.ShapeCoverage(1.0, 1.0, 2.0, 1.3));
            Assert.Equal(0.0, CloudVolume.ShapeCoverage(-5.0, 1.0, 2.0, 1.3));
        }

        [Fact]
        public void ShapeCoverage_AtOrAboveCeiling_IsOne()
        {
            Assert.Equal(1.0, CloudVolume.ShapeCoverage(2.0, 1.0, 2.0, 1.3));
            Assert.Equal(1.0, CloudVolume.ShapeCoverage(9.0, 1.0, 2.0, 1.3));
        }

        [Fact]
        public void ShapeCoverage_IsMonotoneBetweenFloorAndCeiling()
        {
            double previous = -1.0;
            for (int i = 0; i <= 40; i++)
            {
                double precip = 1.0 + i / 40.0;
                double c = CloudVolume.ShapeCoverage(precip, 1.0, 2.0, 1.3);
                Assert.True(c >= previous, $"nem monoton {precip}-nél: {c} < {previous}");
                Assert.InRange(c, 0.0, 1.0);
                previous = c;
            }
        }

        [Fact]
        public void ShapeCoverage_GammaAboveOne_SuppressesTheMiddle()
        {
            double linear = CloudVolume.ShapeCoverage(1.5, 1.0, 2.0, 1.0);
            double shaped = CloudVolume.ShapeCoverage(1.5, 1.0, 2.0, 1.3);
            Assert.Equal(0.5, linear, 12);
            Assert.True(shaped < linear, "gamma > 1 mellett a középérték csökken");
        }

        [Fact]
        public void ShapeCoverage_DegenerateSpan_IsOne()
        {
            // A hívó oldal plafon > padló-t garantál, de a függvény ne osszon nullával.
            Assert.Equal(1.0, CloudVolume.ShapeCoverage(2.0, 1.0, 1.0, 1.3));
        }

        [Theory]
        [InlineData(-1.0, 0.25)]
        [InlineData(0.0, 0.25)]
        [InlineData(0.5, 0.5)]
        [InlineData(1.0, 0.75)]
        [InlineData(2.0, 0.75)]
        public void BlendByOceanFraction_ClampsAndInterpolates(double oceanFraction, double expected)
        {
            Assert.Equal(expected, CloudVolume.BlendByOceanFraction(0.25, 0.75, oceanFraction), 12);
        }

        // ------------------------------------------------------------------
        // Függőleges szerkezet
        // ------------------------------------------------------------------

        [Fact]
        public void RelativeHumidity_HitsTheDocumentedEndpoints()
        {
            Assert.Equal(CloudVolume.RelativeHumidityDryPercent, CloudVolume.RelativeHumidityPercent(0.0), 12);
            Assert.Equal(CloudVolume.RelativeHumidityWetPercent, CloudVolume.RelativeHumidityPercent(1.0), 12);
            Assert.Equal(CloudVolume.RelativeHumidityDryPercent, CloudVolume.RelativeHumidityPercent(-3.0), 12);
            Assert.Equal(CloudVolume.RelativeHumidityWetPercent, CloudVolume.RelativeHumidityPercent(7.0), 12);
        }

        [Fact]
        public void LiftingCondensationLevel_FollowsLawrenceAndRespectsTheFloor()
        {
            // Száraz vég: 25 m/% * (100 - 40) = 1500 m.
            Assert.Equal(1500.0, CloudVolume.LiftingCondensationLevelMeters(0.0), 9);
            // Telített vég: 25 * 5 = 125 m, amit a köd-padló 250 m-re emel.
            Assert.Equal(CloudVolume.MinBaseAboveGroundMeters, CloudVolume.LiftingCondensationLevelMeters(1.0), 9);
        }

        [Fact]
        public void LiftingCondensationLevel_DecreasesWithCoverage()
        {
            double previous = double.MaxValue;
            for (int i = 0; i <= 20; i++)
            {
                double lcl = CloudVolume.LiftingCondensationLevelMeters(i / 20.0);
                Assert.True(lcl <= previous, "a nedvesebb levegő kondenzációs szintje nem lehet magasabb");
                previous = lcl;
            }
        }

        [Fact]
        public void CloudBase_IsAHorizontalSheet_NotTerrainFollowing()
        {
            // FELHASZNÁLÓI VISSZAJELZÉS (2026-09-27): a terepkövető alj miatt a
            // felhő „pontosan talajszintre" került. A középszintű dekk
            // VÍZSZINTES lap: a mért domborzat teljes tartományán (0-2308 m)
            // ugyanazon a szinten van.
            double atSeaLevel = CloudVolume.CloudBaseAboveSeaLevelMeters(0.5, 0.0);
            Assert.Equal(CloudVolume.MidLevelBaseMeters, atSeaLevel, 9);
            // A mért szárazföld-eloszlás jellemző pontjai (medián 325 m,
            // p90 878 m, p99 1469 m) MIND a sík szakaszra esnek - a dekk tehát
            // a szárazföld több mint 99%-a fölött vízszintes lap.
            foreach (double ground in new[] { 0.0, 325.0, 878.0, 1469.0 })
                Assert.Equal(CloudVolume.MidLevelBaseMeters,
                    CloudVolume.CloudBaseAboveSeaLevelMeters(0.5, ground), 9);
            // 1469 m-nél a lap 1031 m-rel a talaj FÖLÖTT van - ez az, ami a
            // „felszínre festett matrica" benyomást megszünteti.
            Assert.True(CloudVolume.MidLevelBaseMeters - 1469.0 > 1000.0);
        }

        [Fact]
        public void CloudBase_LiftsOnlyWhereTheGroundWouldPushThroughTheSheet()
        {
            // Kivételesen magas terep fölött a kondenzációs szint a lap FÖLÉ
            // kerül, és onnan a dekk ráborul a hegyláncra - az átmenet folytonos.
            double lcl = CloudVolume.LiftingCondensationLevelMeters(0.5);
            double threshold = CloudVolume.MidLevelBaseMeters - lcl;
            Assert.Equal(CloudVolume.MidLevelBaseMeters,
                CloudVolume.CloudBaseAboveSeaLevelMeters(0.5, threshold), 9);
            Assert.Equal(threshold + 1000.0 + lcl,
                CloudVolume.CloudBaseAboveSeaLevelMeters(0.5, threshold + 1000.0), 9);

            double previous = 0.0;
            for (int i = 0; i <= 40; i++)
            {
                double b = CloudVolume.CloudBaseAboveSeaLevelMeters(0.5, i * 200.0);
                Assert.True(b >= previous - 1e-9, "a felhőalap nem süllyedhet a magasabb terep fölött");
                previous = b;
            }
        }

        [Fact]
        public void CloudBase_DeckLevelIsLiveTunable()
        {
            // A látvány-ítélethez (todo2 B17) a lap szintje élőben hangolható.
            Assert.Equal(4000.0, CloudVolume.CloudBaseAboveSeaLevelMeters(0.5, 0.0, 4000.0), 9);
            Assert.Equal(1000.0, CloudVolume.CloudBaseAboveSeaLevelMeters(0.5, 0.0, 1000.0), 9);
            // A csúszka nem teheti a domborzat alá a dekket ott, ahol a talaj magasabb.
            Assert.True(CloudVolume.CloudBaseAboveSeaLevelMeters(0.5, 2308.0, 500.0) > 2308.0);
        }

        [Fact]
        public void CloudBase_IsCappedByThePackingCeiling()
        {
            double b = CloudVolume.CloudBaseAboveSeaLevelMeters(0.0, 50_000.0);
            Assert.Equal(CloudVolume.MaxBaseAboveSeaLevelMeters, b, 9);
        }

        [Fact]
        public void SubGridEdgeWidth_IsSoftEnoughToLeaveMostOfACellTranslucent()
        {
            // FELHASZNÁLÓI VISSZAJELZÉS (2026-09-27): „totálisan takarja minden
            // felhő a területet". A sávszélesség azt szabja meg, a cella
            // mekkora hányada teljesen OPAK: egy 0,3-as lefedettségű cellában
            // az opak mag aránya T - w, ami a korábbi 0,25-nél 17,5%, a
            // mostani 0,35-nél 12,5% (0,55-nél 2,5% lenne, de az MÉRVE túl
            // sok eget kent be vékony felhővel).
            const double coverage = 0.3;
            double opaqueFraction = CloudVolume.SubGridThreshold(coverage) - CloudVolume.SubGridEdgeWidth;
            if (opaqueFraction < 0.0) opaqueFraction = 0.0;
            Assert.True(opaqueFraction < 0.15,
                $"a teljesen opak mag aránya {opaqueFraction:F3} - túl sok a cellából");
            // ...és a cella-átlag EKKOR IS a modellezett érték marad.
            const int samples = 20000;
            double sum = 0.0;
            for (int i = 0; i < samples; i++)
                sum += CloudVolume.SubGridCoverage(coverage, (i + 0.5) / samples);
            Assert.Equal(coverage, sum / samples, 2);
        }

        [Fact]
        public void CloudBase_IgnoresGroundBelowSeaLevel()
        {
            Assert.Equal(
                CloudVolume.CloudBaseAboveSeaLevelMeters(0.3, 0.0),
                CloudVolume.CloudBaseAboveSeaLevelMeters(0.3, -4000.0), 12);
        }

        [Fact]
        public void CloudThickness_SpansAThinVeilToDeepConvection()
        {
            // Az alsó vég ÁTTETSZŐ fátyol (optikai mélység ~1,1), a felső vég
            // zivatarfelhő - enélkül minden felhő teljesen opak volt.
            Assert.Equal(CloudVolume.MinThicknessMeters, CloudVolume.CloudThicknessMeters(0.0, 0.0), 9);
            Assert.Equal(
                CloudVolume.StratusThicknessMeters + CloudVolume.ConvectiveThicknessMeters,
                CloudVolume.CloudThicknessMeters(1.0, 0.0), 9);

            double thinTau = CloudVolume.ExtinctionPerMeter * CloudVolume.VerticalProfileMean * CloudVolume.MinThicknessMeters;
            Assert.InRange(thinTau, 0.5, 2.0);
            Assert.InRange(CloudVolume.Transmittance(thinTau), 0.13, 0.61);
        }

        [Fact]
        public void CloudThickness_GrowsMonotonicallyWithCoverage()
        {
            double previous = 0.0;
            for (int i = 0; i <= 20; i++)
            {
                double t = CloudVolume.CloudThicknessMeters(i / 20.0, 0.0);
                Assert.True(t > previous, "vastagabb felhő tartozik a nagyobb lefedettséghez");
                previous = t;
            }
        }

        [Fact]
        public void CloudThickness_HasTheOrographicTerm()
        {
            double flat = CloudVolume.CloudThicknessMeters(1.0, 0.0);
            double mountain = CloudVolume.CloudThicknessMeters(1.0, 3000.0);
            Assert.Equal(flat + CloudVolume.OrographicThicknessFactor * 3000.0, mountain, 9);
            // A lefedettség nélküli terep NEM kap orografikus felhőt.
            Assert.Equal(
                CloudVolume.CloudThicknessMeters(0.0, 0.0),
                CloudVolume.CloudThicknessMeters(0.0, 3000.0), 12);
        }

        [Fact]
        public void CloudThickness_IsCappedByThePackingCeiling()
        {
            Assert.Equal(CloudVolume.MaxThicknessMeters, CloudVolume.CloudThicknessMeters(1.0, 40_000.0), 9);
        }

        [Fact]
        public void ShellOuter_IsTheSumOfTheTwoPackingCeilings()
        {
            Assert.Equal(
                CloudVolume.MaxBaseAboveSeaLevelMeters + CloudVolume.MaxThicknessMeters,
                CloudVolume.ShellOuterAboveSeaLevelMeters, 12);
            // A csomagolási plafonok tényleg befoglalják a leképezés értékkészletét.
            for (int i = 0; i <= 20; i++)
            {
                double c = i / 20.0;
                Assert.True(CloudVolume.CloudBaseAboveSeaLevelMeters(c, 9000.0) <= CloudVolume.MaxBaseAboveSeaLevelMeters);
                Assert.True(CloudVolume.CloudThicknessMeters(c, 9000.0) <= CloudVolume.MaxThicknessMeters);
            }
        }

        // ------------------------------------------------------------------
        // Optika
        // ------------------------------------------------------------------

        [Fact]
        public void VerticalProfile_IsZeroAtBothEndsAndPositiveInside()
        {
            Assert.Equal(0.0, CloudVolume.VerticalProfile(0.0));
            Assert.Equal(0.0, CloudVolume.VerticalProfile(1.0));
            Assert.Equal(0.0, CloudVolume.VerticalProfile(-0.5));
            Assert.Equal(0.0, CloudVolume.VerticalProfile(1.5));
            for (int i = 1; i < 20; i++)
                Assert.True(CloudVolume.VerticalProfile(i / 20.0) > 0.0);
        }

        [Fact]
        public void VerticalProfile_PeaksInsideTheDeckAndStaysBelowOne()
        {
            double max = 0.0;
            for (int i = 0; i <= 10000; i++)
            {
                double v = CloudVolume.VerticalProfile(i / 10000.0);
                Assert.InRange(v, 0.0, 1.0);
                if (v > max) max = v;
            }
            Assert.True(max > 0.99, $"a profilnak el kell érnie a plató 1-et, max = {max}");
        }

        [Fact]
        public void VerticalProfileMean_MatchesTheNumericIntegral()
        {
            // A SZÁRMAZTATÁS: a kvintikus fade szimmetrikus, tehát mindkét
            // átmeneti sáv épp a felét veszi el → 1 - (0,18 + 0,42)/2 = 0,7.
            const int n = 200000;
            double sum = 0.0;
            for (int i = 0; i < n; i++)
                sum += CloudVolume.VerticalProfile((i + 0.5) / n);
            double mean = sum / n;
            Assert.Equal(CloudVolume.VerticalProfileMean, mean, 5);
            Assert.Equal(
                1.0 - (CloudVolume.ProfileBaseFadeFraction + CloudVolume.ProfileTopFadeFraction) / 2.0,
                CloudVolume.VerticalProfileMean, 12);
        }

        [Fact]
        public void Transmittance_IsBeerLambertAndMonotone()
        {
            Assert.Equal(1.0, CloudVolume.Transmittance(0.0));
            Assert.Equal(1.0, CloudVolume.Transmittance(-1.0));
            double previous = 1.0;
            for (int i = 1; i <= 40; i++)
            {
                double t = CloudVolume.Transmittance(i * 0.5);
                Assert.True(t < previous, "az áteresztés szigorúan csökken az optikai mélységgel");
                Assert.InRange(t, 0.0, 1.0);
                previous = t;
            }
            Assert.Equal(Math.Exp(-10.0), CloudVolume.Transmittance(10.0), 9);
        }

        [Fact]
        public void PhaseFunction_IntegratesToOneOverTheSphere()
        {
            // 4π·∫ p(cosθ) dΩ/(4π) = 2π·∫_{-1}^{1} p(μ) dμ  = 1
            const int n = 400000;
            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                double mu = -1.0 + 2.0 * (i + 0.5) / n;
                sum += CloudVolume.PhaseFunction(mu);
            }
            double integral = sum * (2.0 / n) * 2.0 * Math.PI;
            Assert.Equal(1.0, integral, 3);
        }

        [Fact]
        public void PhaseFunction_IsForwardScattering()
        {
            Assert.True(CloudVolume.PhaseFunction(1.0) > 50.0 * CloudVolume.PhaseFunction(-1.0),
                "a vízcsepp-felhő erősen előreszóró");
        }

        [Fact]
        public void PhaseFunctionNormalized_IsIsotropicUnitScaledAndCapped()
        {
            // Izotróp = 1 a normálás definíciója szerint: 4π·(1/4π) = 1.
            // Itt g fix, ezért a 90 fokos szórás közelítőleg egységnyi
            // nagyságrendben van, a csúcs pedig vágott.
            Assert.Equal(CloudVolume.PhasePeakCap, CloudVolume.PhaseNormalized(1.0, CloudVolume.ForwardScatterG), 12);
            double back = CloudVolume.PhaseNormalized(-1.0, CloudVolume.ForwardScatterG);
            Assert.InRange(back, 0.0, 1.0);
            Assert.True(CloudVolume.PhaseNormalized(0.0, CloudVolume.ForwardScatterG) > back);
        }

        // ------------------------------------------------------------------
        // Felhőárnyék
        // ------------------------------------------------------------------

        [Fact]
        public void SurfaceSunlightFactor_WithoutCoverage_IsExactlyOne()
        {
            // Ez az I3/„kikapcsolva bitre a korábbi kép” garancia CPU-oldali párja.
            Assert.Equal(1.0, CloudVolume.SurfaceSunlightFactor(0.0, 5000.0, 1.0));
            Assert.Equal(1.0, CloudVolume.SurfaceSunlightFactor(-1.0, 5000.0, 1.0));
            Assert.Equal(0.0, CloudVolume.ShadowOpticalDepth(0.0, 5000.0, 1.0));
            Assert.Equal(0.0, CloudVolume.ShadowOpticalDepth(1.0, 0.0, 1.0));
        }

        [Fact]
        public void SurfaceSunlightFactor_FullCoverage_FallsToTheOvercastFloor()
        {
            double factor = CloudVolume.SurfaceSunlightFactor(1.0, 5000.0, 1.0);
            // τ = 0,02·1·0,7·5000 = 70 → az áteresztés gyakorlatilag nulla,
            // tehát a szorzó a diffúz padló.
            Assert.Equal(CloudVolume.OvercastDiffuseTransmission, factor, 6);
        }

        [Fact]
        public void SurfaceSunlightFactor_DecreasesWithCoverage()
        {
            double previous = 1.0;
            for (int i = 1; i <= 20; i++)
            {
                double f = CloudVolume.SurfaceSunlightFactor(i / 20.0, 2000.0, 0.8);
                Assert.True(f < previous, "több felhő nem lehet világosabb");
                Assert.InRange(f, 0.0, 1.0);
                previous = f;
            }
        }

        [Fact]
        public void ShadowOpticalDepth_GrowsAsTheSunGetsLowerAndClampsAtGrazing()
        {
            double high = CloudVolume.ShadowOpticalDepth(0.5, 1000.0, 1.0);
            double low = CloudVolume.ShadowOpticalDepth(0.5, 1000.0, 0.3);
            Assert.True(low > high, "lapos beesésnél hosszabb a felhőben megtett út");
            // A vágás alatt már nem növekszik tovább (a sík-párhuzamos
            // közelítés ott elveszti az értelmét).
            Assert.Equal(
                CloudVolume.ShadowOpticalDepth(0.5, 1000.0, 0.15),
                CloudVolume.ShadowOpticalDepth(0.5, 1000.0, 0.001), 12);
        }

        // ------------------------------------------------------------------
        // Részlet-létra
        // ------------------------------------------------------------------

        [Fact]
        public void SubGridCoverage_OnZeroCoverage_IsExactlyZeroForEveryNoiseValue()
        {
            // AZ I3-GARANCIA: a cellán belüli szétbontás FORMÁZ, nem TEREMT felhőt.
            for (int i = 0; i <= 20; i++)
                Assert.Equal(0.0, CloudVolume.SubGridCoverage(0.0, i / 20.0));
            Assert.Equal(0.0, CloudVolume.SubGridCoverage(-0.5, 0.1));
            Assert.Equal(0.0, CloudVolume.SubGridCoverage(double.NaN, 0.1));
            Assert.Equal(0.0, CloudVolume.SubGridCoverage(0.5, double.NaN));
        }

        [Fact]
        public void SubGridCoverage_FullCoverage_IsFullEverywhere()
        {
            for (int i = 0; i <= 20; i++)
                Assert.Equal(1.0, CloudVolume.SubGridCoverage(1.0, i / 20.0));
        }

        [Fact]
        public void SubGridCoverage_PreservesTheModelledCellMean()
        {
            // EZ A LÉNYEG: a szétbontás a cellán BELÜLI eloszlást adja meg, de
            // a zajra vett VÁRHATÓ ÉRTÉKE a modellezett cella-átlag marad -
            // tehát a modell mennyisége nem vész el és nem is nő.
            const int samples = 20000;
            for (int k = 1; k <= 9; k++)
            {
                double coverage = k / 10.0;
                double sum = 0.0;
                for (int i = 0; i < samples; i++)
                    sum += CloudVolume.SubGridCoverage(coverage, (i + 0.5) / samples);
                double mean = sum / samples;
                Assert.True(Math.Abs(mean - coverage) < 0.01,
                    $"lefedettség {coverage}: a sub-grid átlag {mean:F4} eltér a modellezett értéktől");
            }
        }

        [Fact]
        public void SubGridCoverage_MeanIsContinuousDownToZero()
        {
            // A nulla környékén az eltolást visszasimítjuk (különben a nulla
            // lefedettség sem maradna egzaktul üres) - az átlag ott alulról
            // közelít, ugrás nélkül.
            const int samples = 4000;
            double previous = -1.0;
            for (int k = 0; k <= 40; k++)
            {
                double coverage = k / 40.0 * CloudVolume.SubGridEdgeWidth * 2.0;
                double sum = 0.0;
                for (int i = 0; i < samples; i++)
                    sum += CloudVolume.SubGridCoverage(coverage, (i + 0.5) / samples);
                double mean = sum / samples;
                Assert.InRange(mean, 0.0, coverage + 0.02);
                Assert.True(mean >= previous - 1e-9, "az átlag monoton nő a lefedettséggel");
                previous = mean;
            }
        }

        [Fact]
        public void SubGridCoverage_IsMonotoneInBothArguments()
        {
            for (int n = 0; n <= 10; n++)
            {
                double previous = -1.0;
                for (int c = 0; c <= 10; c++)
                {
                    double v = CloudVolume.SubGridCoverage(c / 10.0, n / 10.0);
                    Assert.InRange(v, 0.0, 1.0);
                    Assert.True(v >= previous, "több lefedettség nem adhat kevesebb felhőt");
                    previous = v;
                }
            }
            for (int c = 1; c <= 10; c++)
            {
                double previous = 2.0;
                for (int n = 0; n <= 10; n++)
                {
                    double v = CloudVolume.SubGridCoverage(c / 10.0, n / 10.0);
                    Assert.True(v <= previous, "nagyobb zaj-érték nem adhat több felhőt");
                    previous = v;
                }
            }
        }

        [Fact]
        public void DaylightFactor_DarkensTheNightSideWithATwilightBand()
        {
            // A MERT HIBA: a Nap iranyu optikai melyseg nem tudja, hogy a Nap
            // a horizont alatt van-e, ezert az ejszakai oldal felhoi is
            // teljes napfenyt kaptak es feheren vilagitottak.
            Assert.Equal(0.0, CloudVolume.DaylightFactor(-1.0));
            Assert.Equal(0.0, CloudVolume.DaylightFactor(-CloudVolume.TwilightBandCos));
            Assert.Equal(1.0, CloudVolume.DaylightFactor(CloudVolume.TwilightBandCos));
            Assert.Equal(1.0, CloudVolume.DaylightFactor(1.0));
            Assert.Equal(0.5, CloudVolume.DaylightFactor(0.0), 12);
            Assert.Equal(0.0, CloudVolume.DaylightFactor(double.NaN));
        }

        [Fact]
        public void DaylightFactor_IsMonotone()
        {
            double previous = -1.0;
            for (int i = -20; i <= 20; i++)
            {
                double d = CloudVolume.DaylightFactor(i / 200.0);
                Assert.InRange(d, 0.0, 1.0);
                Assert.True(d >= previous, "magasabban allo Nap nem adhat kevesebb fenyt");
                previous = d;
            }
        }

        [Fact]
        public void SunScatterGain_KeepsThickCloudsLit()
        {
            // A MERT HIBA: tisztan egyszeres szorassal a dekk belsejeben
            // exp(-tau) ~ 0, ezert a felho SOTET SZURKE folt lett. A magasabb
            // oktavok csokkentett kioltast latnak, tehat nagy optikai
            // melysegnel is marad feny.
            double single = CloudVolume.Transmittance(6.0);
            double gain = CloudVolume.SunScatterGain(6.0, -1.0);
            Assert.True(single < 0.01, $"egyszeres szoras ennyi: {single}");
            Assert.True(gain > 20.0 * single, $"a tobbszoros szorasnak nagysagrenddel tobbet kell adnia: {gain}");
            Assert.True(gain > 0.2, $"az optikailag vastag felho sem lehet fekete: {gain}");
        }

        [Fact]
        public void SunScatterGain_DecreasesWithDepth()
        {
            double previous = double.MaxValue;
            for (int i = 0; i <= 40; i++)
            {
                double g = CloudVolume.SunScatterGain(i * 0.5, 0.0);
                Assert.True(g <= previous + 1e-12, "melyebbre hatolva nem nohet a szort feny");
                Assert.True(g > 0.0);
                previous = g;
            }
            Assert.True(CloudVolume.SunScatterGain(0.0, 0.0) > CloudVolume.SunScatterGain(10.0, 0.0));
        }

        [Fact]
        public void SunScatterGain_ZeroDepth_IsTheOctaveSumOfThePhase()
        {
            double expected = 0.0;
            double energy = 1.0, ecc = 1.0;
            for (int k = 0; k < CloudVolume.MultiScatterOctaves; k++)
            {
                expected += energy * CloudVolume.PhaseNormalized(0.3, CloudVolume.ForwardScatterG * ecc);
                energy *= CloudVolume.OctaveEnergy;
                ecc *= CloudVolume.OctaveEccentricity;
            }
            Assert.Equal(expected, CloudVolume.SunScatterGain(0.0, 0.3), 12);
            Assert.Equal(expected, CloudVolume.SunScatterGain(-5.0, 0.3), 12);
        }

        [Fact]
        public void SunScatterGain_StillFavoursTheSunwardSide()
        {
            Assert.True(CloudVolume.SunScatterGain(2.0, 1.0) > CloudVolume.SunScatterGain(2.0, -1.0),
                "a Nap fele nezett perem tovabbra is fenyesebb");
        }

        [Fact]
        public void PhaseNormalized_IsIsotropicAtZeroAsymmetry()
        {
            for (int i = -10; i <= 10; i++)
                Assert.Equal(1.0, CloudVolume.PhaseNormalized(i / 10.0, 0.0), 12);
        }

        [Fact]
        public void DetailBaseFrequency_IsOneCyclePerAtlasCell()
        {
            // A kockalap éle π/2 radián, egy élen 2^level cella → a
            // "ciklus/atlasz-cella" frekvencia 2^level·2/π.
            Assert.Equal(64.0 * 2.0 / Math.PI, CloudVolume.DetailBaseFrequency(6), 9);
            Assert.Equal(40.743665431, CloudVolume.DetailBaseFrequency(6), 6);
            // Szintenként duplázódik - a létra oktávokban lép.
            for (int level = 0; level < 10; level++)
                Assert.Equal(2.0 * CloudVolume.DetailBaseFrequency(level), CloudVolume.DetailBaseFrequency(level + 1), 9);
            Assert.Equal(CloudVolume.DetailBaseFrequency(0), CloudVolume.DetailBaseFrequency(-5), 12);
        }

        [Fact]
        public void Advect_IsAnIsometry_AndDoesNotSaturate()
        {
            // EZ A LÉNYEG (MÉRT hiba javitása): a modell beépített sodródása
            // (hozzáadás + újranormálás) π/2-nél TELÍTŐDIK, tehát hosszú
            // távon megáll. A forgatás nem: tetszőleges szögre értelmes és
            // hossztartó.
            double[] px = { 1, 0, 0, 0.577, -0.3 };
            double[] py = { 0, 1, 0, 0.577, 0.9 };
            double[] pz = { 0, 0, 1, 0.577, 0.3162 };
            foreach (double radians in new[] { 0.0, 0.3, 1.5, 3.0, 10.0, 100.0 })
            {
                for (int i = 0; i < px.Length; i++)
                {
                    double len0 = Math.Sqrt(px[i] * px[i] + py[i] * py[i] + pz[i] * pz[i]);
                    CloudVolume.Advect(px[i], py[i], pz[i], radians, out double ax, out double ay, out double az);
                    double len1 = Math.Sqrt(ax * ax + ay * ay + az * az);
                    Assert.Equal(len0, len1, 9);
                }
            }
        }

        [Fact]
        public void Advect_ZeroAngleIsTheIdentityAndTheAxisIsFixed()
        {
            CloudVolume.Advect(0.3, 0.4, 0.866, 0.0, out double ax, out double ay, out double az);
            Assert.Equal(0.3, ax, 9);
            Assert.Equal(0.4, ay, 9);
            Assert.Equal(0.866, az, 9);
            // A tengely maga fixpont.
            CloudVolume.Advect(CloudVolume.AdvectionAxisX, CloudVolume.AdvectionAxisY, CloudVolume.AdvectionAxisZ,
                1.234, out double bx, out double by, out double bz);
            Assert.Equal(CloudVolume.AdvectionAxisX, bx, 9);
            Assert.Equal(CloudVolume.AdvectionAxisY, by, 9);
            Assert.Equal(CloudVolume.AdvectionAxisZ, bz, 9);
        }

        [Fact]
        public void Advect_KeepsMovingWhereTheModelsDriftWouldHaveStalled()
        {
            // A tengelyre MERŐLEGES pont a legtöbbet mozduló. Két nagy szög
            // között is ÉRDEMI az elmozdulás - a telitődő változat itt már
            // állna.
            double a = 30.0, b = 30.0 + CloudVolume.WeatherAdvectionRadiansPerDay;
            CloudVolume.Advect(0, 0, 1, a, out double ax, out double ay, out double az);
            CloudVolume.Advect(0, 0, 1, b, out double bx, out double by, out double bz);
            double dot = ax * bx + ay * by + az * bz;
            double angle = Math.Acos(Math.Min(1.0, Math.Max(-1.0, dot)));
            Assert.Equal(CloudVolume.WeatherAdvectionRadiansPerDay, angle, 6);
        }

        [Fact]
        public void AdvectionSpeed_IsTheDocumentedJetStreamScale()
        {
            // 0,3 rad/nap a 7420 km-es sugaron ~2225 km/nap = 25,8 m/s.
            double kmPerDay = CloudVolume.WeatherAdvectionRadiansPerDay * (PlanetConstants.RadiusMeters / 1000.0);
            Assert.InRange(kmPerDay, 2100.0, 2350.0);
            double metresPerSecond = kmPerDay * 1000.0 / 86400.0;
            Assert.InRange(metresPerSecond, 24.0, 28.0);
        }

        [Fact]
        public void DetailPhase_IsPureAndSeedDependent()
        {
            CloudVolume.DetailPhase(12345UL, out double x1, out double y1, out double z1);
            CloudVolume.DetailPhase(12345UL, out double x2, out double y2, out double z2);
            Assert.Equal(x1, x2);
            Assert.Equal(y1, y2);
            Assert.Equal(z1, z2);

            CloudVolume.DetailPhase(12346UL, out double x3, out double y3, out double z3);
            Assert.True(x1 != x3 || y1 != y3 || z1 != z3, "más seed más fázist ad");
        }

        [Fact]
        public void DetailPhase_StaysInTheDocumentedRange()
        {
            for (ulong seed = 0; seed < 64; seed++)
            {
                CloudVolume.DetailPhase(seed, out double x, out double y, out double z);
                Assert.InRange(x, 0.0, 1024.0);
                Assert.InRange(y, 0.0, 1024.0);
                Assert.InRange(z, 0.0, 1024.0);
            }
        }

        [Fact]
        public void DetailPhase_DiffersFromTheMicroDetailPhase()
        {
            // A két dekoratív fázis KÜLÖN RandomProperty-t használ (43 vs 44);
            // ha valaki újrahasznosítaná az azonosítót, a felhő és a felszíni
            // mikro-részlet mintázata korrelálna.
            CloudVolume.DetailPhase(99UL, out double cx, out double cy, out double cz);
            SurfaceMicroDetail.PhaseOffset(99UL, out double mx, out double my, out double mz);
            Assert.True(cx != mx || cy != my || cz != mz);
        }

        [Fact]
        public void DetailLadder_MatchesTheModelsOwnNoiseParameters()
        {
            Assert.Equal(FractalNoise.DefaultPersistence, CloudVolume.DetailPersistence);
            Assert.Equal(FractalNoise.DefaultLacunarity, CloudVolume.DetailLacunarity);
            Assert.Equal(4, CloudVolume.DetailOctaves);
        }

        // ------------------------------------------------------------------
        // Élesetek
        // ------------------------------------------------------------------

        [Fact]
        public void EdgeCases_DoNotProduceNaNOrOutOfRangeValues()
        {
            double[] wild = { 0.0, -0.0, 1e-300, 1e300, double.MaxValue, -double.MaxValue, 1.0, -1.0 };
            foreach (double v in wild)
            {
                Assert.InRange(CloudVolume.RelativeHumidityPercent(v),
                    CloudVolume.RelativeHumidityDryPercent, CloudVolume.RelativeHumidityWetPercent);
                Assert.InRange(CloudVolume.CloudBaseAboveSeaLevelMeters(v, v), 0.0, CloudVolume.MaxBaseAboveSeaLevelMeters);
                Assert.InRange(CloudVolume.CloudThicknessMeters(v, v), 0.0, CloudVolume.MaxThicknessMeters);
                Assert.False(double.IsNaN(CloudVolume.VerticalProfile(v)));
                Assert.False(double.IsNaN(CloudVolume.Transmittance(v)));
                Assert.InRange(CloudVolume.SurfaceSunlightFactor(v, 1000.0, 0.5), 0.0, 1.0);
            }
        }

        [Fact]
        public void NaNCoverage_DoesNotLeakIntoTheShellGeometry()
        {
            // A NaN nem várt bemenet (a csapadék-mező sosem ad ilyet), de egy
            // NaN sugár a GPU-n MINDENT megfertőzne, ezért a vágások
            // viselkedését rögzítjük.
            Assert.Equal(0.0, CloudVolume.ShapeCoverage(double.NaN, 1.0, 2.0, 1.3));
            Assert.Equal(0.0, CloudVolume.SubGridCoverage(double.NaN, 0.5));
            Assert.Equal(0.0, CloudVolume.SubGridCoverage(0.5, double.NaN));
        }
    }
}
