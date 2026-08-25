using System;
using System.Runtime.CompilerServices;

namespace WorldGen.Core.Random
{
    /// <summary>
    /// A világgenerátor determinisztikus véletlen-rétege.
    ///
    /// SZERZŐDÉS (a projekt alapköve — ne változtasd könnyelműen):
    ///
    ///  1. Minden metódus TISZTA FÜGGVÉNY. Nincs állapot, nincs mező, nincs
    ///     thread-lokális seed, nincs inicializálási sorrend.
    ///  2. Ugyanaz a bemenet ugyanazt a kimenetet adja — bármelyik szálon,
    ///     bármilyen kiértékelési sorrendben, bármelyik platformon, bitre.
    ///  3. Ebből következik a párhuzamosíthatóság: a tile-okat tetszőleges
    ///     sorrendben, tetszőleges szálon lehet feldolgozni.
    ///
    /// A kulcs/counter leképezés:
    ///     key = [ worldSeed, (domainId &lt;&lt; 32) | propertyId, AlgorithmVersion, 0 ]
    ///     ctr = [ spatialId, timeBucket, sampleIndex, 0 ]
    ///
    /// Ha ez a leképezés megváltozik, MINDEN korábbi seed más világot ad.
    /// Ilyenkor az AlgorithmVersion-t növelni kell, és a betöltésnél hibát dobni.
    /// </summary>
    public static class DeterministicRandom
    {
        /// <summary>
        /// A leképezés verziója. Növelése seed-törő változás.
        /// A world package metadata-jában is szerepel; eltérésnél a betöltés hibázik.
        /// </summary>
        public const ulong AlgorithmVersion = 1;

        // 2^-53 — a double mantisszájának megfelelő skálázás.
        // Fordítási idejű konstans, tehát nincs futásidejű FPU-művelet mögötte.
        private const double Scale53 = 1.0 / 9007199254740992.0;

        /// <summary>
        /// A nyers blokk. A magasabb szintű metódusok mind erre épülnek.
        /// Akkor hívd közvetlenül, ha egyszerre több független értéket akarsz
        /// ugyanabból a pontból (pl. gradiensvektor) — így egy blokk elég négyhez.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Block4 Block(
            ulong worldSeed,
            uint domainId,
            ulong spatialId,
            ulong timeBucket,
            uint propertyId = RandomProperty.Default,
            ulong sampleIndex = 0)
        {
            ulong key1 = ((ulong)domainId << 32) | propertyId;
            return Threefry4x64.Compute(
                spatialId, timeBucket, sampleIndex, 0UL,
                worldSeed, key1, AlgorithmVersion, 0UL);
        }

