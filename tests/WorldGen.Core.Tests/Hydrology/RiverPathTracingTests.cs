using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;
using WorldGen.Core.Terrain;
using Xunit;

namespace WorldGen.Core.Tests.Hydrology;

/// <summary>
/// A nyomvonal-kovetes (steepest descent + lokalis pit-escape) BITRE EGZAKT
/// ellenorzese a Python-referenciaval - a forras-listat KESZEN kapjuk a
/// vektorfajlbol (BuildRiverNetworkFromSources), NEM a csapadek-alapu
/// SelectRiverSources-t hivjuk, mert a csapadek-lanc (MoisturePrecipitation)
/// nyers Math.Sin/Cos-t hasznal es NEM garantaltan bit-egzakt cross-platform -
/// ez a teszt kizarolag az ELEVACIO-alapu (bit-egzakt) nyomvonal-kovetesi
/// logikat vizsgalja, fuggetlenul a csapadek-modell tolerancia-kockazatatol.
/// A forras-kivalasztas sajat, kulon (szintetikus adatos) tesztet kap lent.
/// </summary>
public class RiverPathTracingVectorFileTests
{
    private sealed class VectorFile
    {
        public ulong WorldSeed;
        public int PlateCount;
        public int Level;
        public int FineDepth;
        public double SeaLevel;
        public List<TileId> Sources = new List<TileId>();
        public List<(int SourceIndex, string TerminationReason, List<TileId> Path)> Rivers =
            new List<(int, string, List<TileId>)>();
    }

    private static VectorFile Load()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, "testdata", "river_path_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(filePath));
        JsonElement root = doc.RootElement;

        var vf = new VectorFile
        {
            WorldSeed = root.GetProperty("worldSeed").GetUInt64(),
            PlateCount = root.GetProperty("plateCount").GetInt32(),
            Level = root.GetProperty("level").GetInt32(),
            FineDepth = root.GetProperty("fineDepth").GetInt32(),
            SeaLevel = root.GetProperty("seaLevel").GetDouble(),
        };

        int level = vf.Level;
        foreach (JsonElement s in root.GetProperty("sources").EnumerateArray())
            vf.Sources.Add(TileId.FromFaceLevelUV(
                s.GetProperty("face").GetInt32(), level,
                (uint)s.GetProperty("u").GetInt32(), (uint)s.GetProperty("v").GetInt32()));

        int fineLevel = level + vf.FineDepth;
        foreach (JsonElement r in root.GetProperty("rivers").EnumerateArray())
        {
            var path = new List<TileId>();
            foreach (JsonElement p in r.GetProperty("path").EnumerateArray())
                path.Add(TileId.FromFaceLevelUV(
                    p.GetProperty("face").GetInt32(), fineLevel,
                    (uint)p.GetProperty("u").GetInt32(), (uint)p.GetProperty("v").GetInt32()));
            vf.Rivers.Add((
                r.GetProperty("sourceIndex").GetInt32(),
                r.GetProperty("terminationReason").GetString()!,
                path));
        }

        return vf;
    }

    private static RiverPathTracing.TerminationReason ParseTermination(string s) => s switch
    {
        "ocean" => RiverPathTracing.TerminationReason.Ocean,
        "pit" => RiverPathTracing.TerminationReason.Pit,
        "merged" => RiverPathTracing.TerminationReason.Merged,
        "maxSteps" => RiverPathTracing.TerminationReason.MaxSteps,
        _ => throw new ArgumentException($"Ismeretlen terminationReason: {s}"),
    };

    [Fact]
    public void MatchesPythonReferenceExactly()
    {
        VectorFile vf = Load();
        var seeds = PlateGeneration.GenerateSeeds(vf.WorldSeed, vf.PlateCount);

        List<RiverPathTracing.RiverPath> rivers = RiverPathTracing.BuildRiverNetworkFromSources(
            vf.WorldSeed, seeds, vf.SeaLevel, vf.Sources, vf.FineDepth);

        Assert.Equal(vf.Rivers.Count, rivers.Count);
        Assert.Equal(12, rivers.Count); // a vektorfajl ismert merete - ha ez valtozik, a fajlt is regeneraltuk

        for (int i = 0; i < vf.Rivers.Count; i++)
        {
            (int sourceIndex, string terminationReason, List<TileId> expectedPath) = vf.Rivers[i];
            RiverPathTracing.RiverPath actual = rivers[i];

            Assert.Equal(sourceIndex, actual.SourceIndex);
            Assert.Equal(ParseTermination(terminationReason), actual.Termination);
            Assert.Equal(expectedPath.Count, actual.Path.Count);
            for (int j = 0; j < expectedPath.Count; j++)
                Assert.Equal(expectedPath[j].Value, actual.Path[j].Value);
        }
    }
}

