using System;
using System.IO;
using WorldGen.Cli;
using Xunit;

namespace WorldGen.Cli.Tests;

public class WorldPackageTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;

    [Fact]
    public void CreateIsPure()
    {
        WorldPackage a = WorldPackage.Create(WorldSeed, PlateCount, Level, 50.0);
        WorldPackage b = WorldPackage.Create(WorldSeed, PlateCount, Level, 50.0);
        Assert.Equal(a.StateHashHex, b.StateHashHex);
    }

    [Fact]
    public void DifferentTimeGivesDifferentHash()
    {
        WorldPackage a = WorldPackage.Create(WorldSeed, PlateCount, Level, 0.0);
        WorldPackage b = WorldPackage.Create(WorldSeed, PlateCount, Level, 50.0);
        Assert.NotEqual(a.StateHashHex, b.StateHashHex);
    }

    [Fact]
    public void SaveThenLoadRoundTripsAllFields()
    {
        WorldPackage original = WorldPackage.Create(WorldSeed, PlateCount, Level, 12.5);
        string path = Path.GetTempFileName();
        try
        {
            original.Save(path);
            WorldPackage loaded = WorldPackage.Load(path);

            Assert.Equal(original.FormatVersion, loaded.FormatVersion);
            Assert.Equal(2, loaded.FormatVersion);
            Assert.Equal(original.WorldSeedHex, loaded.WorldSeedHex);
            Assert.Equal(original.PlateCount, loaded.PlateCount);
            Assert.Equal(original.Level, loaded.Level);
            Assert.Equal(original.DeepTimeMyr, loaded.DeepTimeMyr);
            Assert.Equal(original.StateHashHex, loaded.StateHashHex);
            Assert.Equal(WorldSeed, loaded.WorldSeed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VerifyByRecomputationSucceedsForUntamperedCheckpoint()
    {
        WorldPackage pkg = WorldPackage.Create(WorldSeed, PlateCount, Level, 0.0);
        string path = Path.GetTempFileName();
        try
        {
            pkg.Save(path);
            WorldPackage loaded = WorldPackage.Load(path);

            bool ok = loaded.VerifyByRecomputation(out string recomputed);

            Assert.True(ok);
            Assert.Equal(pkg.StateHashHex, recomputed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VerifyByRecomputationFailsWhenParametersAreTampered()
    {
        WorldPackage pkg = WorldPackage.Create(WorldSeed, PlateCount, Level, 0.0);
        pkg.DeepTimeMyr = 999.0; // meghamisítjuk a mentés UTÁN, mintha a fájlt szerkesztették volna

        bool ok = pkg.VerifyByRecomputation(out string recomputed);

        Assert.False(ok);
        Assert.NotEqual(pkg.StateHashHex, recomputed);
    }

    [Fact]
    public void LoadThrowsOnUnknownFormatVersion()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"FormatVersion\": 999, \"WorldSeedHex\": \"0\", \"StateHashHex\": \"x\"}");
            Assert.Throws<NotSupportedException>(() => WorldPackage.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadRejectsVersionOneAfterBoundaryModelChange()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"FormatVersion\": 1, \"WorldSeedHex\": \"0\", \"StateHashHex\": \"x\"}");
            NotSupportedException error = Assert.Throws<NotSupportedException>(() => WorldPackage.Load(path));
            Assert.Contains("ND-90", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadThrowsOnMissingFile()
    {
        Assert.Throws<FileNotFoundException>(() => WorldPackage.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
    }
}
