using System;
using WorldGen.Core.Climate;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests
{
    /// <summary>
    /// ND-154/ND-155: a felhő- és égbolt-atlasz csomagolásának tesztjei.
    /// A LEGFONTOSABB az, hogy a NULLA lefedettség EGZAKTUL nulla byte-ra
    /// kvantálódjon: a shader I3-garanciája (nincs felhő, ahol a modell szerint
    /// nincs) a kvantáláson át vezet.
    /// </summary>
    public class CloudSkyAtlasTests
    {
        [Fact]
        public void AtlasGeometry_IsSharedWithTheThermalOverlay()
        {
            // Ugyanaz a rács és ugyanaz a texel→cella leképezés - ezt a
            // shader AtlasUv képlete is feltételezi (PlanetCubeAtlas.cginc).
            Assert.Equal(ThermalOverlayPacking.Level, CloudSkyAtlas.Level);
            Assert.Equal(ThermalOverlayPacking.Side, CloudSkyAtlas.Side);
            Assert.Equal(ThermalOverlayPacking.FaceTexels, CloudSkyAtlas.FaceTexels);
            Assert.Equal(396, CloudSkyAtlas.AtlasWidth);
            Assert.Equal(66, CloudSkyAtlas.AtlasHeight);
            Assert.Equal(4, CloudSkyAtlas.Channels);
        }

        [Fact]
        public void QuantizeUnit_MapsTheEndpointsExactly()
        {
            Assert.Equal(0, CloudSkyAtlas.QuantizeUnit(0.0));
            Assert.Equal(0, CloudSkyAtlas.QuantizeUnit(-1.0));
            Assert.Equal(0, CloudSkyAtlas.QuantizeUnit(double.NaN));
            Assert.Equal(255, CloudSkyAtlas.QuantizeUnit(1.0));
            Assert.Equal(255, CloudSkyAtlas.QuantizeUnit(3.0));
        }

        [Fact]
        public void QuantizeUnit_RoundTripsWithinHalfAStep()
        {
            for (int i = 0; i <= 1000; i++)
            {
                double v = i / 1000.0;
                double decoded = CloudSkyAtlas.DecodeUnit(CloudSkyAtlas.QuantizeUnit(v));
                Assert.True(Math.Abs(decoded - v) <= 0.5 / 255.0 + 1e-12, $"{v} → {decoded}");
            }
        }

        [Fact]
        public void QuantizeUnit_IsMonotone()
        {
            int previous = -1;
            for (int i = 0; i <= 2000; i++)
            {
                int q = CloudSkyAtlas.QuantizeUnit(i / 2000.0);
                Assert.True(q >= previous);
                previous = q;
            }
        }

        [Fact]
        public void QuantizeMeters_NormalisesToTheCeiling()
        {
            Assert.Equal(0, CloudSkyAtlas.QuantizeMeters(0.0, 12000.0));
            Assert.Equal(255, CloudSkyAtlas.QuantizeMeters(12000.0, 12000.0));
            Assert.Equal(255, CloudSkyAtlas.QuantizeMeters(99999.0, 12000.0));
            Assert.Equal(0, CloudSkyAtlas.QuantizeMeters(500.0, 0.0));
            // A METRIKUS felbontás: 12 km / 255 ≈ 47 m lépés.
            double decoded = CloudSkyAtlas.DecodeUnit(CloudSkyAtlas.QuantizeMeters(6000.0, 12000.0)) * 12000.0;
            Assert.True(Math.Abs(decoded - 6000.0) < 24.0, $"{decoded}");
        }

        [Fact]
        public void BuildChannels_DerivesBaseAndThicknessFromTheCore()
        {
            var coverage = new double[] { 0.0, 0.5, 1.0 };
            var elevation = new double[] { 500.0, 3500.0, -2000.0 };
            var baseM = new double[3];
            var thickM = new double[3];
            CloudSkyAtlas.BuildChannels(coverage, elevation, 500.0, baseM, thickM);

            // A TALAJ a tengerszinthez mért magasság (itt: 0, 3000, negatív→0).
            Assert.Equal(CloudVolume.CloudBaseAboveSeaLevelMeters(0.0, 0.0), baseM[0], 9);
            Assert.Equal(CloudVolume.CloudBaseAboveSeaLevelMeters(0.5, 3000.0), baseM[1], 9);
            Assert.Equal(CloudVolume.CloudBaseAboveSeaLevelMeters(1.0, 0.0), baseM[2], 9);
            Assert.Equal(CloudVolume.CloudThicknessMeters(0.5, 3000.0), thickM[1], 9);
        }

        [Fact]
        public void BuildChannels_ValidatesItsArguments()
        {
            var three = new double[3];
            Assert.Throws<ArgumentNullException>(() => CloudSkyAtlas.BuildChannels(null!, three, 0.0, three, three));
            Assert.Throws<ArgumentNullException>(() => CloudSkyAtlas.BuildChannels(three, null!, 0.0, three, three));
            Assert.Throws<ArgumentException>(() => CloudSkyAtlas.BuildChannels(three, new double[2], 0.0, three, three));
        }

        [Fact]
        public void Pack_PlacesTheFourChannelsInOrder()
        {
            int[] map = ThermalOverlayPacking.BuildTexelSourceMap();
            int cells = CloudSkyAtlas.Side * CloudSkyAtlas.Side * 6;
            var coverage = new double[cells];
            var baseM = new double[cells];
            var thickM = new double[cells];
            var openness = new double[cells];
            for (int c = 0; c < cells; c++)
            {
                coverage[c] = 1.0;
                baseM[c] = CloudVolume.MaxBaseAboveSeaLevelMeters;
                thickM[c] = 0.0;
                openness[c] = 0.0;
            }

            var target = new byte[map.Length * CloudSkyAtlas.Channels];
            CloudSkyAtlas.Pack(map, coverage, baseM, thickM, openness, target);
            for (int i = 0; i < map.Length; i++)
            {
                Assert.Equal(255, target[i * 4]);     // R = lefedettség
                Assert.Equal(255, target[i * 4 + 1]); // G = alj
                Assert.Equal(0, target[i * 4 + 2]);   // B = vastagság
                Assert.Equal(0, target[i * 4 + 3]);   // A = nyitottság
            }
        }

        [Fact]
        public void Pack_ZeroCoverage_StaysExactlyZero()
        {
            // AZ I3-GARANCIA a kvantáláson át: ha a modell szerint nincs
            // csapadék, a shader EGZAKTUL nulla lefedettséget lát, tehát a
            // részlet-zaj sem tud felhőt teremteni.
            int[] map = ThermalOverlayPacking.BuildTexelSourceMap();
            int cells = CloudSkyAtlas.Side * CloudSkyAtlas.Side * 6;
            var zero = new double[cells];
            var target = new byte[map.Length * CloudSkyAtlas.Channels];
            CloudSkyAtlas.Pack(map, zero, zero, zero, zero, target);
            for (int i = 0; i < map.Length; i++)
                Assert.Equal(0, target[i * 4]);
        }

        [Fact]
        public void Pack_GutterTexelsTakeTheNeighbourFaceValue()
        {
            // A gutter (perem) helyes kitöltése a ThermalOverlayPacking
            // már tesztelt leképezéséből jön; itt azt rögzítjük, hogy a
            // felhő-atlasz UGYANAZT használja, tehát a kockalap-éleken a
            // bilineáris szűrés nem kever idegen értéket.
            int[] map = ThermalOverlayPacking.BuildTexelSourceMap();
            int cells = CloudSkyAtlas.Side * CloudSkyAtlas.Side * 6;
            var coverage = new double[cells];
            for (int c = 0; c < cells; c++)
                coverage[c] = (c % 255) / 255.0;

            var target = new byte[map.Length * CloudSkyAtlas.Channels];
            CloudSkyAtlas.Pack(map, coverage, null!, null!, null!, target);
            for (int i = 0; i < map.Length; i++)
                Assert.Equal(CloudSkyAtlas.QuantizeUnit(coverage[map[i]]), target[i * 4]);
        }

        [Fact]
        public void Pack_ValidatesTheTargetSize()
        {
            int[] map = ThermalOverlayPacking.BuildTexelSourceMap();
            int cells = CloudSkyAtlas.Side * CloudSkyAtlas.Side * 6;
            var zero = new double[cells];
            Assert.Throws<ArgumentNullException>(() => CloudSkyAtlas.Pack(null!, zero, zero, zero, zero, new byte[4]));
            Assert.Throws<ArgumentNullException>(() => CloudSkyAtlas.Pack(map, zero, zero, zero, zero, null!));
            Assert.Throws<ArgumentException>(() => CloudSkyAtlas.Pack(new int[7], zero, zero, zero, zero, new byte[28]));
            Assert.Throws<ArgumentException>(() => CloudSkyAtlas.Pack(map, zero, zero, zero, zero, new byte[map.Length]));
        }
    }
}
