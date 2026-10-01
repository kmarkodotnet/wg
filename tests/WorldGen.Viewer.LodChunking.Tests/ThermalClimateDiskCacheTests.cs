using System;
using System.IO;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Viewer.Lod;
using Xunit;

namespace WorldGen.Viewer.LodChunking.Tests;

/// <summary>
/// Az ND-158/159 éves éghajlat lemez-gyorsítótára. A hangsúly a HELYES
/// ELUTASÍTÁSON van: a cache soha nem lehet world-state, tehát minden
/// kétségnél `null`-t kell adnia.
/// </summary>
public class ThermalClimateDiskCacheTests
{
    private const ulong Seed = 184482873278464UL;
    private static readonly ThermalOrbit Orbit = new ThermalOrbit(8.0, 1.0, 23.44 * Math.PI / 180.0);

    /// <summary>Kicsi, de valódi világ — a cache formátumát nem a méret dönti el.</summary>
    private static (DenseGridMetrics grid, SurfaceThermalKind[] kinds, double[] elevation) World(int level = 1)
    {
        DenseGridMetrics grid = DenseGridMetrics.Build(level);
        var kinds = new SurfaceThermalKind[grid.CellCount];
        var elevation = new double[grid.CellCount];
        for (int c = 0; c < grid.CellCount; c++)
        {
            double z = grid.CenterZ[c];
            bool ocean = grid.CenterX[c] < 0.0;
            kinds[c] = ocean ? SurfaceThermalKind.Ocean : SurfaceThermalKind.Land;
            elevation[c] = ocean ? -3000.0 : 200.0 + 5000.0 * Math.Abs(z);
        }
        return (grid, kinds, elevation);
    }

    private static SurfaceTemperatureField Field(ThermalModelParameters? parameters = null)
    {
        (DenseGridMetrics grid, SurfaceThermalKind[] kinds, double[] elevation) = World();
        return new SurfaceTemperatureField(grid, kinds, elevation, 0.0, Seed, 0.0, Orbit, parameters);
    }

    private static ThermalClimate Climate(double? percentile = 0.07, bool includeWind = true)
    {
        (DenseGridMetrics grid, SurfaceThermalKind[] kinds, double[] elevation) = World();
        return ThermalClimateCalculator.Compute(grid, kinds, elevation, 0.0, Seed, 0.0, Orbit,
            sampleDays: 4, permanentIcePercentile: percentile, includeRefinedWind: includeWind);
    }

    private static ThermalClimateDiskCache.Key KeyFor(SurfaceTemperatureField field, double? percentile = 0.07)
        => new ThermalClimateDiskCache.Key(
            field.ModelIdentity,
            field.Grid.CellCount,
            ThermalAnnualStatisticsCalculator.SampleDayIndices(Orbit.OrbitalPeriodDays, 4),
            ThermalClimateDiskCache.Key.PercentileBits(percentile),
            ThermalClimateDiskCache.ComputeSolverFingerprint(field, 0));

    private static byte[] Written(ThermalClimateDiskCache.Key key, ThermalClimateDiskCache.Payload payload)
    {
        using var memory = new MemoryStream();
        ThermalClimateDiskCache.Write(memory, key, payload);
        return memory.ToArray();
    }

    private static ThermalClimateDiskCache.Payload? Read(byte[] bytes, ThermalClimateDiskCache.Key key,
        out string? reason)
    {
        using var memory = new MemoryStream(bytes);
        return ThermalClimateDiskCache.TryRead(memory, key, out reason);
    }