/// <summary>Strukturalis/hataresetes egyseg-tesztek - nincs szuksegunk kulso referenciara, mert az algoritmus logikaja (nem egy uj fizikai keplet) az, amit ellenorzunk.</summary>
public class RiverPathTracingUnitTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;

    [Fact]
    public void TraceRiverPathIsPure()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, PlateCount);
        TileId source = TileId.FromFaceLevelUV(2, Level, 17, 31);

        RiverPathTracing.RiverPath a = RiverPathTracing.TraceRiverPath(
            WorldSeed, seeds, -5000.0, source, 0, RiverPathTracing.DefaultFineDepth,
            new Dictionary<TileId, int>(), RiverPathTracing.DefaultMaxSteps, RiverPathTracing.DefaultEscapeNodeBudget);
        RiverPathTracing.RiverPath b = RiverPathTracing.TraceRiverPath(
            WorldSeed, seeds, -5000.0, source, 0, RiverPathTracing.DefaultFineDepth,
            new Dictionary<TileId, int>(), RiverPathTracing.DefaultMaxSteps, RiverPathTracing.DefaultEscapeNodeBudget);

        Assert.Equal(a.Termination, b.Termination);
        Assert.Equal(a.Path.Count, b.Path.Count);
        for (int i = 0; i < a.Path.Count; i++)
            Assert.Equal(a.Path[i].Value, b.Path[i].Value);
    }

    [Fact]
    public void TraceRiverPathNeverRepeatsATile()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, PlateCount);
        // Tobb kulonbozo forrast probalunk - a hurok-mentesseg strukturalis
        // garancia (a path_visited halmaz), nem csak egy konkret forrasra igaz.
        TileId[] sources =
        {
            TileId.FromFaceLevelUV(2, Level, 17, 31),
            TileId.FromFaceLevelUV(2, Level, 38, 32),
            TileId.FromFaceLevelUV(0, Level, 10, 10),
            TileId.FromFaceLevelUV(4, Level, 5, 50),
        };
        foreach (TileId source in sources)
        {
            RiverPathTracing.RiverPath river = RiverPathTracing.TraceRiverPath(
                WorldSeed, seeds, -5000.0, source, 0, RiverPathTracing.DefaultFineDepth,
                new Dictionary<TileId, int>(), RiverPathTracing.DefaultMaxSteps, RiverPathTracing.DefaultEscapeNodeBudget);

            var seen = new HashSet<ulong>();
            foreach (TileId t in river.Path)
                Assert.True(seen.Add(t.Value), $"Ismetlodo tile a nyomvonalban (forras {source.Value}): {t.Value}");
        }
    }

    [Fact]
    public void EarlierSourceClaimsTilesSoLaterSourceCanMerge()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, PlateCount);
        TileId source = TileId.FromFaceLevelUV(2, Level, 17, 31);

        // Elso folyo: nincs claimed tile meg.
        var claimed = new Dictionary<TileId, int>();
        RiverPathTracing.RiverPath first = RiverPathTracing.TraceRiverPath(
            WorldSeed, seeds, -5000.0, source, 0, RiverPathTracing.DefaultFineDepth,
            claimed, RiverPathTracing.DefaultMaxSteps, RiverPathTracing.DefaultEscapeNodeBudget);
        foreach (TileId t in first.Path) claimed[t] = 0;

        // Masodik "folyo" UGYANABBOL a forrasbol indul - az elso lepesenel
        // MAR a sajat kezdopontja is "claimed" (step==0 kivetel miatt ez meg
        // nem all meg), de a MASODIK lepesenel mar a claimed halmazba fut ->
        // azonnal "merged"-kent zarul.
        RiverPathTracing.RiverPath second = RiverPathTracing.TraceRiverPath(
            WorldSeed, seeds, -5000.0, source, 1, RiverPathTracing.DefaultFineDepth,
            claimed, RiverPathTracing.DefaultMaxSteps, RiverPathTracing.DefaultEscapeNodeBudget);

        Assert.Equal(RiverPathTracing.TerminationReason.Merged, second.Termination);
        Assert.True(second.Path.Count <= first.Path.Count);
    }

    [Fact]
    public void VeryHighSeaLevelTerminatesSourceImmediatelyAsOcean()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, PlateCount);
        TileId source = TileId.FromFaceLevelUV(2, Level, 17, 31);

        // Ha a "tengerszint" magasabb, mint barmilyen realis eleváció, a
        // forras MAGA is "ocean"-nek szamit -> azonnal megall, 1 elemu utvonal.
        RiverPathTracing.RiverPath river = RiverPathTracing.TraceRiverPath(
            WorldSeed, seeds, 1_000_000.0, source, 0, RiverPathTracing.DefaultFineDepth,
            new Dictionary<TileId, int>(), RiverPathTracing.DefaultMaxSteps, RiverPathTracing.DefaultEscapeNodeBudget);

        Assert.Equal(RiverPathTracing.TerminationReason.Ocean, river.Termination);
        Assert.Single(river.Path);
    }

    [Fact]
    public void SelectRiverSourcesRespectsElevationAndPrecipitationThresholds()
    {
        TileId low = TileId.FromFaceLevelUV(0, 3, 0, 0);
        TileId highDry = TileId.FromFaceLevelUV(0, 3, 1, 0);
        TileId highWet = TileId.FromFaceLevelUV(0, 3, 2, 0);
        TileId ocean = TileId.FromFaceLevelUV(0, 3, 3, 0);

        var elevField = new Dictionary<TileId, double>
        {
            [low] = 50.0, [highDry] = 1000.0, [highWet] = 1200.0, [ocean] = -500.0,
        };
        var precipField = new Dictionary<TileId, double>
        {
            [low] = 0.9, [highDry] = 0.1, [highWet] = 0.95, [ocean] = 0.5,
        };
        var isOcean = new Dictionary<TileId, bool>
        {
            [low] = false, [highDry] = false, [highWet] = false, [ocean] = true,
        };

        List<TileId> sources = RiverPathTracing.SelectRiverSources(
            elevField, precipField, isOcean, seaLevel: 0.0,
            topK: 10, minElevAboveSeaM: 300.0, precipPercentile: 0.5);

        // Csak a highWet felel meg MINDKET kuszobnek (magas ES csapadekos);
        // low csapadekos de alacsony, highDry magas de szaraz, ocean ki van zarva.
        Assert.Single(sources);
        Assert.Equal(highWet.Value, sources[0].Value);
    }

    [Fact]
    public void SelectRiverSourcesTopKKeepsHighestPrecipitationFirst()
    {
        var elevField = new Dictionary<TileId, double>();
        var precipField = new Dictionary<TileId, double>();
        var isOcean = new Dictionary<TileId, bool>();
        var tiles = new List<TileId>();
        for (int i = 0; i < 5; i++)
        {
            TileId t = TileId.FromFaceLevelUV(0, 3, (uint)i, 0);
            tiles.Add(t);
            elevField[t] = 1000.0;
            precipField[t] = i; // 0,1,2,3,4 - novekvo csapadek
            isOcean[t] = false;
        }

        List<TileId> sources = RiverPathTracing.SelectRiverSources(
            elevField, precipField, isOcean, seaLevel: 0.0,
            topK: 2, minElevAboveSeaM: 0.0, precipPercentile: 0.0);

        Assert.Equal(2, sources.Count);
        // A legmagasabb csapadeku (index 4, ertek 4) all elso helyen.
        Assert.Equal(tiles[4].Value, sources[0].Value);
        Assert.Equal(tiles[3].Value, sources[1].Value);
    }

    [Fact]
    public void SelectRiverSourcesIsPure()
    {
        var elevField = new Dictionary<TileId, double> { [TileId.FromFaceLevelUV(0, 3, 0, 0)] = 1000.0 };
        var precipField = new Dictionary<TileId, double> { [TileId.FromFaceLevelUV(0, 3, 0, 0)] = 1.0 };
        var isOcean = new Dictionary<TileId, bool> { [TileId.FromFaceLevelUV(0, 3, 0, 0)] = false };

        List<TileId> a = RiverPathTracing.SelectRiverSources(elevField, precipField, isOcean, 0.0, 5, 0.0, 0.0);
        List<TileId> b = RiverPathTracing.SelectRiverSources(elevField, precipField, isOcean, 0.0, 5, 0.0, 0.0);

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
            Assert.Equal(a[i].Value, b[i].Value);
    }

    [Fact]
    public void SelectRiverSourcesOnEmptyFieldReturnsEmpty()
    {
        List<TileId> sources = RiverPathTracing.SelectRiverSources(
            new Dictionary<TileId, double>(), new Dictionary<TileId, double>(),
            new Dictionary<TileId, bool>(), 0.0);
        Assert.Empty(sources);
    }
}

