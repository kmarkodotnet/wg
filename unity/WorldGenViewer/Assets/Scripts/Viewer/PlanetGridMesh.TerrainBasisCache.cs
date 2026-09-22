using System;
using System.IO;
using UnityEngine;
using WorldGen.Core.Tectonics;
using WorldGen.Viewer.Lod;

namespace WorldGen.Viewer
{
    public partial class PlanetGridMesh
    {
        // ====================================================================
        // A terrain-bazis LEMEZ-GYORSITOTARA. KET fogyasztoja van, ugyanezzel
        // a fajlrendszer- es kvota-kezelessel (a formatum a Kind kulcs-elemmel
        // kuloniti el oket, ld. TerrainBasisDiskCache):
        //
        //  - STATIKUS SAROK-bazis (ND-63/ND-122, todo.md 1. tabla 9. sor):
        //    harom tomb, hideg Buildnel elesben MERVE 7,4-8,5 s, level 8-on
        //    3 x 396 294 x 48 bajt = 54,4 MiB.
        //  - HIDROLOGIAI TILE-KOZEPPONT bazis (ND-64/ND-131, todo2.md A2):
        //    egy tomb, hideg Buildnel elesben MERVE 2,27 s (a
        //    `hydrology(...) terrainBasis=` sor), level 8-on 18,0 MiB.
        //
        // A formatum es az ervenytelenites a motorfuggetlen
        // WorldGen.Viewer.Lod.TerrainBasisDiskCache-ben van, hogy az offline
        // tesztprojekt igazolhassa (TerrainBasisDiskCacheTests) - itt csak a
        // FAJLRENDSZER es a kvota kezelese marad.
        //
        // HARMAS BIZTOSITAS, hogy a gyorsitotar SOHA ne rontsa el a vilagot:
        //  1. kulcs-egyezes (seed, szint, epszilon, darabszam) a fejlecben;
        //  2. 64 bites ellenorzoosszeg a teljes tartalomra (serules/csonkolas);
        //  3. 1024 szelesen szort bejegyzes UJRASZAMOLASA es BITRE
        //     hasonlitasa - EZ zarja ki, hogy egy masik algoritmusverzioval
        //     irt fajl betoltodjon. NEM kezzel emelt verziokonstansra
        //     epitunk: az pont akkor bukna el, amikor valaki a DomainWarp-ot
        //     vagy a ComputeNoiseBasis-t modositja es elfelejti emelni.
        //
        // MINDEN I/O HIBA NYELVE: a gyorsitotar opcionalis. Barmilyen kivetel
        // eseten figyelmeztetunk es SZAMOLUNK - a Build sosem bukhat el azon,
        // hogy a lemez tele van vagy a fajl olvashatatlan.
        // ====================================================================

        [SerializeField]
        [Tooltip("Hideg Buildnél a terrain-bázisok (statikus sarok: 7,4-8,5 s; " +
                 "hidrológiai tile-középpont: 2,27 s) lemezről töltődjenek-e, ha van " +
                 "érvényes gyorsítótár. A betöltött adatot MINDIG ellenőrizzük " +
                 "(kulcs + ellenőrzőösszeg + 1024 bejegyzés újraszámolása), és bármilyen " +
                 "eltérésnél újraszámolunk. Kikapcsolva a korábbi, mindig számoló " +
                 "viselkedés fut.")]
        private bool useTerrainBasisDiskCache = true;

        /// <summary>
        /// A gyorsitotar-konyvtar felso merethatara. Level 8-on egy VILAG
        /// 54,4 MiB (sarok) + 18,0 MiB (tile-kozeppont) = 72,4 MiB, tehat ez
        /// kb. 7 vilagot tart meg; a legregebben irt fajlok esnek ki eloszor.
        /// A hatar SZANDEKOSAN konzervativ: a gyorsitotar kenyelem, nem adat.
        /// </summary>
        private const long TerrainBasisCacheQuotaBytes = 512L * 1024 * 1024;

        private static string TerrainBasisCacheDirectory =>
            Path.Combine(Application.persistentDataPath, "terrainBasisCache");

        private bool TryLoadTerrainBasisFromDisk(
            in TerrainBasisDiskCache.Key key,
            Action<int, TerrainPointBasis[][]> recompute,
            out TerrainBasisDiskCache.Payload payload)
        {
            payload = null;
            if (!useTerrainBasisDiskCache) return false;
            PurgeStaleTerrainBasisCacheFilesOnce();
            try
            {
                string path = Path.Combine(TerrainBasisCacheDirectory, key.ToFileName());
                if (!File.Exists(path)) return false;

                var timer = System.Diagnostics.Stopwatch.StartNew();
                string reason;
                using (FileStream stream = File.OpenRead(path))
                    payload = TerrainBasisDiskCache.TryRead(stream, key, recompute, out reason);

                if (payload == null)
                {
                    // Az elavult/sérült fájlt TÖRÖLJÜK: különben minden hideg
                    // Build újra beolvasná és újra elutasítaná (54 MiB I/O
                    // feleslegesen), és a kvótából is helyet foglalna.
                    PerfLog($"[terrain-basis cache] ELUTASITVA ({reason}) - torles: {key.ToFileName()}");
                    TryDelete(path);
                    return false;
                }

                PerfLog($"[terrain-basis cache] TALALAT {key.ToFileName()} "
                    + $"({new FileInfo(path).Length / 1048576.0:F1} MiB, {timer.Elapsed.TotalMilliseconds:F0} ms)");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"PlanetGridMesh: a terrain-bazis gyorsitotar olvasasa nem sikerult "
                    + $"({ex.GetType().Name}: {ex.Message}) - ujraszamolunk.");
                payload = null;
                return false;
            }
        }