    [Fact]
    public void RoundTripPreservesEveryValueBitExactly()
    {
        SurfaceTemperatureField field = Field();
        ThermalClimate climate = Climate();
        ThermalClimateDiskCache.Key key = KeyFor(field);
        ThermalClimateDiskCache.Payload payload = ThermalClimateDiskCache.Payload.From(climate);

        byte[] bytes = Written(key, payload);
        Assert.Equal(ThermalClimateDiskCache.ExpectedFileSize(
            field.Grid.CellCount, field.ModelIdentity.Length, key.SampleDays.Length), bytes.Length);

        ThermalClimateDiskCache.Payload? loaded = Read(bytes, key, out string? reason);
        Assert.NotNull(loaded);
        Assert.Null(reason);

        for (int c = 0; c < field.Grid.CellCount; c++)
        {
            Assert.Equal(climate.Refined.MeanSurfaceK[c], loaded!.Refined.MeanSurfaceK[c]);
            Assert.Equal(climate.Refined.MeanAirK[c], loaded.Refined.MeanAirK[c]);
            Assert.Equal(climate.Refined.MinSurfaceK[c], loaded.Refined.MinSurfaceK[c]);
            Assert.Equal(climate.Refined.MaxSurfaceK[c], loaded.Refined.MaxSurfaceK[c]);
            Assert.Equal(climate.Refined.MinAirK[c], loaded.Refined.MinAirK[c]);
            Assert.Equal(climate.Refined.MaxAirK[c], loaded.Refined.MaxAirK[c]);
            Assert.Equal(climate.IceFree.MeanSurfaceK[c], loaded.IceFree.MeanSurfaceK[c]);
            Assert.Equal(climate.IceFree.MaxAirK[c], loaded.IceFree.MaxAirK[c]);
            Assert.Equal(climate.IceFreeClass[c], loaded.IceFreeClass[c]);
            Assert.Equal(climate.RefinedClass[c], loaded.RefinedClass[c]);
            Assert.Equal(climate.RefinedKinds[c], loaded.RefinedKinds[c]);
        }
        Assert.Equal(climate.RefinedThresholds.PermanentIceMeanK, loaded!.RefinedThresholdK);
        Assert.Equal(climate.IceFreeThresholds.PermanentIceMeanK, loaded.IceFreeThresholdK);
        Assert.Equal(climate.RefinedThresholds.SeasonalSnowMinK, loaded.SeasonalSnowThresholdK);
        Assert.Equal(climate.ReclassifiedCells, loaded.ReclassifiedCells);
        Assert.Equal(climate.SecondPassSkipped, loaded.SecondPassSkipped);
    }

    [Fact]
    public void RoundTripPreservesRefinedWindForPrecipitationConsumer()
    {
        SurfaceTemperatureField field = Field();
        ThermalClimate climate = Climate(includeWind: true);
        ThermalClimateDiskCache.Key key = KeyFor(field);
        byte[] bytes = Written(key, ThermalClimateDiskCache.Payload.From(climate));
        ThermalClimateDiskCache.Payload? loaded = Read(bytes, key, out string? reason);
        Assert.Null(reason);
        Assert.NotNull(loaded);
        Assert.NotNull(climate.Refined.MeanWindX);
        for (int c = 0; c < field.Grid.CellCount; c++)
        {
            Assert.Equal(climate.Refined.MeanWindX![c], loaded!.Refined.MeanWindX[c]);
            Assert.Equal(climate.Refined.MeanWindY![c], loaded.Refined.MeanWindY[c]);
            Assert.Equal(climate.Refined.MeanWindZ![c], loaded.Refined.MeanWindZ[c]);
            Assert.Equal(climate.Refined.MeanWindSpeedMs![c], loaded.Refined.MeanWindSpeedMs[c]);
        }
    }

    [Fact]
    public void CacheRejectsClimateWithoutAnnualWindInsteadOfWritingPlaceholderValues()
    {
        Assert.Throws<ArgumentException>(() => ThermalClimateDiskCache.Payload.From(
            Climate(includeWind: false)));
    }

