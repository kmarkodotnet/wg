using System;
using System.Collections.Generic;
using WorldGen.Core.Events;
using WorldGen.Core.Grid;
using WorldGen.Core.Tectonics;
using WorldGen.Core.Terrain;
using Xunit;

namespace WorldGen.Core.Tests.Tectonics;

public class TerrainPointBasisTests
{
    [Fact]
    public void CachedBasisMatchesOriginalComponentsBitExactly()
    {
        ulong[] worldSeeds = { 1UL, 0xA7C944210000UL, 184482873278464UL };
        double[] timesMyr = { 0.0, 17.25, 500.0 };

        foreach (ulong worldSeed in worldSeeds)
        {
            var originalSeeds = PlateGeneration.GenerateSeeds(worldSeed, 20);
            foreach (double timeMyr in timesMyr)
            {
                var seeds = PlateMotion.MovedSeeds(worldSeed, originalSeeds, timeMyr);
                for (int i = 0; i < 72; i++)
                {
                    TileId id = TileId.FromFaceLevelUV(i % 6, 8, (uint)(i * 37 % 256), (uint)(i * 83 % 256));
                    TileGeometry.ToPosition(id, out double x, out double y, out double z);

                    DomainWarp.WarpPosition(worldSeed, x, y, z, out double wx, out double wy, out double wz);
                    int plateId = PlateGeneration.AssignPlate(wx, wy, wz, seeds);
                    double expectedBase = CrustElevation.BaseElevation(worldSeed, plateId, x, y, z, out bool expectedOceanic);
                    double expectedUplift = PlateBoundaryEffect.BoundaryUpliftFromWarped(
                        worldSeed, x, y, z, wx, wy, wz, seeds);

                    TerrainPointBasis basis = TerrainPointBasis.Compute(worldSeed, x, y, z);
                    basis.Evaluate(worldSeed, seeds, out double actualBase, out double actualUplift, out bool actualOceanic);

                    Assert.Equal(expectedOceanic, actualOceanic);
                    Assert.Equal(BitConverter.DoubleToInt64Bits(expectedBase), BitConverter.DoubleToInt64Bits(actualBase));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(expectedUplift), BitConverter.DoubleToInt64Bits(actualUplift));
                }
            }
        }
    }

    [Fact]
    public void CachedBasisIsPure()
    {
        const ulong worldSeed = 99UL;
        TerrainPointBasis a = TerrainPointBasis.Compute(worldSeed, 0.3, 0.4, 0.8660254037844386);
        TerrainPointBasis b = TerrainPointBasis.Compute(worldSeed, 0.3, 0.4, 0.8660254037844386);

        Assert.Equal(BitConverter.DoubleToInt64Bits(a.WarpedX), BitConverter.DoubleToInt64Bits(b.WarpedX));
        Assert.Equal(BitConverter.DoubleToInt64Bits(a.WarpedY), BitConverter.DoubleToInt64Bits(b.WarpedY));
        Assert.Equal(BitConverter.DoubleToInt64Bits(a.WarpedZ), BitConverter.DoubleToInt64Bits(b.WarpedZ));
        Assert.Equal(BitConverter.DoubleToInt64Bits(a.PrimaryNoise), BitConverter.DoubleToInt64Bits(b.PrimaryNoise));
        Assert.Equal(BitConverter.DoubleToInt64Bits(a.MountainMask), BitConverter.DoubleToInt64Bits(b.MountainMask));
        Assert.Equal(BitConverter.DoubleToInt64Bits(a.SecondaryNoise), BitConverter.DoubleToInt64Bits(b.SecondaryNoise));
    }

    [Fact]
    public void CachedCombinedFieldOrderMatchesOriginalPipelineBitExactly()
    {
        const ulong worldSeed = 0xA7C944210000UL;
        const double timeMyr = 500.0;
        var originalSeeds = PlateGeneration.GenerateSeeds(worldSeed, 20);
        var seeds = PlateMotion.MovedSeeds(worldSeed, originalSeeds, timeMyr);

        for (int i = 0; i < 48; i++)
        {
            TileId id = TileId.FromFaceLevelUV(i % 6, 8, (uint)(i * 41 % 256), (uint)(i * 97 % 256));
            TileGeometry.ToPosition(id, out double x, out double y, out double z);
            var craters = new List<ImpactCratering.CraterRecord>
            {
                new ImpactCratering.CraterRecord(x, y, z, 0.9999, 350.0)
            };

            DomainWarp.WarpPosition(worldSeed, x, y, z, out double wx, out double wy, out double wz);
            int plateId = PlateGeneration.AssignPlate(wx, wy, wz, seeds);
            double expected = PlateBoundaryEffect.ElevationWithBoundaryFromWarped(
                worldSeed, plateId, id.Value, x, y, z, wx, wy, wz, seeds, out _);
            expected += ImpactCratering.ElevationDelta(x, y, z, craters);
            double originalUplift = PlateBoundaryEffect.BoundaryUpliftFromWarped(
                worldSeed, x, y, z, wx, wy, wz, seeds);
            double relaxedOriginalUplift = DeepTimeErosionGlaciation.UpliftRelaxationElevation(
                originalUplift, timeMyr);
            expected += relaxedOriginalUplift - originalUplift;

            TerrainPointBasis basis = TerrainPointBasis.Compute(worldSeed, x, y, z);
            basis.Evaluate(worldSeed, seeds, out double baseElevation, out double uplift, out _);
            double actual = baseElevation + uplift;
            actual += ImpactCratering.ElevationDelta(x, y, z, craters);
            double relaxedUplift = DeepTimeErosionGlaciation.UpliftRelaxationElevation(uplift, timeMyr);
            actual += relaxedUplift - uplift;

            Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
        }
    }
}
