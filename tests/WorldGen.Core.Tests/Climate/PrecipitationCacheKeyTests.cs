using System;
using System.Collections.Generic;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using Xunit;
using Xunit.Abstractions;

namespace WorldGen.Core.Tests.Climate;

/// <summary>
/// A viewer csapadék-mező CACHE-ÉNEK alapja (todo.md 1. tábla 5. sor).
///
/// A gyorsítótár akkor és csak akkor helyes, ha
/// <see cref="MoisturePrecipitation.Compute"/> TISZTA függvény, és a
/// cache-kulcs PONTOSAN az átadott argumentumok halmaza. Ez a fájl mindkettőt
/// igazolja — a CLAUDE.md tesztelési mátrixának két sora szerint
/// („Tisztaság: ismételt hívás azonos", „Minden paraméter érdemben hat a
/// kimenetre").
///
/// MIÉRT KELL EZ KÜLÖN TESZT. Egy cache-kulcsnál kétféleképpen lehet tévedni,
/// és mindkettő CSENDES:
///  - kimarad egy paraméter → a cache elavult mezőt ad vissza, a hiba pedig
///    csak egy konkrét paraméter-váltás után, később jelentkezik;
///  - a függvény mégsem tiszta → ugyanaz a kulcs más eredményt takar.
/// Egyik sem bukna el a szokásos „fut-e le" ellenőrzéseken.
///
/// A viewer a `level`, `plateCount`, `worldSeed`, `climateDayT`,
/// `climateOrbitalPeriodDays`, `climateRotationPeriodDays`,
/// `climateAxialTiltDegrees` és `targetWaterFraction` értékeket adja át; a
/// többi paraméter alapértelmezett marad. Az utolsó teszt azt rögzíti, hogy
/// EZEK IS hatnak — vagyis ha valaha Inspector-mezővé válnának, a kulcsba is
/// be kellene kerülniük.
/// </summary>
public class PrecipitationCacheKeyTests
{
    private readonly ITestOutputHelper _out;
    public PrecipitationCacheKeyTests(ITestOutputHelper o) { _out = o; }

    private const ulong Seed = 0xA7C944210000UL;
    private const int PlateCount = 12;
    private const int Level = 4;
    private const double DayT = 0.0;
    private const double Orbital = 365.25;
    private const double Rotation = 1.0;
    private const double Tilt = 23.44;
    private const double TargetWater = 0.65;

    private static MoisturePrecipitation.PrecipitationField Compute(
        ulong seed = Seed, int plateCount = PlateCount, int level = Level,
        double dayT = DayT, double orbital = Orbital, double rotation = Rotation,
        double tilt = Tilt, double targetWater = TargetWater,
        int iterations = MoisturePrecipitation.DefaultIterations,
        double precipBaseFraction = MoisturePrecipitation.DefaultPrecipBaseFraction,
        double orographicCoeff = MoisturePrecipitation.DefaultOrographicCoeff,
        double orographicElevScale = MoisturePrecipitation.DefaultOrographicElevScale)
        => MoisturePrecipitation.Compute(seed, plateCount, level, dayT, orbital, rotation, tilt,
            iterations, precipBaseFraction, orographicCoeff, orographicElevScale, targetWater);

    /// <summary>Bitre azonos-e a két csapadék-mező?</summary>
    private static bool SamePrecipitation(
        MoisturePrecipitation.PrecipitationField a, MoisturePrecipitation.PrecipitationField b)
    {
        if (a.Precipitation.Count != b.Precipitation.Count) return false;
        if (a.SeaLevel != b.SeaLevel) return false;
        foreach (KeyValuePair<TileId, double> kv in a.Precipitation)
        {
            if (!b.Precipitation.TryGetValue(kv.Key, out double other)) return false;
            if (kv.Value != other) return false;
        }
        return true;
    }

    /// <summary>
    /// TISZTASÁG: ugyanaz a bemenet, ugyanaz a kimenet — bitre. Ez a cache
    /// létjogosultságának alapfeltétele.
    /// </summary>
    [Fact]
    public void RepeatedCallsWithTheSameArgumentsAreBitIdentical()
    {
        MoisturePrecipitation.PrecipitationField first = Compute();
        MoisturePrecipitation.PrecipitationField second = Compute();

        Assert.True(SamePrecipitation(first, second),
            "Ugyanaz a bemenet két külön eredményt adott - a Compute nem tiszta, "
            + "és akkor a gyorsítótár sem lehet helyes.");
        Assert.NotEmpty(first.Precipitation);
    }

