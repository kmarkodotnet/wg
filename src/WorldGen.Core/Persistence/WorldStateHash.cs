using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using WorldGen.Core.Grid;

namespace WorldGen.Core.Persistence
{
    /// <summary>
    /// M12 első lépése (spec §60 "World State hash", docs/04-decisions.md ND-30):
    /// determinisztikus hash egy tile-mezőről — a "worldgen verify"
    /// (ugyanaz a definíció → ugyanaz a világ minden platformon, I1)
    /// automatizált ellenőrzésének alapja.
    ///
    /// HATÓKÖR (tudatosan szűkítve): CSAK a hash-függvény. A .worldpkg
    /// fájlformátum, CLI (`worldgen verify`), event-sourcing/replay
    /// HALASZTVA — ld. ND-30. Indoklás: a Core minden része már most is
    /// TISZTA FÜGGVÉNYE a (worldSeed, paraméterek, idő)-nek — nincs
    /// "irreverzibilis" állapot, amit event-sourcing-gal kellene tárolni,
    /// tehát a hash-mechanizmus önmagában is azonnal hasznos, még a teljes
    /// perzisztencia-réteg megépítése előtt.
    ///
    /// MÓDSZER: a mezőt (TileId → érték) KANONIKUS sorrendben
    /// (TileId.Value szerint növekvő, NEM Dictionary bejárási sorrend)
    /// EXPLICIT big-endian bájtsorrendbe írjuk, majd SHA-256. A big-endian
    /// szándékosan választott, nem a platform natív bájtsorrendjére
    /// támaszkodva (elméletben ARM64 eltérő lehetne, bár a gyakorlatban
    /// minden CI célplatform little-endian) — "ne bízz implicit
    /// platform-feltételezésben", ugyanaz az elv, mint a Threefry
    /// kulcs/counter leképezésnél.
    ///
    /// SHA-256 GARANTÁLTAN determinisztikus (bit-manipuláció, nem
    /// transzcendens közelítés) — más kockázati osztály, mint a
    /// Math.Sin/Cos/Pow (ND-27); nem igényel semmilyen ND-kockázatvállalást.
    /// </summary>
    public static class WorldStateHash
    {
        /// <summary>
        /// SHA-256 hash egy (TileId → double) mezőről, kanonikus
        /// (TileId.Value szerint növekvő) sorrendben — a Dictionary
        /// bejárási sorrendjétől FÜGGETLEN eredmény.
        /// </summary>
        public static byte[] ComputeFieldHash(Dictionary<TileId, double> field)
        {
            var sortedKeys = new List<TileId>(field.Keys);
            sortedKeys.Sort((a, b) => a.Value.CompareTo(b.Value));

            byte[] buffer = new byte[sortedKeys.Count * 16];
            int offset = 0;
            foreach (TileId id in sortedKeys)
            {
                WriteUInt64BigEndian(buffer, offset, id.Value);
                offset += 8;
                long bits = BitConverter.DoubleToInt64Bits(field[id]);
                WriteUInt64BigEndian(buffer, offset, unchecked((ulong)bits));
                offset += 8;
            }

            using SHA256 sha = SHA256.Create();
            return sha.ComputeHash(buffer);
        }

        /// <summary>A hash kisbetűs hexadecimális szövegként (64 karakter).</summary>
        public static string ToHexString(byte[] hash)
        {
            char[] chars = new char[hash.Length * 2];
            for (int i = 0; i < hash.Length; i++)
            {
                byte b = hash[i];
                chars[i * 2] = HexDigit(b >> 4);
                chars[i * 2 + 1] = HexDigit(b & 0xF);
            }
            return new string(chars);
        }

        private static char HexDigit(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'a' + (nibble - 10));

        private static void WriteUInt64BigEndian(byte[] buffer, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++)
                buffer[offset + i] = (byte)(value >> (8 * (7 - i)));
        }
    }
}
