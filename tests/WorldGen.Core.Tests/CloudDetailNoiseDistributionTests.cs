using System;
using WorldGen.Core.Climate;
using Xunit;

namespace WorldGen.Core.Tests
{
    /// <summary>
    /// ND-154: a felhő-részlet zajának ELOSZLÁSA, és az ebből következő
    /// egyenletesítés ellenőrzése.
    ///
    /// MIÉRT VAN ITT A SHADER ZAJÁNAK TÜKRE. A
    /// <see cref="CloudVolume.SubGridThreshold"/> zárt alakja EGYENLETES zajra
    /// van levezetve; ha a shader zaja nem az, a „modell mennyisége megmarad"
    /// I3-érvelés megbukik. A <see cref="CloudVolume.DetailNoiseStdDev"/> tehát
    /// nem szabadon választott szám, hanem MÉRT — és ez a tükör az, ami újra
    /// megméri.
    ///
    /// EZ NEM MÁSODIK IGAZSÁGFORRÁS (nem az ND-128 hibaosztálya): a tükör csak
    /// TESZTBEN él, semmi nem hívja a renderből, és pontosan az a dolga, hogy
    /// ELBUKJON, ha a shader zaja megváltozik — akkor a σ-t újra kell mérni.
    /// </summary>
    public class CloudDetailNoiseDistributionTests
    {
        // --- A WorldGen/CloudVolume shader ertek-zajanak tukre ---------------

        private static uint Hash(uint x, uint y, uint z)
        {
            x *= 0x9E3779B1u;
            y *= 0x85EBCA77u;
            z *= 0xC2B2AE3Du;
            uint h = x ^ y ^ z;
            h ^= h >> 15;
            h *= 0x2545F491u;
            h ^= h >> 13;
            return h;
        }

        private static double Lattice(int i, int j, int k)
            => Hash(unchecked((uint)i), unchecked((uint)j), unchecked((uint)k)) * (1.0 / 4294967296.0);

        private static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

        private static double ValueNoise(double x, double y, double z)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
            double wx = Fade(x - fx), wy = Fade(y - fy), wz = Fade(z - fz);
            int cx = (int)fx, cy = (int)fy, cz = (int)fz;

            double Lerp(double a, double b, double t) => a + (b - a) * t;
            double n000 = Lattice(cx, cy, cz), n100 = Lattice(cx + 1, cy, cz);
            double n010 = Lattice(cx, cy + 1, cz), n110 = Lattice(cx + 1, cy + 1, cz);
            double n001 = Lattice(cx, cy, cz + 1), n101 = Lattice(cx + 1, cy, cz + 1);
            double n011 = Lattice(cx, cy + 1, cz + 1), n111 = Lattice(cx + 1, cy + 1, cz + 1);
            double x00 = Lerp(n000, n100, wx), x10 = Lerp(n010, n110, wx);
            double x01 = Lerp(n001, n101, wx), x11 = Lerp(n011, n111, wx);
            return Lerp(Lerp(x00, x10, wy), Lerp(x01, x11, wy), wz) * 2.0 - 1.0;
        }

        private static double Fbm(double x, double y, double z)
        {
            double total = 0.0, norm = 0.0, amplitude = 1.0;
            for (int k = 0; k < CloudVolume.DetailOctaves; k++)
            {
                total += ValueNoise(x, y, z) * amplitude;
                norm += amplitude;
                amplitude *= CloudVolume.DetailPersistence;
                x *= CloudVolume.DetailLacunarity;
                y *= CloudVolume.DetailLacunarity;
                z *= CloudVolume.DetailLacunarity;
            }
            return norm > 1e-12 ? total / norm : 0.0;
        }

        /// <summary>Determinisztikus mintavétel (nem System.Random, ld. I2).</summary>
        private static double[] SampleFbm(int count)
        {
            var values = new double[count];
            ulong state = 0x2545F4914F6CDD1DUL;
            for (int i = 0; i < count; i++)
            {
                double Next()
                {
                    state ^= state << 13;
                    state ^= state >> 7;
                    state ^= state << 17;
                    return (state >> 11) * (1.0 / 9007199254740992.0) * 500.0;
                }
                values[i] = Fbm(Next(), Next(), Next());
            }
            return values;
        }