    /// <summary>
    /// MINDEN KULCS-ELEM ÉRDEMBEN HAT. Ha bármelyik nem hatna, az azt jelentené,
    /// hogy fölöslegesen van a kulcsban (kisebb baj) — ha viszont a teszt
    /// később elbukik, az azt jelzi, hogy a Compute viselkedése megváltozott,
    /// és a kulcsot újra kell gondolni.
    /// </summary>
    [Fact]
    public void EveryKeyComponentChangesTheResult()
    {
        MoisturePrecipitation.PrecipitationField baseline = Compute();

        var variants = new (string Name, MoisturePrecipitation.PrecipitationField Field)[]
        {
            ("seed", Compute(seed: Seed ^ 0xFFUL)),
            ("plateCount", Compute(plateCount: PlateCount + 1)),
            ("level", Compute(level: Level + 1)),
            ("climateDayT", Compute(dayT: 120.0)),
            ("climateOrbitalPeriodDays", Compute(orbital: 200.0)),
            ("climateRotationPeriodDays", Compute(rotation: 2.5)),
            ("climateAxialTiltDegrees", Compute(tilt: 40.0)),
            ("targetWaterFraction", Compute(targetWater: 0.4)),
        };

        foreach ((string name, MoisturePrecipitation.PrecipitationField field) in variants)
        {
            Assert.False(SamePrecipitation(baseline, field),
                $"A(z) '{name}' megváltoztatása NEM változtatta meg a csapadék-mezőt - "
                + "vagy fölösleges a cache-kulcsban, vagy a Compute viselkedése változott meg.");
            _out.WriteLine($"{name,-28} -> eltérő mező ({field.Precipitation.Count} tile)");
        }
    }

    /// <summary>
    /// A NEM-KULCS paraméterek: a viewer ezeket alapértelmezetten hagyja,
    /// ezért nincsenek a kulcsban. De ÉRDEMBEN HATNAK — tehát ha valaha
    /// Inspector-mezővé válnának, a cache-kulcsba is be KELL kerülniük.
    /// Ez a teszt azért van, hogy ez a feltétel bizonyítva legyen, ne csak
    /// egy komment állítsa.
    /// </summary>
    [Fact]
    public void NonKeyDefaultsWouldMatterIfTheyEverBecameConfigurable()
    {
        MoisturePrecipitation.PrecipitationField baseline = Compute();

        Assert.False(SamePrecipitation(baseline,
            Compute(iterations: MoisturePrecipitation.DefaultIterations + 6)), "iterations");
        Assert.False(SamePrecipitation(baseline,
            Compute(precipBaseFraction: MoisturePrecipitation.DefaultPrecipBaseFraction * 0.5)), "precipBaseFraction");
        Assert.False(SamePrecipitation(baseline,
            Compute(orographicCoeff: MoisturePrecipitation.DefaultOrographicCoeff * 2.0)), "orographicCoeff");
        Assert.False(SamePrecipitation(baseline,
            Compute(orographicElevScale: MoisturePrecipitation.DefaultOrographicElevScale * 2.0)), "orographicElevScale");
    }

    /// <summary>
    /// A CACHE LÉNYEGE: a Compute a deep-time időt MEG SEM KAPJA. Ez a
    /// reflexiós ellenőrzés azt fogja meg, ha valaki később mégis felvenne egy
    /// idő-paramétert — onnantól a viewer cache-kulcsa hiányos lenne.
    /// (A `dayT` a NAPSZAK/évszak a kering&#233;si periódusban, nem a deep-time.)
    /// </summary>
    [Fact]
    public void ComputeHasNoDeepTimeParameter()
    {
        System.Reflection.ParameterInfo[] parameters = typeof(MoisturePrecipitation)
            .GetMethod(nameof(MoisturePrecipitation.Compute))!.GetParameters();

        foreach (System.Reflection.ParameterInfo p in parameters)
        {
            string lower = p.Name!.ToLowerInvariant();
            Assert.False(lower.Contains("deeptime") || lower.Contains("myr") || lower.Contains("timemyr"),
                $"A Compute kapott egy deep-time paramétert ('{p.Name}') - a viewer "
                + "csapadék-cache kulcsát ki kell egészíteni vele, különben elavult mezőt ad.");
        }

        _out.WriteLine("Compute paraméterei: " + string.Join(", ", Array.ConvertAll(parameters, p => p.Name)));
        Assert.Equal(12, parameters.Length);
    }
}
