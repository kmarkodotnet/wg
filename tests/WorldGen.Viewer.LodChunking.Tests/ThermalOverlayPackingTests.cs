using System;
using WorldGen.Core.Grid;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests;

public class ThermalOverlayPackingTests
{
    private const int W = ThermalOverlayPacking.AtlasWidth;
    private const int F = ThermalOverlayPacking.FaceTexels;
    private const int S = ThermalOverlayPacking.Side;

    [Fact]
    public void InteriorTexelsMapToTheirOwnCell()
    {
        int[] map = ThermalOverlayPacking.BuildTexelSourceMap();
        Assert.Equal(W * ThermalOverlayPacking.AtlasHeight, map.Length);
        for (int face = 0; face < 6; face++)
            for (int u = 0; u < S; u++)
                for (int v = 0; v < S; v++)
                    Assert.Equal(DenseGridMetrics.Index(face, u, v, S), map[(v + 1) * W + face * F + u + 1]);
    }

    [Fact]
    public void GutterTexelsMapToTopologicalNeighbours()
    {
        int[] map = ThermalOverlayPacking.BuildTexelSourceMap();
        for (int face = 0; face < 6; face++)
        {
            for (int k = 0; k < S; k++)
            {
                AssertNeighbour(map[(k + 1) * W + face * F + 0], face, 0, k, TileDirection.Left);
                AssertNeighbour(map[(k + 1) * W + face * F + F - 1], face, S - 1, k, TileDirection.Right);
                AssertNeighbour(map[0 * W + face * F + k + 1], face, k, 0, TileDirection.Down);
                AssertNeighbour(map[(F - 1) * W + face * F + k + 1], face, k, S - 1, TileDirection.Up);
            }
        }
        Assert.All(map, c => Assert.InRange(c, 0, 6 * S * S - 1));
    }

    private static void AssertNeighbour(int mapped, int face, int u, int v, TileDirection direction)
    {
        TileId nb = TileNeighbors.Neighbor(TileId.FromFaceLevelUV(face, ThermalOverlayPacking.Level, (uint)u, (uint)v), direction);
        nb.GetUV(out uint nu, out uint nv);
        Assert.NotEqual(face, nb.Face);
        Assert.Equal(DenseGridMetrics.Index(nb.Face, (int)nu, (int)nv, S), mapped);
    }

    [Fact]
    public void QuantizationRoundTripsAndClamps()
    {
        foreach (double k in new[] { 150.0, 200.004, 273.15, 288.126, 330.0, 805.35 })
            Assert.InRange(ThermalOverlayPacking.Decode(ThermalOverlayPacking.Quantize(k)) - k, -0.005 - 1e-9, 0.005 + 1e-9);
        Assert.Equal(0, ThermalOverlayPacking.Quantize(10.0));
        Assert.Equal(ushort.MaxValue, ThermalOverlayPacking.Quantize(2000.0));
        Assert.Equal(0, ThermalOverlayPacking.Quantize(double.NaN));
        Assert.Equal(12315, ThermalOverlayPacking.Quantize(273.15));
    }

    [Fact]
    public void PackIsByteReproducible()
    {
        int[] map = ThermalOverlayPacking.BuildTexelSourceMap();
        var values = new double[6 * S * S];
        for (int i = 0; i < values.Length; i++) values[i] = 220.0 + (i % 997) * 0.1;
        var a = new ushort[map.Length];
        var b = new ushort[map.Length];
        ThermalOverlayPacking.Pack(map, values, a);
        ThermalOverlayPacking.Pack(map, values, b);
        Assert.Equal(a, b);
        Assert.Throws<ArgumentException>(() => ThermalOverlayPacking.Pack(map, values, new ushort[10]));
    }

    [Fact]
    public void AtlasCoordinateOfCellCentreHitsTexelCentre()
    {
        for (int face = 0; face < 6; face++)
        {
            foreach ((int u, int v) in new[] { (0, 0), (5, 40), (31, 32), (63, 63), (0, 63), (63, 0) })
            {
                TileGeometry.ToPosition(TileId.FromFaceLevelUV(face, ThermalOverlayPacking.Level, (uint)u, (uint)v),
                    out double x, out double y, out double z);
                ThermalOverlayPacking.AtlasCoordinate(x, y, z, out int gotFace, out double au, out double av);
                Assert.Equal(face, gotFace);
                Assert.InRange(au * W - (face * F + u + 1.5), -1e-9, 1e-9);
                Assert.InRange(av * ThermalOverlayPacking.AtlasHeight - (v + 1.5), -1e-9, 1e-9);
            }
        }
        Assert.Throws<ArgumentException>(() => ThermalOverlayPacking.AtlasCoordinate(0, 0, 0, out _, out _, out _));
    }

    [Fact]
    public void PaletteHasFixedAnchorsAroundZeroCelsius()
    {
        const double min = 213.15, max = 323.15;
        ThermalOverlayPacking.Palette(273.15, min, max, out float r0, out float g0, out float b0);
        Assert.Equal(0.93f, r0, 3); Assert.Equal(0.93f, g0, 3); Assert.Equal(0.89f, b0, 3);
        ThermalOverlayPacking.Palette(min - 50.0, min, max, out float rc, out _, out float bc);
        Assert.Equal(0.08f, rc, 3); Assert.Equal(0.62f, bc, 3);
        ThermalOverlayPacking.Palette(max + 50.0, min, max, out float rw, out float gw, out _);
        Assert.Equal(0.78f, rw, 3); Assert.Equal(0.12f, gw, 3);
        ThermalOverlayPacking.Palette(300.0, min, max, out float rm, out _, out float bm);
        Assert.True(rm > bm, "a meleg oldal pirosabb, mint kék");
    }
}
