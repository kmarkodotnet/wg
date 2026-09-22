using System;
using System.IO;
using System.Text.Json;
using WorldGen.Cli;
using WorldGen.Core.Persistence;
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
            Assert.Equal(3, loaded.FormatVersion);
            Assert.Equal(WorldGeneratorVersion.Current, loaded.WorldGeneratorVersion);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(WorldGeneratorVersion.Current, json.RootElement.GetProperty("WorldGeneratorVersion").GetString());
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("999")]
    [InlineData("01")]
    [InlineData("unversioned-dev")]
    public void LoadRejectsMissingOrDifferentGenerator(string? generator)
    {
        string path = Path.GetTempFileName();
        try
        {
            string property = generator == null ? "" : ",\"WorldGeneratorVersion\":" + JsonSerializer.Serialize(generator);
            File.WriteAllText(path, "{\"FormatVersion\":3" + property + "}");
            var error = Assert.Throws<NotSupportedException>(() => WorldPackage.Load(path));
            Assert.Contains("generátorverzió", error.Message);
            Assert.Contains("ND-108", error.Message);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"WorldGeneratorVersion\":\"1\"}")]
    [InlineData("{\"FormatVersion\":2,\"WorldGeneratorVersion\":\"1\"}")]
    [InlineData("{\"FormatVersion\":999,\"WorldGeneratorVersion\":\"1\"}")]
    public void GeneratorCannotOverrideMissingLegacyOrFutureFormat(string json)
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var error = Assert.Throws<NotSupportedException>(() => WorldPackage.Load(path));
            Assert.Contains("formátumverzió", error.Message);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(3, "obsolete")]
    [InlineData(3, "")]
    [InlineData(2, "1")]
    public void DirectVerificationAndSaveCannotBypassCompatibility(int format, string generator)
    {
        // Érvénytelen seed: ha a kapu csak a számítás után futna, más hiba jönne.
        var pkg = new WorldPackage { FormatVersion = format, WorldGeneratorVersion = generator, WorldSeedHex = "not-a-seed" };
        Assert.Throws<NotSupportedException>(() => pkg.VerifyByRecomputation(out _));
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "existing checkpoint");
            Assert.Throws<NotSupportedException>(() => pkg.Save(path));
            Assert.Equal("existing checkpoint", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MutatingLoadedGeneratorIsRejectedAndCliReturnsFailure()
    {
        string path = Path.GetTempFileName();
        try
        {
            var pkg = WorldPackage.Create(WorldSeed, PlateCount, 1, 0);
            pkg.Save(path);
            var loaded = WorldPackage.Load(path);
            loaded.WorldGeneratorVersion = "future";
            Assert.Throws<NotSupportedException>(() => loaded.VerifyByRecomputation(out _));
            File.WriteAllText(path, JsonSerializer.Serialize(loaded));
            byte[] before = File.ReadAllBytes(path);
            Assert.Equal(1, Program.Main(new[] { "checkpoint", "verify", "--in", path }));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }
}
