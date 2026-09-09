using System;
using System.Collections.Generic;
using WorldGen.Core.Random;

namespace WorldGen.Core.Tectonics
{
    /// <summary>
    /// Lemez-eletciklus (szuletes/halal): PlateSplitEvent, PlateMergeEvent/
    /// SubductionTermination, RiftActivation (spec §16, M10+M11 osszevonva). ND-45.
    /// Python referencia: tools/reference/plate_lifecycle_ref.py.
    ///
    /// A konstansok (P_SPLIT stb.) MVP "vilag-tervezesi" ertekek, NEM verifikalt
    /// fizikai konstansok - felhasznaloi megerositest igenyelnek (ND-45).
    ///
    /// TIMESTEP-INVARIANCIA (ND-04, a vezerlo elv): nincs futasidoben akkumulalt
    /// allapot; a ResolveTopology MINDEN hivaskor a TELJES leszarmazasi fat
    /// ujraepiti a gyokerektol, tisztan a seedbol es t-bol - a "lepesenkent
    /// szimulalva" es a "t kozvetlen lekerdezese" DEFINICIO SZERINT ugyanaz.
    ///
    /// A lanc VEGIG bit-egzakt (Sample4/DeterministicRandom + Rodrigues a
    /// DeterministicMath.SinCos-szal - nincs nyers transzcendens), tehat a
    /// referenciahoz bitre egyezik.
    /// </summary>
    public static class PlateLifecycle
    {
        public const double PSplit = 0.45;
        public const double PMerge = 0.25;
        public const double MinLifespanMyr = 80.0;
        public const double MaxLifespanMyr = 400.0;
        public const double RiftFractionMin = 0.40;
        public const double RiftFractionMax = 0.85;
        public const double SplitHalfAngleRad = 0.12;
        public const int MaxGeneration = 3;

        public enum EventKind { None, Split, Merge }

        public static string EventKindName(EventKind k) =>
            k == EventKind.Split ? "split" : k == EventKind.Merge ? "merge" : "none";

        public readonly struct LifecycleRoll
        {
            public readonly EventKind Kind;
            public readonly double LifespanMyr;
            public readonly double RiftFractionOfLifespan;
            public LifecycleRoll(EventKind kind, double lifespan, double riftFraction)
            {
                Kind = kind; LifespanMyr = lifespan; RiftFractionOfLifespan = riftFraction;
            }
        }

        public sealed class PlateRecord
        {
            public ulong PlateId;
            public double PosX, PosY, PosZ;
            public double AxisX, AxisY, AxisZ;
            public double Omega;
            public double BirthTimeMyr;
            public int Generation;
            public ulong? ParentId;
            public EventKind Event;
            public double? PendingEventTimeMyr;
            public double? RiftActivationTimeMyr;
        }

        /// <summary>Egy lemez "eletrajzi sorsa" - tiszta fuggveny (worldSeed, plateId)-bol (ND-45a).</summary>
        public static LifecycleRoll PlateLifecycleRoll(ulong worldSeed, ulong plateId)
        {
            DeterministicRandom.Sample4(worldSeed, RandomDomain.Tectonics, plateId, 0,
                out double a, out double b, out double c, out _, RandomProperty.PlateLifecycleRoll, 0);
            EventKind kind = a < PSplit ? EventKind.Split : (a < PSplit + PMerge ? EventKind.Merge : EventKind.None);
            double lifespan = MinLifespanMyr + b * (MaxLifespanMyr - MinLifespanMyr);
            double riftFraction = RiftFractionMin + c * (RiftFractionMax - RiftFractionMin);
            return new LifecycleRoll(kind, lifespan, riftFraction);
        }

        private static void PerpendicularSplitAxis(ulong worldSeed, ulong plateId,
            double sx, double sy, double sz, out double ax, out double ay, out double az)
        {
            ulong i = 0;
            while (true)
            {
                DeterministicRandom.SampleUnitVector3(worldSeed, RandomDomain.Tectonics, plateId, i,
                    out double hx, out double hy, out double hz, RandomProperty.PlateSplitAxisHint);
                double cx = sy * hz - sz * hy;
                double cy = sz * hx - sx * hz;
                double cz = sx * hy - sy * hx;
                double lenSq = cx * cx + cy * cy + cz * cz;
                if (lenSq > 0.01)
                {
                    double inv = 1.0 / Math.Sqrt(lenSq);
                    ax = cx * inv; ay = cy * inv; az = cz * inv;
                    return;
                }
                i++;
            }
        }