    [Fact]
    public void TheSolverFingerprintIsPureAndSensitiveToTheModel()
    {
        SurfaceTemperatureField a = Field();
        SurfaceTemperatureField b = Field();
        Assert.Equal(ThermalClimateDiskCache.ComputeSolverFingerprint(a, 0),
            ThermalClimateDiskCache.ComputeSolverFingerprint(b, 0));

        // Ismételt hívás ugyanazon a mezőn: nincs rejtett állapot.
        Assert.Equal(ThermalClimateDiskCache.ComputeSolverFingerprint(a, 0),
            ThermalClimateDiskCache.ComputeSolverFingerprint(a, 0));

        // A lenyomat a nap BUCKETJÉNEK kanonikus kezdetétől függ, nem magától a
        // naptól: a 0. és a 4. nap ugyanabba a 30 napos bucketbe esik, tehát
        // ugyanaz a kezdőtick és ugyanaz a lenyomat. Ez NEM hiba - a mintanapok
        // úgyis külön kulcs-elemek.
        Assert.Equal(ThermalClimateDiskCache.ComputeSolverFingerprint(a, 0),
            ThermalClimateDiskCache.ComputeSolverFingerprint(a, 4));

        // MÁSIK bucket viszont másik kanonikus szakasz -> más lenyomat.
        Assert.NotEqual(ThermalClimateDiskCache.ComputeSolverFingerprint(a, 0),
            ThermalClimateDiskCache.ComputeSolverFingerprint(a, 30));

        // EZ A LÉNYEG: egy numerikus paraméter-változás megváltoztatja a
        // lenyomatot, tehát a cache nem tölthet be másik modellel írt fájlt.
        SurfaceTemperatureField tweaked = Field(new ThermalModelParameters(radiativeSmoothing: 0.25));
        Assert.NotEqual(ThermalClimateDiskCache.ComputeSolverFingerprint(a, 0),
            ThermalClimateDiskCache.ComputeSolverFingerprint(tweaked, 0));
    }

    [Fact]
    public void ADifferentModelIsRejectedEvenWhenEverythingElseMatches()
    {
        SurfaceTemperatureField field = Field();
        ThermalClimateDiskCache.Key key = KeyFor(field);
        byte[] bytes = Written(key, ThermalClimateDiskCache.Payload.From(Climate()));

        SurfaceTemperatureField other = Field(new ThermalModelParameters(radiativeSmoothing: 0.25));
        ThermalClimateDiskCache.Key otherKey = KeyFor(other);
        Assert.Null(Read(bytes, otherKey, out string? reason));
        Assert.Equal("algoritmus-elteres (solver-lenyomat)", reason);
    }

    [Fact]
    public void AForeignWorldIsRejectedByTheModelIdentity()
    {
        SurfaceTemperatureField field = Field();
        ThermalClimateDiskCache.Key key = KeyFor(field);
        byte[] bytes = Written(key, ThermalClimateDiskCache.Payload.From(Climate()));

        // Ugyanaz a lenyomat és cellaszám, de MÁS modellazonosító: a fejléc
        // ellenőrzése nem elég, a tartalomban tárolt azonosítónak kell fognia.
        var foreign = new ThermalClimateDiskCache.Key(
            new string('0', field.ModelIdentity.Length), key.CellCount, key.SampleDays,
            key.IcePercentileBits, key.SolverFingerprint);
        Assert.Null(Read(bytes, foreign, out string? reason));
        Assert.Equal("modellazonosito-elteres", reason);
    }

    [Fact]
    public void ADifferentIceThresholdModeIsRejected()
    {
        SurfaceTemperatureField field = Field();
        ThermalClimateDiskCache.Key key = KeyFor(field);
        byte[] bytes = Written(key, ThermalClimateDiskCache.Payload.From(Climate()));

        // Percentilis -> abszolút: MÁS világ (az A menet jege adja a B menet
        // felszíntípusait), tehát a cache nem adhatja vissza a másikat.
        ThermalClimateDiskCache.Key absolute = KeyFor(field, null);
        Assert.Null(Read(bytes, absolute, out string? reason));
        Assert.Equal("jegkuszob-elteres", reason);

        ThermalClimateDiskCache.Key otherPercentile = KeyFor(field, 0.10);
        Assert.Null(Read(bytes, otherPercentile, out reason));
        Assert.Equal("jegkuszob-elteres", reason);
    }