/// <summary>
/// A folytonos (nem tile-racshoz kotott) nyomvonal-koveto (ND-49, 2026-09-06,
/// HARMADSZOR ujragondolva felhasznaloi visszajelzes utan - ld. `TraceRiverPathContinuous`
/// osztaly-doksi es docs/04-decisions.md ND-49 3. kiegeszites) tesztjei.
/// KIVETEL a projekt "referencia-elobb" szabalya alol - INDOKLAS: a metodus a
/// MAR bit-egzakt pontszeru elevaciofuggvenyt hasznalja UJ FIZIKA NELKUL,
/// tisztan egy geometriai/algoritmikus (racs-kereses az erinto-sikban) lepest
/// ad hozza; kozvetlen, geometriai invarianciakat ES a termeszetes
/// konvergenciat ellenorzo C#-tesztekkel verifikalunk.
///
/// A LEGFONTOSABB UJ ELVARAS (a korabbi, hibas valtozat regresszioja ellen):
/// a nyomvonalnak TENYLEGESEN el kell ernie a termeszetes vegallapotat
/// (Ocean vagy Pit) - SOHA nem `MaxSteps`, ami a korabbi valtozatnal egy
/// rejtett, ~150 km-es mesterseges "teleport"-ot takart (merve, ld. ND-49
/// 3. kiegeszites).
/// </summary>
public class TraceRiverPathContinuousTests
{
    private const ulong WorldSeed = 0xA7C944210000UL;
    private const int PlateCount = 20;
    private const int Level = 6;

