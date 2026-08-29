using WorldGen.Core.Random;

namespace WorldGen.Core.Terrain
{
    /// <summary>
    /// M13/ND-31: determinisztikus 3D gradiens-zaj (Perlin-stílusú,
    /// "Improving Noise" Ken Perlin 2002 kvintikus fade-görbével) + fBm
    /// (spec §13.2 "F: fractal detail").
    ///
    /// CÉL: a <see cref="Tectonics.CrustElevation"/> korábbi FEHÉR ZAJÁT
    /// (tile-onként FÜGGETLEN, térben NEM koherens) váltja fel valódi,
    /// térben koherens fBm-mel — ez töri meg a lemez-Voronoi-cellák
    /// "túl szabályos" határát organikus változatossággal, dokumentált,
    /// korábban ismert hiányosság (ND-31).
    ///
    /// MÓDSZER: a rácspont-gradiensek hash-elése ÚJRAFELHASZNÁLJA a már
    /// verifikált <see cref="DeterministicRandom.SampleUnitVector3"/>
    /// elutasításos mintavételt (nincs új, ellenőrizetlen hash-függvény) —
    /// a rács-koordináták (ix,iy,iz) és az oktáv-index a MÁR HASZNÁLT
    /// (spatialId, timeBucket) párba csomagolva.
    ///
    /// A kvintikus fade-görbe (6t⁵-15t⁴+10t³) és a trilineáris
    /// interpoláció TISZTA POLINOM/aritmetika — nincs új transzcendens-
    /// kockázat (ND-27 osztálya nem bővül).
    /// </summary>
    public static class FractalNoise
    {
        public const double DefaultBaseFrequency = 8.0;
        public const int DefaultOctaves = 5;
        public const double DefaultPersistence = 0.5;
        public const double DefaultLacunarity = 2.0;

        private static void PackLattice(long ix, long iy, long iz, int octave, out ulong spatialId, out ulong timeBucket)
        {
            uint ux = unchecked((uint)ix);
            uint uy = unchecked((uint)iy);
            uint uz = unchecked((uint)iz);
            uint uo = unchecked((uint)octave);
            spatialId = ((ulong)ux << 32) | uy;
            timeBucket = ((ulong)uo << 32) | uz;
        }

        private static void LatticeGradient(
            ulong worldSeed, long ix, long iy, long iz, int octave,
            out double gx, out double gy, out double gz)
        {
            PackLattice(ix, iy, iz, octave, out ulong spatialId, out ulong timeBucket);
            DeterministicRandom.SampleUnitVector3(
                worldSeed, RandomDomain.Terrain, spatialId, timeBucket,
                out gx, out gy, out gz, RandomProperty.NoiseGradient);
        }

        private static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

        private static double Lerp(double a, double b, double t) => a + t * (b - a);

        private static double Corner(
            ulong worldSeed, long cx, long cy, long cz, int octave, double dx, double dy, double dz)
        {
            LatticeGradient(worldSeed, cx, cy, cz, octave, out double gx, out double gy, out double gz);
            return gx * dx + gy * dy + gz * dz;
        }

        /// <summary>3D Perlin-stílusú gradiens-zaj egy (x,y,z) pontban.</summary>
        public static double GradientNoise3D(ulong worldSeed, double x, double y, double z, int octave = 0)
        {
            long ix0 = (long)System.Math.Floor(x);
            long iy0 = (long)System.Math.Floor(y);
            long iz0 = (long)System.Math.Floor(z);
            long ix1 = ix0 + 1, iy1 = iy0 + 1, iz1 = iz0 + 1;

            double fx = x - ix0, fy = y - iy0, fz = z - iz0;
            double u = Fade(fx), v = Fade(fy), w = Fade(fz);

            double n000 = Corner(worldSeed, ix0, iy0, iz0, octave, fx, fy, fz);
            double n100 = Corner(worldSeed, ix1, iy0, iz0, octave, fx - 1, fy, fz);
            double n010 = Corner(worldSeed, ix0, iy1, iz0, octave, fx, fy - 1, fz);
            double n110 = Corner(worldSeed, ix1, iy1, iz0, octave, fx - 1, fy - 1, fz);
            double n001 = Corner(worldSeed, ix0, iy0, iz1, octave, fx, fy, fz - 1);
            double n101 = Corner(worldSeed, ix1, iy0, iz1, octave, fx - 1, fy, fz - 1);
            double n011 = Corner(worldSeed, ix0, iy1, iz1, octave, fx, fy - 1, fz - 1);
            double n111 = Corner(worldSeed, ix1, iy1, iz1, octave, fx - 1, fy - 1, fz - 1);

            double nx00 = Lerp(n000, n100, u);
            double nx10 = Lerp(n010, n110, u);
            double nx01 = Lerp(n001, n101, u);
            double nx11 = Lerp(n011, n111, u);

            double nxy0 = Lerp(nx00, nx10, v);
            double nxy1 = Lerp(nx01, nx11, v);

            return Lerp(nxy0, nxy1, w);
        }

        /// <summary>Fractal Brownian Motion: oktávok összege, kb. [-1,1] tartományba normálva.</summary>
        public static double Fbm(
            ulong worldSeed, double x, double y, double z,
            double baseFrequency = DefaultBaseFrequency, int octaves = DefaultOctaves,
            double persistence = DefaultPersistence, double lacunarity = DefaultLacunarity)
        {
            double total = 0.0;
            double amplitude = 1.0;
            double frequency = baseFrequency;
            double maxAmplitude = 0.0;

            for (int octave = 0; octave < octaves; octave++)
            {
                total += GradientNoise3D(worldSeed, x * frequency, y * frequency, z * frequency, octave) * amplitude;
                maxAmplitude += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return total / maxAmplitude;
        }
    }
}