    [Fact]
    public void DifferentSampleDaysAreRejected()
    {
        SurfaceTemperatureField field = Field();
        ThermalClimateDiskCache.Key key = KeyFor(field);
        byte[] bytes = Written(key, ThermalClimateDiskCache.Payload.From(Climate()));

        var shiftedDays = (long[])key.SampleDays.Clone();
        shiftedDays[^1] += 1;
        var shifted = new ThermalClimateDiskCache.Key(field.ModelIdentity, key.CellCount, shiftedDays,
            key.IcePercentileBits, key.SolverFingerprint);
        Assert.Null(Read(bytes, shifted, out string? reason));
        Assert.Equal("mintanap-elteres", reason);

        var fewerDays = new ThermalClimateDiskCache.Key(field.ModelIdentity, key.CellCount,
            new long[] { 0, 2 }, key.IcePercentileBits, key.SolverFingerprint);
        Assert.Null(Read(bytes, fewerDays, out reason));
        Assert.Equal("mintanap-szam elterese", reason);
    }

    [Fact]
    public void CorruptionTruncationAndUnknownFormatAreAllRejected()
    {
        SurfaceTemperatureField field = Field();
        ThermalClimateDiskCache.Key key = KeyFor(field);
        byte[] bytes = Written(key, ThermalClimateDiskCache.Payload.From(Climate()));

        var flipped = (byte[])bytes.Clone();
        flipped[^3] ^= 0xFF;
        Assert.Null(Read(flipped, key, out string? reason));
        Assert.Equal("ellenorzoosszeg-elteres", reason);

        Assert.Null(Read(bytes[..10], key, out reason));
        Assert.Equal("csonka fejlec", reason);

        var truncated = bytes[..^16];
        Assert.Null(Read(truncated, key, out reason));
        Assert.Equal("ellenorzoosszeg-elteres", reason);

        var badMagic = (byte[])bytes.Clone();
        badMagic[0] ^= 0xFF;
        Assert.Null(Read(badMagic, key, out reason));
        Assert.Equal("ismeretlen formatum", reason);

        var wrongCellCount = new ThermalClimateDiskCache.Key(field.ModelIdentity, key.CellCount + 1,
            key.SampleDays, key.IcePercentileBits, key.SolverFingerprint);
        Assert.Null(Read(bytes, wrongCellCount, out reason));
        Assert.Equal("cellaszam-elteres", reason);
    }

    [Fact]
    public void FileNamesAreStableDistinctAndRecognisedByTheCleanup()
    {
        SurfaceTemperatureField field = Field();
        ThermalClimateDiskCache.Key key = KeyFor(field);
        string name = key.ToFileName();

        Assert.Equal(name, KeyFor(Field()).ToFileName());
        Assert.True(ThermalClimateDiskCache.IsCurrentFormatFileName(name));
        Assert.NotEqual(name, KeyFor(field, 0.10).ToFileName());
        Assert.NotEqual(name, KeyFor(Field(new ThermalModelParameters(radiativeSmoothing: 0.25))).ToFileName());

        Assert.False(ThermalClimateDiskCache.IsCurrentFormatFileName("climate_k0_old.bin"));
        Assert.False(ThermalClimateDiskCache.IsCurrentFormatFileName(name + ".tmp"));
        Assert.False(ThermalClimateDiskCache.IsCurrentFormatFileName(null!));
    }

    [Fact]
    public void WriteRejectsAPayloadThatDoesNotMatchTheKey()
    {
        SurfaceTemperatureField field = Field();
        ThermalClimateDiskCache.Key key = KeyFor(field);
        ThermalClimateDiskCache.Payload payload = ThermalClimateDiskCache.Payload.From(Climate());
        payload.RefinedClass = new LakesIceErosion.IceClass[key.CellCount - 1];

        using var memory = new MemoryStream();
        Assert.Throws<ArgumentException>(() => ThermalClimateDiskCache.Write(memory, key, payload));
    }

    [Fact]
    public void RejectsNullArguments()
    {
        SurfaceTemperatureField field = Field();
        ThermalClimateDiskCache.Key key = KeyFor(field);
        Assert.Throws<ArgumentNullException>(
            () => ThermalClimateDiskCache.Write(null!, key, ThermalClimateDiskCache.Payload.From(Climate())));
        Assert.Throws<ArgumentNullException>(
            () => ThermalClimateDiskCache.ComputeSolverFingerprint(null!, 0));
        using var memory = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => ThermalClimateDiskCache.TryRead(null!, key, out _));
    }
}
