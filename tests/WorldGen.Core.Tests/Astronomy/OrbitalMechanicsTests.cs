using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using WorldGen.Core.Astronomy;
using Xunit;

namespace WorldGen.Core.Tests.Astronomy;

public class OrbitalMechanicsVectorFileTests
{
    /// <summary>
    /// A Python referencia (tools/reference/astronomy_ref.py) altal generalt
    /// vektorok. TOLERANCIA-alapu osszehasonlitas, NEM bitpontos - a
    /// Math.Sin/Cos/Atan2/Asin nem garantaltan bitre azonos platformok
    /// kozott (ND-26, elfogadott kockazat M3-ra).
    /// </summary>
    [Fact]
    public void MatchesPythonReferenceWithinTolerance()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "testdata", "astronomy_vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        int checkedCount = 0;
        foreach (JsonElement v in root.GetProperty("vectors").EnumerateArray())
        {
            double t = v.GetProperty("t").GetDouble();
            double orbitalPeriod = v.GetProperty("orbitalPeriod").GetDouble();
            double rotationPeriod = v.GetProperty("rotationPeriod").GetDouble();
            double axialTilt = v.GetProperty("axialTilt").GetDouble();
            double orbitalPhase0 = v.GetProperty("orbitalPhase0").GetDouble();
            double rotationPhase0 = v.GetProperty("rotationPhase0").GetDouble();

            JsonElement sunArr = v.GetProperty("sunBodyFrame");
            double expX = sunArr[0].GetDouble();
            double expY = sunArr[1].GetDouble();
            double expZ = sunArr[2].GetDouble();
            double expLat = v.GetProperty("subsolarLat").GetDouble();
            double expLon = v.GetProperty("subsolarLon").GetDouble();

            OrbitalMechanics.SunDirectionBodyFrame(
                t, orbitalPeriod, rotationPeriod, axialTilt, orbitalPhase0, rotationPhase0,
                out double x, out double y, out double z);

            Assert.True(Math.Abs(x - expX) < 1e-9, $"x eltér: {x} vs {expX}");
            Assert.True(Math.Abs(y - expY) < 1e-9, $"y eltér: {y} vs {expY}");
            Assert.True(Math.Abs(z - expZ) < 1e-9, $"z eltér: {z} vs {expZ}");

            OrbitalMechanics.SubsolarPoint(x, y, z, out double lat, out double lon);
            Assert.True(Math.Abs(lat - expLat) < 1e-9, $"lat eltér: {lat} vs {expLat}");
            Assert.True(Math.Abs(lon - expLon) < 1e-9, $"lon eltér: {lon} vs {expLon}");
            checkedCount++;
        }

        Assert.Equal(300, checkedCount);
    }
}

public class OrbitalMechanicsPurityTests
{
    [Fact]
    public void RepeatedCallsAreIdentical()
    {
        for (int i = 0; i < 200; i++)
        {
            double t = i * 3.7;
            OrbitalMechanics.SunDirectionBodyFrame(
                t, 365.25, 1.0, 0.4, 0.1, 0.2, out double x1, out double y1, out double z1);
            OrbitalMechanics.SunDirectionBodyFrame(
                t, 365.25, 1.0, 0.4, 0.1, 0.2, out double x2, out double y2, out double z2);

            Assert.Equal(x1, x2);
            Assert.Equal(y1, y2);
            Assert.Equal(z1, z2);
        }
    }

    /// <summary>Tiszta függvények: párhuzamos kiértékelés ugyanazt adja, mint szekvenciális.</summary>
    [Fact]
    public void ParallelEvaluationMatchesSequential()
    {
        const int n = 20_000;
        var sequential = new double[n];
        for (int i = 0; i < n; i++)
        {
            OrbitalMechanics.SunDirectionBodyFrame(
                i * 0.13, 365.25, 1.0, 0.41, 0.0, 0.0, out double x, out _, out _);
            sequential[i] = x;
        }

        var parallel = new ConcurrentDictionary<int, double>();
        Parallel.For(0, n, i =>
        {
            OrbitalMechanics.SunDirectionBodyFrame(
                i * 0.13, 365.25, 1.0, 0.41, 0.0, 0.0, out double x, out _, out _);
            parallel[i] = x;
        });

        for (int i = 0; i < n; i++)
            Assert.Equal(sequential[i], parallel[i]);
    }
}

public class OrbitalMechanicsPlausibilityTests
{
    private const double AxialTiltEarthLike = 0.40910517666747087; // 23.44 deg radiánban

    /// <summary>Napéjegyenlőség/napforduló minta: 0 deg / ±tengelydőlés, a spec §25/§27 fizikájának megfelelően.</summary>
    [Theory]
    [InlineData(0.0, 0.0)]                    // napéjegyenlőség #1
    [InlineData(0.25 * 365.25, 1.0)]           // napforduló #1: +tilt
    [InlineData(0.5 * 365.25, 0.0)]            // napéjegyenlőség #2
    [InlineData(0.75 * 365.25, -1.0)]          // napforduló #2: -tilt
    public void DeclinationFollowsSeasonalPattern(double t, double expectedSign)
    {
        OrbitalMechanics.SunDirectionBodyFrame(
            t, 365.25, 1.0, AxialTiltEarthLike, 0.0, 0.0,
            out double x, out double y, out double z);
        OrbitalMechanics.SubsolarPoint(x, y, z, out double lat, out _);

        if (expectedSign == 0.0)
            Assert.True(Math.Abs(lat) < 1e-6, $"Napéjegyenlőségnél ~0 várt, kaptunk: {lat}");
        else
            Assert.True(Math.Abs(lat - expectedSign * AxialTiltEarthLike) < 1e-6,
                $"Napfordulónál ±tengelydőlés várt, kaptunk: {lat}");
    }

