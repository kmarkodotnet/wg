using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;

/// <summary>
/// ND-142 reprodukálható mérőeszköz a Unity run_script parancsához.
/// Háttérszálon, scene- és rendereradat nélkül fut. Kimenet: artifacts/a7.
/// Futás közbeni domain reload megszakítja, ezért fordítás után indítandó.
/// A szintetikus világ a thermal_field_ref.py szabályát követi.
/// </summary>
public static class ThermalStabilityProbe
{
    public static string Start() => StartLevel(6);
    public static string StartSmall() => StartLevel(3);

    private static string StartLevel(int level)
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        string directory = Path.Combine(root, "artifacts", "a7");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "thermal-stability-level" + level + ".csv");
        File.WriteAllText(path, "level,strength,spinup10vs30MaxK,spinup20vs30MaxK,bucketMaxK,bucketRmsK,elapsedSeconds\n");
        Task.Run(() =>
        {
            try
            {
                foreach (double strength in new[] { 0.0, 0.1, 0.25, 1.0 })
                    File.AppendAllText(path, Measure(level, strength) + "\n");
            }
            catch (Exception error) { File.AppendAllText(path, "ERROR: " + error + "\n"); }
        });
        return "Started: " + path;
    }

    private static string Measure(int level, double strength)
    {
        var sw = Stopwatch.StartNew();
        var field = CreateField(level, strength);
        var states = new ThermalSnapshot[3];
        for (int run = 0; run < 3; run++)
        {
            states[run] = new ThermalSnapshot(field.Grid.CellCount);
            states[run].Reset(-(run + 1) * 10 * SimulationTime.TicksPerDay);
            field.RunTo(states[run], 0);
        }
        double error10 = Difference(states[0], states[2], out _);
        double error20 = Difference(states[1], states[2], out _);
        field.RunTo(states[0], SimulationTime.BucketTicks);
        var restarted = new ThermalSnapshot(field.Grid.CellCount);
        field.StateAt(restarted, SimulationTime.BucketTicks);
        double bucketMax = Difference(states[0], restarted, out double bucketRms);
        return string.Join(",", level.ToString(CultureInfo.InvariantCulture), Format(strength), Format(error10),
            Format(error20), Format(bucketMax), Format(bucketRms), Format(sw.Elapsed.TotalSeconds));
    }

    private static double Difference(ThermalSnapshot a, ThermalSnapshot b, out double rms)
    {
        double max = 0, sum = 0;
        for (int c = 0; c < a.ThetaS.Length; c++)
        {
            double ds = a.ThetaS[c] - b.ThetaS[c], da = a.ThetaA[c] - b.ThetaA[c];
            max = Math.Max(max, Math.Max(Math.Abs(ds), Math.Abs(da)));
            sum += ds * ds + da * da;
        }
        rms = Math.Sqrt(sum / (2 * a.ThetaS.Length));
        return max;
    }

    private static SurfaceTemperatureField CreateField(int level, double strength)
    {
        var grid = DenseGridMetrics.Build(level);
        var kinds = new SurfaceThermalKind[grid.CellCount];
        var elevation = new double[grid.CellCount];
        for (int c = 0; c < grid.CellCount; c++)
        {
            double x = grid.CenterX[c], y = grid.CenterY[c], z = grid.CenterZ[c];
            double s = 0.8 * x + 0.3 * y + 0.2 * z;
            if (z > 0.92 || z < -0.92) { kinds[c] = SurfaceThermalKind.Ice; elevation[c] = 300.0; }
            else if (s < 0.05) { kinds[c] = SurfaceThermalKind.Ocean; elevation[c] = -3000.0; }
            else if (x > 0.5 && y > 0.3 && s < 0.6) { kinds[c] = SurfaceThermalKind.Freshwater; elevation[c] = 100.0; }
            else { kinds[c] = SurfaceThermalKind.Land; elevation[c] = 150.0 + 2500.0 * (s - 0.05); }
        }
        return new SurfaceTemperatureField(grid, kinds, elevation, 0.0, 184482873278464UL, 0.0,
            new ThermalOrbit(365.25, 1.0, 23.44 * Math.PI / 180.0),
            new ThermalModelParameters(airFeedbackStrength: strength));
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