        private sealed class Node
        {
            public ulong PlateId;
            public double BirthTimeMyr;
            public double SeedX, SeedY, SeedZ;
            public double AxisX, AxisY, AxisZ;
            public double Omega;
            public int Generation;
            public ulong? ParentId;
        }

        /// <summary>A lemez-topologia timeMyr idopontban (PlateTopologyAtTime). t=0 = statikus M4 lemez-lista.</summary>
        public static List<PlateRecord> ResolveTopology(ulong worldSeed, double timeMyr, int plateCount, int maxGeneration = MaxGeneration)
        {
            (double X, double Y, double Z)[] rootSeeds = PlateGeneration.GenerateSeeds(worldSeed, plateCount);
            var pending = new Stack<Node>();
            for (int i = 0; i < plateCount; i++)
            {
                PlateMotion.GenerateEulerPole(worldSeed, (ulong)i, out double ax, out double ay, out double az);
                pending.Push(new Node
                {
                    PlateId = (ulong)i, BirthTimeMyr = 0.0,
                    SeedX = rootSeeds[i].X, SeedY = rootSeeds[i].Y, SeedZ = rootSeeds[i].Z,
                    AxisX = ax, AxisY = ay, AxisZ = az,
                    Omega = PlateMotion.GenerateAngularVelocity(worldSeed, (ulong)i),
                    Generation = 0, ParentId = null,
                });
            }

            var active = new List<PlateRecord>();
            while (pending.Count > 0)
            {
                Node node = pending.Pop();
                LifecycleRoll roll = PlateLifecycleRoll(worldSeed, node.PlateId);
                double eventTime = node.BirthTimeMyr + roll.LifespanMyr;
                bool generationCapped = node.Generation >= maxGeneration;
                EventKind kind = generationCapped ? EventKind.None : roll.Kind;

                double elapsed = timeMyr - node.BirthTimeMyr;
                double px = node.SeedX, py = node.SeedY, pz = node.SeedZ;
                if (elapsed != 0.0)
                    PlateMotion.RodriguesRotate(node.SeedX, node.SeedY, node.SeedZ, node.AxisX, node.AxisY, node.AxisZ,
                        node.Omega * elapsed, out px, out py, out pz);

                if (kind == EventKind.None || eventTime > timeMyr)
                {
                    active.Add(new PlateRecord
                    {
                        PlateId = node.PlateId, PosX = px, PosY = py, PosZ = pz,
                        AxisX = node.AxisX, AxisY = node.AxisY, AxisZ = node.AxisZ, Omega = node.Omega,
                        BirthTimeMyr = node.BirthTimeMyr, Generation = node.Generation, ParentId = node.ParentId,
                        Event = kind,
                        PendingEventTimeMyr = kind == EventKind.None ? (double?)null : eventTime,
                        RiftActivationTimeMyr = kind == EventKind.Split
                            ? node.BirthTimeMyr + roll.LifespanMyr * roll.RiftFractionOfLifespan
                            : (double?)null,
                    });
                    continue;
                }

                if (kind == EventKind.Merge)
                    continue; // a lemez megszunik, terulete a szomszedoke (Voronoi)

                // Split: ket gyermek-lemez (a szulo pozicioja PONTOSAN az esemenynel = px,py,pz).
                PerpendicularSplitAxis(worldSeed, node.PlateId, px, py, pz, out double perpX, out double perpY, out double perpZ);
                for (int childIndex = 0; childIndex < 2; childIndex++)
                {
                    double sign = childIndex == 0 ? 1.0 : -1.0;
                    PlateMotion.RodriguesRotate(px, py, pz, perpX, perpY, perpZ, sign * SplitHalfAngleRad,
                        out double cbx, out double cby, out double cbz);
                    ulong childId = DeterministicRandom.Block(worldSeed, RandomDomain.Tectonics, node.PlateId,
                        (ulong)childIndex, RandomProperty.PlateChildId, 0).X0;
                    PlateMotion.GenerateEulerPole(worldSeed, childId, out double cax, out double cay, out double caz);
                    pending.Push(new Node
                    {
                        PlateId = childId, BirthTimeMyr = eventTime,
                        SeedX = cbx, SeedY = cby, SeedZ = cbz,
                        AxisX = cax, AxisY = cay, AxisZ = caz,
                        Omega = PlateMotion.GenerateAngularVelocity(worldSeed, childId),
                        Generation = node.Generation + 1, ParentId = node.PlateId,
                    });
                }
            }

            active.Sort((r1, r2) => r1.PlateId.CompareTo(r2.PlateId));
            return active;
        }
    }
}