    [Fact]
    public void SubsolarPointReceivesExactlyFullFlux()
    {
        OrbitalMechanics.SunDirectionBodyFrame(
            12.3, 365.25, 1.0, AxialTiltEarthLike, 0.5, 0.7, out double x, out double y, out double z);

        double insolation = OrbitalMechanics.Insolation(x, y, z, x, y, z, flux: 1361.0);
        Assert.True(Math.Abs(insolation - 1361.0) < 1e-9);
    }

    [Fact]
    public void NightSideReceivesExactlyZero()
    {
        OrbitalMechanics.SunDirectionBodyFrame(
            42.0, 365.25, 1.0, AxialTiltEarthLike, 0.0, 0.0, out double x, out double y, out double z);

        double insolation = OrbitalMechanics.Insolation(x, y, z, -x, -y, -z, flux: 1361.0);
        Assert.Equal(0.0, insolation);
    }

    [Fact]
    public void TerminatorReceivesNearZero()
    {
        OrbitalMechanics.SunDirectionBodyFrame(
            0.0, 365.25, 1.0, AxialTiltEarthLike, 0.0, 0.0, out double x, out double y, out double z);

        // A nap-iranyra meroleges normal (csak x,y sikban, mert z=0 itt t=0-nal a XY-ban van a vektor jelentos resze)
        double perpX = -y, perpY = x, perpZ = 0.0;
        double insolation = OrbitalMechanics.Insolation(x, y, z, perpX, perpY, perpZ, flux: 1361.0);
        Assert.True(Math.Abs(insolation) < 1e-9);
    }

    [Fact]
    public void ZeroAxialTiltGivesConstantZeroDeclination()
    {
        for (int i = 0; i < 20; i++)
        {
            double t = i * 20.0;
            OrbitalMechanics.SunDirectionBodyFrame(
                t, 365.25, 1.0, axialTilt: 0.0, 0.0, 0.0, out double x, out double y, out double z);
            OrbitalMechanics.SubsolarPoint(x, y, z, out double lat, out _);
            Assert.True(Math.Abs(lat) < 1e-9, $"Dőlés nélkül a deklinációnak mindig 0-nak kell lennie: {lat}");
        }
    }
}

public class OrbitalMechanicsParameterSensitivityTests
{
    /// <summary>Minden paraméter érdemben hat a kimenetre - kimaradt paramétert fog meg.</summary>
    [Fact]
    public void EveryParameterAffectsOutput()
    {
        OrbitalMechanics.SunDirectionBodyFrame(
            100.0, 365.25, 1.0, 0.4, 0.1, 0.2, out double bx, out double by, out double bz);

        AssertDiffers(200.0, 365.25, 1.0, 0.4, 0.1, 0.2, bx, by, bz);       // t
        AssertDiffers(100.0, 400.0, 1.0, 0.4, 0.1, 0.2, bx, by, bz);        // orbitalPeriod
        AssertDiffers(100.0, 365.25, 1.3, 0.4, 0.1, 0.2, bx, by, bz);       // rotationPeriod
        AssertDiffers(100.0, 365.25, 1.0, 0.9, 0.1, 0.2, bx, by, bz);       // axialTilt
        AssertDiffers(100.0, 365.25, 1.0, 0.4, 0.9, 0.2, bx, by, bz);       // orbitalPhase0
        AssertDiffers(100.0, 365.25, 1.0, 0.4, 0.1, 0.9, bx, by, bz);       // rotationPhase0
    }

    private static void AssertDiffers(
        double t, double orbitalPeriod, double rotationPeriod, double axialTilt,
        double orbitalPhase0, double rotationPhase0,
        double baseX, double baseY, double baseZ)
    {
        OrbitalMechanics.SunDirectionBodyFrame(
            t, orbitalPeriod, rotationPeriod, axialTilt, orbitalPhase0, rotationPhase0,
            out double x, out double y, out double z);
        Assert.False(x == baseX && y == baseY && z == baseZ);
    }
}

public class OrbitalMechanicsEdgeCaseTests
{
    [Fact]
    public void SunDirectionIsAlwaysUnitLength()
    {
        for (int i = 0; i < 500; i++)
        {
            double t = i * 1.7;
            double axialTilt = (i % 7) * 0.3;
            OrbitalMechanics.SunDirectionBodyFrame(
                t, 200.0 + i, 0.5 + i * 0.01, axialTilt, 0.0, 0.0,
                out double x, out double y, out double z);
            double lenSq = x * x + y * y + z * z;
            Assert.True(Math.Abs(lenSq - 1.0) < 1e-9, $"Nem egységhosszú: {lenSq}");
        }
    }

    [Fact]
    public void StellarFluxMatchesInverseSquareLaw()
    {
        double f1 = OrbitalMechanics.StellarFlux(luminosity: 3.828e26, distance: 1.496e11); // ~1 AU
        double f2 = OrbitalMechanics.StellarFlux(luminosity: 3.828e26, distance: 2.0 * 1.496e11);
        // Ketszeres tavolsag -> negyedannyi fluxus.
        Assert.True(Math.Abs(f1 / f2 - 4.0) < 1e-6);
    }

    [Fact]
    public void SubsolarPointClampsAtPoles()
    {
        // Kozvetlenul a pluszon (z=1 hatarertek) nem szabad NaN-t adnia.
        OrbitalMechanics.SubsolarPoint(0.0, 0.0, 1.0, out double lat, out double lon);
        Assert.False(double.IsNaN(lat));
        Assert.False(double.IsNaN(lon));
        Assert.True(Math.Abs(lat - Math.PI / 2.0) < 1e-9);
    }
}