    private static double SeaLevel((double X, double Y, double Z)[] seeds) =>
        SeaLevelCalibration.CalibrateSeaLevel(
            SeaLevelCalibration.ComputeElevationField(WorldSeed, PlateCount, Level).Values, 0.65);

    [Fact]
    public void IsPure()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, PlateCount);
        double seaLevel = SeaLevel(seeds);
        TileId source = TileId.FromFaceLevelUV(2, Level, 17, 31);

        var a = RiverPathTracing.TraceRiverPathContinuous(WorldSeed, seeds, seaLevel, source, 0, RiverPathTracing.DefaultFineDepth, new Dictionary<TileId, RiverPathTracing.ClaimedTileInfo>());
        var b = RiverPathTracing.TraceRiverPathContinuous(WorldSeed, seeds, seaLevel, source, 0, RiverPathTracing.DefaultFineDepth, new Dictionary<TileId, RiverPathTracing.ClaimedTileInfo>());

        Assert.Equal(a.Termination, b.Termination);
        Assert.Equal(a.Points.Count, b.Points.Count);
        for (int i = 0; i < a.Points.Count; i++)
        {
            Assert.Equal(a.Points[i].X, b.Points[i].X, 12);
            Assert.Equal(a.Points[i].Y, b.Points[i].Y, 12);
            Assert.Equal(a.Points[i].Z, b.Points[i].Z, 12);
        }
    }

    [Fact]
    public void AllPointsStayOnTheUnitSphere()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, PlateCount);
        double seaLevel = SeaLevel(seeds);
        TileId source = TileId.FromFaceLevelUV(2, Level, 17, 31);

        var river = RiverPathTracing.TraceRiverPathContinuous(WorldSeed, seeds, seaLevel, source, 0, RiverPathTracing.DefaultFineDepth, new Dictionary<TileId, RiverPathTracing.ClaimedTileInfo>());

        Assert.True(river.Points.Count > 2);
        foreach ((double X, double Y, double Z) p in river.Points)
        {
            double len = Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
            Assert.True(Math.Abs(len - 1.0) < 1e-9, $"A pont nincs az egysegombon: |p|={len}");
        }
    }

    [Fact]
    public void StartsAtSourceFineTilePosition()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, PlateCount);
        double seaLevel = SeaLevel(seeds);
        TileId source = TileId.FromFaceLevelUV(2, Level, 17, 31);

        var river = RiverPathTracing.TraceRiverPathContinuous(WorldSeed, seeds, seaLevel, source, 0, RiverPathTracing.DefaultFineDepth, new Dictionary<TileId, RiverPathTracing.ClaimedTileInfo>());

        source.GetUV(out uint su, out uint sv);
        TileId fineSource = TileId.FromFaceLevelUV(source.Face, source.Level + RiverPathTracing.DefaultFineDepth, su << RiverPathTracing.DefaultFineDepth, sv << RiverPathTracing.DefaultFineDepth);
        TileGeometry.ToPosition(fineSource, out double sx, out double sy, out double sz);

        Assert.Equal(sx, river.Points[0].X, 12);
        Assert.Equal(sy, river.Points[0].Y, 12);
        Assert.Equal(sz, river.Points[0].Z, 12);
    }

    [Fact]
    public void SmallerStepSizeProducesMorePoints()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, PlateCount);
        double seaLevel = SeaLevel(seeds);
        TileId source = TileId.FromFaceLevelUV(2, Level, 17, 31);

        var coarse = RiverPathTracing.TraceRiverPathContinuous(WorldSeed, seeds, seaLevel, source, 0, RiverPathTracing.DefaultFineDepth, new Dictionary<TileId, RiverPathTracing.ClaimedTileInfo>(), stepMeters: 500.0);
        var fine = RiverPathTracing.TraceRiverPathContinuous(WorldSeed, seeds, seaLevel, source, 0, RiverPathTracing.DefaultFineDepth, new Dictionary<TileId, RiverPathTracing.ClaimedTileInfo>(), stepMeters: 50.0);

        Assert.True(fine.Points.Count > coarse.Points.Count,
            $"50m lepeskoznek tobb pontot kellene adnia, mint 500m-nek (kapott: fine={fine.Points.Count}, coarse={coarse.Points.Count}).");
    }

    /// <summary>
    /// REGRESSZIO-VEDELEM: a korabbi ("iranykupos + stagnalas-eszleles")
    /// valtozat MERESSEL bizonyitottan SOHA nem ert termeszetes veget ezen a
    /// forrason belul a lepesszam-korlaton - mindig `MaxSteps`-be futott, es
    /// egy rejtett, ~150 km-es "safety net" teleport zarta a torkolatra (ld.
    /// docs/04-decisions.md ND-49 3. kiegeszites). Ez a teszt mind a 12
    /// referencia-forrason (ld. river_path_vectors.json) ellenorzi, hogy a
    /// termeszetes vegallapot (Ocean vagy Pit) TENYLEGESEN teljesul - SOHA
    /// nem `MaxSteps`.
    /// </summary>
    [Theory]
    [InlineData(2, 17u, 31u)]
    [InlineData(2, 38u, 32u)]
    [InlineData(3, 47u, 31u)]
    [InlineData(0, 31u, 25u)]
    [InlineData(1, 31u, 59u)]
    [InlineData(0, 31u, 14u)]
    [InlineData(2, 17u, 32u)]
    [InlineData(3, 47u, 32u)]
    [InlineData(3, 46u, 32u)]
    [InlineData(2, 8u, 15u)]
    [InlineData(2, 18u, 31u)]
    [InlineData(1, 32u, 59u)]
    public void ReachesNaturalTerminationNotMaxSteps(int face, uint u, uint v)
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, PlateCount);
        double seaLevel = SeaLevel(seeds);
        TileId source = TileId.FromFaceLevelUV(face, Level, u, v);

        var river = RiverPathTracing.TraceRiverPathContinuous(WorldSeed, seeds, seaLevel, source, 0, RiverPathTracing.DefaultFineDepth, new Dictionary<TileId, RiverPathTracing.ClaimedTileInfo>());

        Assert.NotEqual(RiverPathTracing.TerminationReason.MaxSteps, river.Termination);
        Assert.True(
            river.Termination == RiverPathTracing.TerminationReason.Ocean || river.Termination == RiverPathTracing.TerminationReason.Pit,
            $"Varatlan termination: {river.Termination}");

        double finalElev = river.Points.Count > 0
            ? ElevationViaReflectionForTest(WorldSeed, seeds, river.Points[^1])
            : double.NaN;
        if (river.Termination == RiverPathTracing.TerminationReason.Ocean)
            Assert.True(finalElev < seaLevel + 1e-6, $"Ocean vegallapotnal a vegpont elevacioja ({finalElev:F1}) a tengerszint ({seaLevel:F1}) alatt kell legyen.");
    }

    /// <summary>Segéd a végpont elevációjának ellenőrzéséhez - a Core NEM ad publikus API-t pontszerű elevációhoz, itt csak a teszt-ellenőrzés kedvéért, a PlateBoundaryEffect-en keresztül.</summary>
    private static double ElevationViaReflectionForTest(ulong worldSeed, (double X, double Y, double Z)[] seeds, (double X, double Y, double Z) p)
    {
        DomainWarp.WarpPosition(worldSeed, p.X, p.Y, p.Z, out double wx, out double wy, out double wz);
        int plateId = PlateGeneration.AssignPlate(wx, wy, wz, seeds);
        return PlateBoundaryEffect.ElevationWithBoundaryFromWarped(worldSeed, plateId, 0UL, p.X, p.Y, p.Z, wx, wy, wz, seeds, out _);
    }

    [Fact]
    public void ClaimedTileEndsAsMerged()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, PlateCount);
        double seaLevel = SeaLevel(seeds);
        TileId source = TileId.FromFaceLevelUV(2, Level, 17, 31);

        var first = RiverPathTracing.TraceRiverPathContinuous(WorldSeed, seeds, seaLevel, source, 0, RiverPathTracing.DefaultFineDepth, new Dictionary<TileId, RiverPathTracing.ClaimedTileInfo>());

        var claimed = new Dictionary<TileId, RiverPathTracing.ClaimedTileInfo>();
        int fineLevel = source.Level + RiverPathTracing.DefaultFineDepth;
        foreach ((double X, double Y, double Z) p in first.Points)
        {
            TileId t = TileGeometry.FromPosition(p.X, p.Y, p.Z, fineLevel);
            if (!claimed.ContainsKey(t)) claimed[t] = new RiverPathTracing.ClaimedTileInfo(0, p);
        }

        // Ugyanabbol a forrasbol indulo "masodik folyo" - a sajat utja MAR
        // teljes egeszeben claimed (az elsotol), tehat MERGED-kent kell
        // vegzodnie, MIELOTT elerne az oceant/pit-et.
        var second = RiverPathTracing.TraceRiverPathContinuous(WorldSeed, seeds, seaLevel, source, 1, RiverPathTracing.DefaultFineDepth, claimed);

        Assert.Equal(RiverPathTracing.TerminationReason.Merged, second.Termination);

        // REGRESSZIO-VEDELEM (2026-09-06, felhasznaloi visszajelzes: "az
        // utvonal a feluleten helyenkent megszakadni tunik"): a MEGSZAKADO
        // folyo UTOLSO pontjanak PONTOSAN egyeznie kell a befogado folyo
        // valamelyik pontjaval (nem csak "ugyanabban a durva fine-tile-ban"
        // kell lennie) - kulonben a ket vonal vizualisan rest hagy a
        // talalkozasnal.
        (double X, double Y, double Z) mergedEndpoint = second.Points[^1];
        bool matchesAFirstPoint = false;
        foreach ((double X, double Y, double Z) p in first.Points)
        {
            if (Math.Abs(p.X - mergedEndpoint.X) < 1e-12 && Math.Abs(p.Y - mergedEndpoint.Y) < 1e-12 && Math.Abs(p.Z - mergedEndpoint.Z) < 1e-12)
            {
                matchesAFirstPoint = true;
                break;
            }
        }
        Assert.True(matchesAFirstPoint, "A megszakado folyo vegpontjanak PONTOSAN egyeznie kell a befogado folyo egyik pontjaval.");
    }

    [Fact]
    public void BuildContinuousRiverNetworkFromSourcesProducesOneEntryPerSource()
    {
        var seeds = PlateGeneration.GenerateSeeds(WorldSeed, PlateCount);
        double seaLevel = SeaLevel(seeds);
        var sources = new List<TileId>
        {
            TileId.FromFaceLevelUV(2, Level, 17, 31),
            TileId.FromFaceLevelUV(1, Level, 31, 59),
        };

        var rivers = RiverPathTracing.BuildContinuousRiverNetworkFromSources(WorldSeed, seeds, seaLevel, sources, RiverPathTracing.DefaultFineDepth);

        Assert.Equal(2, rivers.Count);
        foreach (RiverPathTracing.ContinuousRiverPath r in rivers)
            Assert.NotEqual(RiverPathTracing.TerminationReason.MaxSteps, r.Termination);
    }

    [Fact]
    public void ComputeDischargeWeightsSumsChainedMerges()
    {
        // Lanc: 2 beleolvad 1-be, 1 beleolvad 0-ba (0 <- 1 <- 2) - a
        // forditott bejaras ellenorzese: 0-nak a TELJES lanc sulyat kell
        // kapnia, nem csak a kozvetlen gyermeket (1-et). 3 fuggetlen forras
        // (sehova nem olvad be).
        var rivers = new List<RiverPathTracing.ContinuousRiverPath>
        {
            new() { SourceIndex = 0, MergedIntoRiverIndex = -1 },
            new() { SourceIndex = 1, MergedIntoRiverIndex = 0 },
            new() { SourceIndex = 2, MergedIntoRiverIndex = 1 },
            new() { SourceIndex = 3, MergedIntoRiverIndex = -1 },
        };

        int[] weights = RiverPathTracing.ComputeDischargeWeights(rivers);

        Assert.Equal(new[] { 3, 2, 1, 1 }, weights);
    }

    [Fact]
    public void ComputeDischargeWeightsAllOnesWhenNoMerges()
    {
        var rivers = new List<RiverPathTracing.ContinuousRiverPath>
        {
            new() { SourceIndex = 0, MergedIntoRiverIndex = -1 },
            new() { SourceIndex = 1, MergedIntoRiverIndex = -1 },
            new() { SourceIndex = 2, MergedIntoRiverIndex = -1 },
        };

        int[] weights = RiverPathTracing.ComputeDischargeWeights(rivers);

        Assert.Equal(new[] { 1, 1, 1 }, weights);
    }

    [Fact]
    public void ComputeDischargeWeightsSumsMultipleTributariesIntoSameParent()
    {
        // Ket FUGGETLEN mellekfolyo (1 es 2) olvad ugyanabba a fo-agba (0) -
        // 0-nak MINDKETTO sulyat meg kell kapnia.
        var rivers = new List<RiverPathTracing.ContinuousRiverPath>
        {
            new() { SourceIndex = 0, MergedIntoRiverIndex = -1 },
            new() { SourceIndex = 1, MergedIntoRiverIndex = 0 },
            new() { SourceIndex = 2, MergedIntoRiverIndex = 0 },
        };

        int[] weights = RiverPathTracing.ComputeDischargeWeights(rivers);

        Assert.Equal(new[] { 3, 1, 1 }, weights);
    }

    // ELESETEK (code-review-ban feltart hianyossag, 2026-09-06: CLAUDE.md
    // "Tesztelesi elvarasok" tablazata elesetek tesztelset ir elo minden
    // uj modulhoz - a fenti 3 teszt csak ervenyes, tartomanyon beluli
    // bemenetekkel dolgozott).

    [Fact]
    public void ComputeDischargeWeightsEmptyListReturnsEmptyArray()
    {
        int[] weights = RiverPathTracing.ComputeDischargeWeights(new List<RiverPathTracing.ContinuousRiverPath>());

        Assert.Empty(weights);
    }

    [Fact]
    public void ComputeDischargeWeightsSingleRiverIsOne()
    {
        var rivers = new List<RiverPathTracing.ContinuousRiverPath>
        {
            new() { SourceIndex = 0, MergedIntoRiverIndex = -1 },
        };

        int[] weights = RiverPathTracing.ComputeDischargeWeights(rivers);

        Assert.Equal(new[] { 1 }, weights);
    }

    [Fact]
    public void ComputeDischargeWeightsIgnoresOutOfRangeMergedIntoRiverIndex()
    {
        // A MergedIntoRiverIndex ELMELETBEN mindig ervenyes (a claimed-terkep
        // csak mar bejart folyok indexet adhatja), de a fuggveny akkor is
        // biztonsagosan viselkedjen, ha ez a garancia (pl. egy jovobeli hivo
        // hibaja miatt) megis serulne - ne dobjon kivetelt, ne irjon tul
        // hatarokon, es a "sajat" sulyat (1) meg akkor is szamolja bele.
        var rivers = new List<RiverPathTracing.ContinuousRiverPath>
        {
            new() { SourceIndex = 0, MergedIntoRiverIndex = -1 },
            new() { SourceIndex = 1, MergedIntoRiverIndex = 99 }, // ervenytelen: nincs 99-es index
        };

        int[] weights = RiverPathTracing.ComputeDischargeWeights(rivers);

        Assert.Equal(new[] { 1, 1 }, weights);
    }
}