        [Fact]
        public void DetailNoiseIsApproximatelyNormalWithTheDocumentedSigma()
        {
            double[] values = SampleFbm(30000);
            double mean = 0.0;
            foreach (double v in values) mean += v;
            mean /= values.Length;
            double variance = 0.0;
            foreach (double v in values) variance += (v - mean) * (v - mean);
            double sigma = Math.Sqrt(variance / values.Length);

            Assert.True(Math.Abs(mean) < 0.02, $"a zaj átlaga {mean:F4}, nem nulla körüli");
            Assert.True(Math.Abs(sigma - CloudVolume.DetailNoiseStdDev) < 0.01,
                $"a MÉRT szórás {sigma:F4}, a dokumentált {CloudVolume.DetailNoiseStdDev} — ha a shader zaja változott, a konstanst újra kell mérni (ld. ND-154)");
        }

        [Fact]
        public void TheUniformMappingActuallyFlattensTheDistribution()
        {
            double[] values = SampleFbm(30000);
            var mapped = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
                mapped[i] = CloudVolume.DetailNoiseToUniform(values[i]);
            Array.Sort(mapped);

            // Kolmogorov–Szmirnov-távolság az egyenletestől.
            double ks = 0.0;
            for (int i = 0; i < mapped.Length; i++)
            {
                double empirical = (i + 0.5) / mapped.Length;
                ks = Math.Max(ks, Math.Abs(mapped[i] - empirical));
            }
            Assert.True(ks < 0.03, $"a leképezés utáni eloszlás KS-távolsága {ks:F4} az egyenletestől");

            // A NAIV leképezés (0,5 + 0,5·fbm) ennél NAGYSÁGRENDDEL rosszabb -
            // ez a mérés indokolja, hogy egyáltalán van leképezés.
            var naive = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
                naive[i] = Math.Min(1.0, Math.Max(0.0, 0.5 + 0.5 * values[i]));
            Array.Sort(naive);
            double naiveKs = 0.0;
            for (int i = 0; i < naive.Length; i++)
                naiveKs = Math.Max(naiveKs, Math.Abs(naive[i] - (i + 0.5) / naive.Length));
            Assert.True(naiveKs > 5.0 * ks, $"a naiv leképezés KS-távolsága {naiveKs:F4}, a javítotté {ks:F4}");
        }

        [Fact]
        public void SubGridCoverage_PreservesTheCellMean_OnTheREALNoiseDistribution()
        {
            // A döntő teszt: a cella-átlag nem az ELMÉLETI egyenletes zajon
            // marad meg, hanem azon, amit a shader TÉNYLEGESEN mintavételez.
            double[] values = SampleFbm(30000);
            for (int k = 1; k <= 9; k++)
            {
                double coverage = k / 10.0;
                double sum = 0.0;
                foreach (double v in values)
                    sum += CloudVolume.SubGridCoverage(coverage, CloudVolume.DetailNoiseToUniform(v));
                double mean = sum / values.Length;
                Assert.True(Math.Abs(mean - coverage) < 0.03,
                    $"lefedettség {coverage}: a VALÓDI zajon vett sub-grid átlag {mean:F4}");
            }
        }

        [Fact]
        public void DetailNoiseToUniform_IsMonotoneAndBounded()
        {
            double previous = -1.0;
            for (int i = -50; i <= 50; i++)
            {
                double u = CloudVolume.DetailNoiseToUniform(i / 25.0);
                Assert.InRange(u, 0.0, 1.0);
                Assert.True(u >= previous, "a leképezésnek monotonnak kell lennie");
                previous = u;
            }
            Assert.Equal(0.5, CloudVolume.DetailNoiseToUniform(0.0), 12);
            Assert.Equal(0.5, CloudVolume.DetailNoiseToUniform(double.NaN));
            Assert.Equal(0.0, CloudVolume.DetailNoiseToUniform(-1e6));
            Assert.Equal(1.0, CloudVolume.DetailNoiseToUniform(1e6));
        }
    }
}
