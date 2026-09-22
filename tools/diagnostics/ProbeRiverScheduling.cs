using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Core.Tectonics;
using WorldGen.Core.Terrain;

internal static class Program
{
    private const ulong Seed = 0xA7C944210000UL;
    private static TerrainPointBasis[] _basis;
    private static DailyInsolationSampleDirections _sun;

    private static void Main(string[] args)
    {
        string mode = args.Length == 0 ? "baseline" : args[0];
        var seeds = PlateGeneration.GenerateSeeds(Seed, 20);
        var precipitation = MoisturePrecipitation.Compute(Seed, 20, 5, targetWaterFraction: 0.65);
        var flood = FlowNetwork.PriorityFlood(precipitation.Elevation, precipitation.IsOcean);
        var sources = RiverPathTracing.SelectRiverSourcesPerBasin(
            precipitation.Elevation, precipitation.Precipitation,
            precipitation.IsOcean, flood.Parent, precipitation.SeaLevel);
        Console.WriteLine("mode={0} cores={1} sources={2}", mode, Environment.ProcessorCount, sources.Count);
        _sun = DailyInsolationSampleDirections.Create(0.0, 365.25, 1.0, 23.44 * Math.PI / 180.0);
        _basis = new TerrainPointBasis[393216];
        Parallel.For(0, _basis.Length, index =>
        {
            TileId tile = TileId.FromFaceLevelUV(index / 65536, 8,
                (uint)((index / 256) % 256), (uint)(index % 256));
            TileGeometry.ToPosition(tile, out double x, out double y, out double z);
            _basis[index] = TerrainPointBasis.Compute(Seed, x, y, z);
        });
        Console.WriteLine("terrain basis ready");
        Foreground(seeds, 64);
        using (var cancellation = new CancellationTokenSource())
        using (var finished = new ManualResetEventSlim())
        {
            var watchdog = new Thread(() =>
            {
                if (!finished.Wait(TimeSpan.FromSeconds(30)))
                {
                    Console.WriteLine("dedicated watchdog cancelling rivers at 30s");
                    cancellation.Cancel();
                }
            });
            watchdog.IsBackground = true;
            watchdog.Start();
            Task river = null;
            if (mode == "old")
                river = Task.Run(() => RiverPathTracing.BuildContinuousRiverNetworkFromSourcesParallel(
                    Seed, seeds, precipitation.SeaLevel, sources, 4,
                    cancellation: cancellation.Token), cancellation.Token);
            else if (mode == "dedicated")
                river = Task.Factory.StartNew(() => RiverPathTracing.BuildContinuousRiverNetworkFromSources(
                    Seed, seeds, precipitation.SeaLevel, sources, 4,
                    cancellation: cancellation.Token), cancellation.Token,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);

            var stopwatch = Stopwatch.StartNew();
            for (int iteration = 0; iteration < 3; iteration++)
            {
                stopwatch.Restart();
                double checksum = Foreground(seeds, 393216);
                Console.WriteLine("iteration={0} foregroundMs={1:F1} checksum={2:R}", iteration,
                    stopwatch.Elapsed.TotalMilliseconds, checksum);
            }
            stopwatch.Restart();
            cancellation.Cancel();
            if (river != null)
            {
                try { river.GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { }
            }
            Console.WriteLine("cancelDrainMs={0:F1}", stopwatch.Elapsed.TotalMilliseconds);
            finished.Set();
            watchdog.Join();
        }
    }

    private static double Foreground((double X, double Y, double Z)[] seeds, int count)
    {
        var results = new double[count];
        Parallel.For(0, count, index =>
        {
            TileId tile = TileId.FromFaceLevelUV(index / 65536, 8,
                (uint)((index / 256) % 256), (uint)(index % 256));
            TileGeometry.ToPosition(tile, out double x, out double y, out double z);
            _basis[index].Evaluate(Seed, seeds, out double baseElevation, out double uplift, out _);
            double elevation = baseElevation + uplift;
            double temperature = Temperature.TemperatureKelvinFromSamples(x, y, z, in _sun,
                elevation < 1623.9, elevation, 1623.9);
            results[index] = temperature + FractalNoise.Fbm(Seed, x, y, z, 24.0, 3);
        });
        double checksum = 0.0;
        foreach (double value in results) checksum += value;
        return checksum;
    }
}
