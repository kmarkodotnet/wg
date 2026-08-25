using System;
using System.Runtime.CompilerServices;

namespace WorldGen.Core.Random
{
    /// <summary>
    /// Négy 64 bites szó — a Threefry blokk kimenete. Struct, hogy ne allokáljon.
    /// </summary>
    public readonly struct Block4
    {
        public readonly ulong X0;
        public readonly ulong X1;
        public readonly ulong X2;
        public readonly ulong X3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Block4(ulong x0, ulong x1, ulong x2, ulong x3)
        {
            X0 = x0; X1 = x1; X2 = x2; X3 = x3;
        }

        public ulong this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return X0;
                    case 1: return X1;
                    case 2: return X2;
                    case 3: return X3;
                    default: throw new IndexOutOfRangeException(nameof(i));
                }
            }
        }
    }

    /// <summary>
    /// Threefry-4x64-20 counter-based blokk-cipher.
    ///
    /// Hitelesítve a DEShawResearch/random123 hivatalos kat_vectors fájljához:
    /// 9/9 vektor egyezik (13, 20 és 72 körre). A vektorok a repóban:
    /// tools/reference/kat_vectors
    ///
    /// Miért ez:
    ///  - Counter-based: nincs belső állapot, nincs hívási sorrend-függés.
    ///  - Csak összeadás, XOR, rotáció — nincs lebegőpontos művelet, tehát a
    ///    kimenet bitre azonos minden platformon, fordítón és optimalizációs szinten.
    ///  - Nincs permutációs tábla, tehát nincs inicializálási sorrendfüggés.
    /// </summary>
    public static class Threefry4x64
    {
        private const ulong SkeinKsParity = 0x1BD11BDAA9FC1A22UL;

        public const int DefaultRounds = 20;

        // Random123 rotációs konstansok, Threefry4x64.
        // Rot0 = páros körök, Rot1 = páratlan körök.
        private static readonly int[] Rot0 = { 14, 52, 23, 5, 25, 46, 58, 32 };
        private static readonly int[] Rot1 = { 16, 57, 40, 37, 33, 12, 22, 32 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotL(ulong x, int n)
        {
            return (x << n) | (x >> (64 - n));
        }

        /// <summary>
        /// A blokk-függvény. Tiszta: ugyanaz a bemenet mindig ugyanazt adja,
        /// bármilyen szálon, bármilyen sorrendben, bármilyen platformon.
        /// </summary>
        public static Block4 Compute(
            ulong ctr0, ulong ctr1, ulong ctr2, ulong ctr3,
            ulong key0, ulong key1, ulong key2, ulong key3,
            int rounds = DefaultRounds)
        {
            // Kulcs-ütemterv: az ötödik elem a paritás
            ulong ks0 = key0, ks1 = key1, ks2 = key2, ks3 = key3;
            ulong ks4 = SkeinKsParity ^ ks0 ^ ks1 ^ ks2 ^ ks3;

            // A C# ulong aritmetikája alapból unchecked — a túlcsordulás körbefordul,
            // ami itt pontosan a kívánt viselkedés.
            ulong x0 = ctr0 + ks0;
            ulong x1 = ctr1 + ks1;
            ulong x2 = ctr2 + ks2;
            ulong x3 = ctr3 + ks3;

            for (int r = 0; r < rounds; r++)
            {
                int idx = r & 7;
                int r0 = Rot0[idx];
                int r1 = Rot1[idx];

                if ((r & 1) == 0)
                {
                    // (0,1) és (2,3) párosítás
                    x0 += x1; x1 = RotL(x1, r0) ^ x0;
                    x2 += x3; x3 = RotL(x3, r1) ^ x2;
                }
                else
                {
                    // (0,3) és (2,1) párosítás
                    x0 += x3; x3 = RotL(x3, r0) ^ x0;
                    x2 += x1; x1 = RotL(x1, r1) ^ x2;
                }

                // Kulcs-injekció minden negyedik kör után
                if ((r & 3) == 3)
                {
                    ulong inj = (ulong)(r / 4 + 1);
                    x0 += KeyAt(ks0, ks1, ks2, ks3, ks4, (int)((inj + 0) % 5));
                    x1 += KeyAt(ks0, ks1, ks2, ks3, ks4, (int)((inj + 1) % 5));
                    x2 += KeyAt(ks0, ks1, ks2, ks3, ks4, (int)((inj + 2) % 5));
                    x3 += KeyAt(ks0, ks1, ks2, ks3, ks4, (int)((inj + 3) % 5));
                    x3 += inj;
                }
            }

            return new Block4(x0, x1, x2, x3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong KeyAt(ulong k0, ulong k1, ulong k2, ulong k3, ulong k4, int i)
        {
            switch (i)
            {
                case 0: return k0;
                case 1: return k1;
                case 2: return k2;
                case 3: return k3;
                default: return k4;
            }
        }
    }
}