        /// <summary>Egyenletes eloszlású érték a [0,1) intervallumban.</summary>
        /// <remarks>
        /// A felső 53 bitet használjuk, egész szorzással skálázva. Ez FPU-független:
        /// nincs osztás, nincs kerekítési mód-függés, nincs denormál szám.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Sample(
            ulong worldSeed,
            uint domainId,
            ulong spatialId,
            ulong timeBucket,
            uint propertyId = RandomProperty.Default,
            ulong sampleIndex = 0)
        {
            ulong x = Block(worldSeed, domainId, spatialId, timeBucket, propertyId, sampleIndex).X0;
            return (x >> 11) * Scale53;
        }

        /// <summary>
        /// Négy független [0,1) érték egyetlen blokkból.
        /// Négyszer olcsóbb, mint négy külön Sample hívás.
        /// </summary>
        public static void Sample4(
            ulong worldSeed, uint domainId, ulong spatialId, ulong timeBucket,
            out double a, out double b, out double c, out double d,
            uint propertyId = RandomProperty.Default, ulong sampleIndex = 0)
        {
            Block4 blk = Block(worldSeed, domainId, spatialId, timeBucket, propertyId, sampleIndex);
            a = (blk.X0 >> 11) * Scale53;
            b = (blk.X1 >> 11) * Scale53;
            c = (blk.X2 >> 11) * Scale53;
            d = (blk.X3 >> 11) * Scale53;
        }

        /// <summary>Egyenletes érték adott tartományban.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SampleRange(
            ulong worldSeed, uint domainId, ulong spatialId, ulong timeBucket,
            double min, double max,
            uint propertyId = RandomProperty.Default, ulong sampleIndex = 0)
        {
            return min + (max - min) *
                   Sample(worldSeed, domainId, spatialId, timeBucket, propertyId, sampleIndex);
        }

        /// <summary>
        /// Torzításmentes egész a [minInclusive, maxExclusive) tartományban.
        /// </summary>
        /// <remarks>
        /// Elutasításos módszer: a modulo önmagában torzítana, ha a tartomány nem
        /// osztja a 2^64-et. Az elutasítás determinisztikus — a sampleIndex nő,
        /// nem egy belső állapot. A várható iterációszám kevesebb, mint 2.
        /// </remarks>
        public static long SampleInt(
            ulong worldSeed, uint domainId, ulong spatialId, ulong timeBucket,
            long minInclusive, long maxExclusive,
            uint propertyId = RandomProperty.Default, ulong sampleIndex = 0)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentException("maxExclusive must be greater than minInclusive");

            ulong span = (ulong)(maxExclusive - minInclusive);
            ulong threshold = (ulong.MaxValue % span + 1) % span;  // == 2^64 mod span

            ulong i = sampleIndex;
            while (true)
            {
                ulong x = Block(worldSeed, domainId, spatialId, timeBucket, propertyId, i).X0;
                if (x >= threshold)
                    return minInclusive + (long)(x % span);
                i++;
            }
        }

        /// <summary>Bernoulli-próba: igaz p valószínűséggel.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Chance(
            ulong worldSeed, uint domainId, ulong spatialId, ulong timeBucket,
            double probability,
            uint propertyId = RandomProperty.Default, ulong sampleIndex = 0)
        {
            return Sample(worldSeed, domainId, spatialId, timeBucket, propertyId, sampleIndex)
                   < probability;
        }

        /// <summary>
        /// Standard normális eloszlású érték, Box–Muller transzformációval.
        /// </summary>
        /// <remarks>
        /// FIGYELEM — DETERMINIZMUS-KOCKÁZAT (ND-23):
        /// Math.Log és Math.Cos NEM garantáltan bitre azonos különböző platformokon
        /// és .NET runtime-verziókon. A Math.Sqrt biztonságos (IEEE-754 korrekt
        /// kerekítést ír elő), a többi nem.
        ///
        /// Ezért ez a metódus NEM használható a szimuláció kritikus útján, amíg
        /// az ND-23 el nem dől. Jelenleg csak megjelenítési/kozmetikai célra.
        /// </remarks>
        public static double SampleGaussianUnsafe(
            ulong worldSeed, uint domainId, ulong spatialId, ulong timeBucket,
            uint propertyId = RandomProperty.Default, ulong sampleIndex = 0)
        {
            double u1, u2, unused1, unused2;
            Sample4(worldSeed, domainId, spatialId, timeBucket,
                    out u1, out u2, out unused1, out unused2, propertyId, sampleIndex);

            if (u1 <= 0.0) u1 = Scale53;   // a logaritmus miatt

            double r = Math.Sqrt(-2.0 * Math.Log(u1));
            return r * Math.Cos(2.0 * Math.PI * u2);
        }

        /// <summary>
        /// Egyenletes eloszlású egységvektor a gömbfelületen, elutasításos módszerrel.
        /// </summary>
        /// <remarks>
        /// Ez a változat BITPONTOS: csak összeadást, szorzást és Math.Sqrt-et használ,
        /// és a Math.Sqrt-re az IEEE-754 korrekt kerekítést ír elő. Nincs trigonometria.
        ///
        /// Az elutasítás determinisztikus (a sampleIndex nő). Az egységkockából
        /// mintavételezünk, a gömbön kívüli pontokat eldobjuk. Elfogadási arány
        /// pi/6, azaz kb. 52%, tehát átlagosan kb. 1.9 iteráció.
        ///
        /// Ez használható a kritikus úton — pl. Euler-pólusok generálására.
        /// </remarks>
        public static void SampleUnitVector3(
            ulong worldSeed, uint domainId, ulong spatialId, ulong timeBucket,
            out double x, out double y, out double z,
            uint propertyId = RandomProperty.Default, ulong sampleIndex = 0)
        {
            ulong i = sampleIndex;
            while (true)
            {
                double a, b, c, unused;
                Sample4(worldSeed, domainId, spatialId, timeBucket,
                        out a, out b, out c, out unused, propertyId, i);

                // [0,1) -> [-1,1)
                double px = 2.0 * a - 1.0;
                double py = 2.0 * b - 1.0;
                double pz = 2.0 * c - 1.0;

                double lenSq = px * px + py * py + pz * pz;

                // A nulla közeli pontokat is eldobjuk, hogy a normálás stabil legyen
                if (lenSq > 1e-12 && lenSq <= 1.0)
                {
                    double inv = 1.0 / Math.Sqrt(lenSq);
                    x = px * inv;
                    y = py * inv;
                    z = pz * inv;
                    return;
                }
                i++;
            }
        }
    }
}