        private void SaveTerrainBasisToDisk(
            in TerrainBasisDiskCache.Key key, TerrainBasisDiskCache.Payload payload)
        {
            if (!useTerrainBasisDiskCache) return;
            PurgeStaleTerrainBasisCacheFilesOnce();
            try
            {
                Directory.CreateDirectory(TerrainBasisCacheDirectory);
                string path = Path.Combine(TerrainBasisCacheDirectory, key.ToFileName());
                // ELOSZOR ideiglenes fajlba, aztan atnevezes: igy egy felbeszakadt
                // iras (osszeomlas, aramszunet) nem hagy hatra fel-fajlt, amit a
                // kovetkezo indulas beolvasna es csak az ellenorzoosszegnel bukna.
                string temporary = path + ".tmp";
                var timer = System.Diagnostics.Stopwatch.StartNew();
                using (FileStream stream = File.Create(temporary))
                    TerrainBasisDiskCache.Write(stream, key, payload);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
                PerfLog($"[terrain-basis cache] KIIRVA {key.ToFileName()} "
                    + $"({TerrainBasisDiskCache.ExpectedFileSize(key.Count, key.ArrayCount) / 1048576.0:F1} MiB, "
                    + $"{timer.Elapsed.TotalMilliseconds:F0} ms)");
                EnforceTerrainBasisCacheQuota();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"PlanetGridMesh: a terrain-bazis gyorsitotar irasa nem sikerult "
                    + $"({ex.GetType().Name}: {ex.Message}) - a Build ettol fuggetlenul helyes.");
            }
        }

        private bool _terrainBasisStaleFilesPurged;

        /// <summary>
        /// ND-131: a KORÁBBI fájlformátummal írt bejegyzések törlése, mérettől
        /// függetlenül. Ezeket soha többé nem olvassuk (a nevük sem illeszkedik
        /// a mostani kulcsra), tehát tisztán helyfoglalás lennének a kvótából -
        /// egy formátum-váltás így EGYSZER takarít maga után.
        ///
        /// Munkamenetenként egyszer fut, és a BETÖLTÉS útjáról is hívjuk, nem
        /// csak a mentéséről: egy olyan Build, ami minden bázist a
        /// gyorsítótárból kap, SOHA nem ír, tehát a kvóta-kezelés sem futna le -
        /// az elavult fájl pedig ott maradna a felhasználó lemezén.
        /// </summary>
        private void PurgeStaleTerrainBasisCacheFilesOnce()
        {
            if (_terrainBasisStaleFilesPurged || !useTerrainBasisDiskCache) return;
            _terrainBasisStaleFilesPurged = true;
            try
            {
                var directory = new DirectoryInfo(TerrainBasisCacheDirectory);
                if (!directory.Exists) return;
                foreach (FileInfo file in directory.GetFiles("basis_*.bin"))
                {
                    if (TerrainBasisDiskCache.IsCurrentFormatFileName(file.Name)) continue;
                    long size = file.Length;
                    if (TryDelete(file.FullName))
                        PerfLog($"[terrain-basis cache] ELAVULT FORMATUM - torolve: {file.Name} "
                            + $"({size / 1048576.0:F1} MiB)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"PlanetGridMesh: az elavult terrain-bazis fajlok torlese nem sikerult "
                    + $"({ex.GetType().Name}: {ex.Message}).");
            }
        }

        /// <summary>A legrégebben írt fájlok esnek ki, amíg a könyvtár a kvóta alá nem kerül.</summary>
        private void EnforceTerrainBasisCacheQuota()
        {
            try
            {
                var directory = new DirectoryInfo(TerrainBasisCacheDirectory);
                if (!directory.Exists) return;
                FileInfo[] files = directory.GetFiles("basis_*.bin");
                long total = 0;
                foreach (FileInfo file in files) total += file.Length;
                if (total <= TerrainBasisCacheQuotaBytes) return;

                Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                foreach (FileInfo file in files)
                {
                    if (total <= TerrainBasisCacheQuotaBytes) break;
                    long size = file.Length;
                    if (TryDelete(file.FullName))
                    {
                        total -= size;
                        PerfLog($"[terrain-basis cache] KVOTA - torolve: {file.Name} ({size / 1048576.0:F1} MiB)");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"PlanetGridMesh: a terrain-bazis gyorsitotar kvota-kezelese nem sikerult "
                    + $"({ex.GetType().Name}: {ex.Message}).");
            }
        }

        private static bool TryDelete(string path)
        {
            try { File.Delete(path); return true; }
            catch (Exception) { return false; }
        }
    }
}
