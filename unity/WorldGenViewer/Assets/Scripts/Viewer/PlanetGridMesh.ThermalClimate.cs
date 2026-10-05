using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Hydrology;
using WorldGen.Viewer.Lod;
using Debug = UnityEngine.Debug;

namespace WorldGen.Viewer
{
    public partial class PlanetGridMesh
    {
        // ====================================================================
        // ND-162: a jegmaszk AUTORITATIV forrasa a homodell eves eghajlata.
        //
        // MIERT NEM A BUILDBEN SZAMOLJUK. A ketmenetes eves eghajlat MERVE
        // level 5-on 39,8 s, level 6-on 117,1 s. Az ND-161 kizarta az olcsobb
        // utat (durvabb racs), tehat marad az architektura:
        //
        //  - CACHE-TALALAT: a Build MAGA tolti be (ms), es azonnal a homodell
        //    jeget hasznalja - nincs elonezet, nincs csere.
        //  - CACHE-TEVESZTES: a Build az OLCSO analitikus utat rajzolja
        //    (MERVE 684 ms level 6-on) - ez az ELONEZET -, kozben egy
        //    hatterszal eloallitja az eghajlatot, elmenti, es kér EGY ujabb
        //    Build-et. Az mar cache-talalat lesz.
        //
        // MIERT UJ BUILD, ES NEM "csak szinezes". A jegmaszk a fo terep-mesh
        // SZINET es a tile-klasszifikaciot is befolyasolja; a csak-szinezo
        // ujraepites a PlanetGridMesh.cs-ben dokumentaltan NYITOTT refaktor
        // ("nem oldjuk meg csendben"). Egy teljes Build viszont mar letezo,
        // bizonyitott ut - es cache-talalat mellett az eghajlat resze benne
        // ingyen van.
        //
        // REVIZIO: minden Build noveli a revizio-szamot. A hatterszal az
        // INDULASAKOR ervenyes reviziot hordozza, es a kesz eredmenyt csak
        // akkor vesszuk at, ha az meg mindig az aktualis - igy korabbi vilag
        // eredmenye SOHA nem kerulhet a kepre (ugyanaz a minta, mint az
        // ND-104 homezo-workerenel).
        //
        // KET DEEP-TIME FORCING (ND-162 2. pont): az ND-44 glaciacios eltolas
        // (150 Myr, +-6 K) EGYELORE megmarad a homodell eves atlagara adva,
        // mert a homodell sajat Milankovic-ciklusa (10-500 kyr) a Myr-es
        // idocsuszka felbontasan aliasol. A ket forcing osszevonasa kulon,
        // seed-toro dontes.
        // ====================================================================

        [SerializeField]
        [Tooltip("A tartós jégtakaró a HŐMODELL éves éghajlatából jöjjön-e (ND-162), " +
                 "a régi analitikus AnnualTemperatureStats helyett. Cache-találatnál " +
                 "azonnal; egyébként a Build az analitikus ELŐNÉZETET rajzolja, és a " +
                 "háttérszál elkészülte után kér egy újabb Buildet. Kikapcsolva a " +
                 "korábbi, tisztán analitikus viselkedés fut.")]
        private bool useThermalClimateIce = true;

        [SerializeField]
        [Tooltip("A BIOME hőmérséklet-tengelye és a csapadék PÁROLGÁSA is a hőmodell éves " +
                 "éghajlatából jöjjön-e (ND-164), az analitikus pillanatnyi hőmérséklet helyett. " +
                 "A biome az éves LEVEGŐ-, a párolgás az éves FELSZÍNI átlagot kapja. " +
                 "Kikapcsolva a korábbi, analitikus viselkedés fut. Csak akkor hat, ha a " +
                 "hőmodell jege is be van kapcsolva - egy világban egy hőmérséklet-forrás legyen.")]
        private bool useThermalClimateBiome = true;

        [SerializeField]
        [Tooltip("Az éves éghajlat lemez-gyorsítótára (ND-162). A betöltött adatot MINDIG " +
                 "ellenőrizzük (modellazonosító + mintanapok + jégküszöb-mód + rövid " +
                 "kanonikus előtag lenyomata + ellenőrzőösszeg); bármilyen eltérésnél " +
                 "újraszámolunk. A gyorsítótár kényelem, nem adat.")]
        private bool useThermalClimateDiskCache = true;

        /// <summary>
        /// A gyorsitotar-konyvtar felso merethatara. Level 5-on egy vilag
        /// ~0,6 MiB, level 6-on ~2,4 MiB, tehat ez tobb szaz vilagot tart meg.
        /// Szandekosan konzervativ: a gyorsitotar kenyelem, nem adat.
        /// </summary>
        private const long ThermalClimateCacheQuotaBytes = 128L * 1024 * 1024;

        /// <summary>
        /// A gyorsitotar konyvtara. AZERT MEZO, es azert a FOSZALON toltjuk fel:
        /// az `Application.persistentDataPath` UNITY API, amit KIZAROLAG a
        /// foszalrol szabad olvasni - hatterszalrol `UnityException`-t dob.
        ///
        /// ELO PLAY-BEN MERVE (2026-09-28): amikor ezt tulajdonsagkent, a
        /// workerbol olvastuk, MINDHAROM cache-muvelet (takaritas, olvasas,
        /// iras) kivetellel elszallt. A kovetkezmeny nem osszeomlas volt -
        /// a hibakat lenyeljuk -, hanem az, hogy a cache SOHA nem irodott es
        /// SOHA nem talalt, tehat minden Build ujraszamolt. Csendes, draga
        /// hiba: a funkcio "mukodott", csak epp sosem gyorsitott.
        /// </summary>
        private static string _thermalClimateCacheDirectory;

        /// <summary>A foszalrol hivando: rogziti a konyvtarat a hatterszal szamara.</summary>
        private static void EnsureThermalClimateCacheDirectory()
        {
            if (_thermalClimateCacheDirectory == null)
                _thermalClimateCacheDirectory = Path.Combine(Application.persistentDataPath, "thermalClimateCache");
        }

        private static string ThermalClimateCacheDirectory => _thermalClimateCacheDirectory;

        /// <summary>A hatterszalnak atadott, IMMUTABILIS vilag-pillanatkep.</summary>
        private sealed class ThermalClimateInputs
        {
            public int Revision;
            /// <summary>ND-192: a <see cref="ComputeInputsIdentity"/> eredménye - a
            /// solver-lenyomat memória-gyorsítótárának kulcsa.</summary>
            public ulong Identity;
            public int Level;
            public ulong Seed;
            public double SeaLevelM;
            public double TYears;
            public ThermalOrbit Orbit;
            public SurfaceThermalKind[] IceFreeKinds = Array.Empty<SurfaceThermalKind>();
            public double[] ElevationM = Array.Empty<double>();
        }

        /// <summary>A kesz eghajlat abban az alakban, ahogy a Build hasznalja.</summary>
        private sealed class ThermalClimateResult
        {
            public int Revision;
            public int Level;
            public bool FromCache;
            public double ComputeMs;
            public double PermanentIceThresholdK;
            public bool UsesPhysicalIce;
            public Dictionary<TileId, double> IcePersistenceMarginK = new Dictionary<TileId, double>();

            /// <summary>A SZARAZFOLDI eves felszini atlag - a jegmaszk bemenete (ND-162).</summary>
            public Dictionary<TileId, double> MeanSurfaceK = new Dictionary<TileId, double>();

            /// <summary>
            /// ND-164: az eves LEVEGO-kozephomerseklet MINDEN cellara - a biome
            /// homerseklet-tengelye. Azert a levego es nem a felszin, mert a
            /// Whittaker-jellegu tabla (ND-126) arra van kalibralva, es a
            /// vegetacio is a levegot "erzi" (ld. ThermalClimateCalculator.ClassifyBiomes).
            /// </summary>
            public Dictionary<TileId, double> MeanAirK = new Dictionary<TileId, double>();

            /// <summary>
            /// ND-164: az eves FELSZINI atlag MINDEN cellara (az oceanokat is
            /// beleertve) - a PAROLGAS bemenete. Azert kulon a MeanSurfaceK-tol,
            /// mert az szandekosan csak a szarazfoldet tartja (a jegmaszkhoz), a
            /// parolgas viszont EPP az ocean folott tortenik.
            /// </summary>
            public Dictionary<TileId, double> SurfaceAllK = new Dictionary<TileId, double>();

            /// <summary>ND-169: a végleges B menet éves szélvektora és átlagsebessége.</summary>
            public Dictionary<TileId, SurfaceWindSample> MeanWind = new Dictionary<TileId, SurfaceWindSample>();

            /// <summary>A cella eleavacioja - a homerseklet magassag-korrekciojahoz (ND-164).</summary>
            public Dictionary<TileId, double> ElevationM = new Dictionary<TileId, double>();

            /// <summary>Az a tengerszint, amivel az eghajlat keszult (a magassag-korrekcio nullapontja).</summary>
            public double SeaLevelM;
        }

        private int _climateRevision;
        private Task _climateTask;
        private CancellationTokenSource _climateCancel;
        private ThermalClimateInputs _climatePendingInputs;
        private volatile ThermalClimateResult _climateCompleted;
        private ThermalClimateResult _climateApplied;
        private bool _climateRebuildRequested;
        private ulong _climateInputsIdentity;
        private bool _hasClimateInputsIdentity;
        private string _climateStatus = "éghajlat: nem indult";
        private bool _climateCachePurged;

        /// <summary>Igaz, ha a jelenlegi kep MAR a homodell jegét mutatja (nem az elonezetet).</summary>
        internal bool ThermalClimateIceActive => _climateApplied != null && _climateApplied.Revision == _climateRevision;

        internal string ThermalClimateStatus => _climateStatus;

        /// <summary>
        /// A Build hivja: ha van ERVENYES, a mostani vilaghoz tartozo eghajlat,
        /// visszaadja a jegmezot; kulonben false, es a Build az analitikus
        /// elonezetet hasznalja.
        /// </summary>
        private bool TryGetThermalClimateIce(out Dictionary<TileId, double> meanSurfaceK, out double thresholdK)
        {
            meanSurfaceK = null;
            thresholdK = LakesIceErosion.PermanentIceMeanThresholdK;
            if (!useThermalClimateIce) return false;

            ThermalClimateResult completed = _climateCompleted;
            if (completed != null && completed.Revision == _climateRevision)
                _climateApplied = completed;

            if (_climateApplied == null || _climateApplied.Revision != _climateRevision) return false;
            meanSurfaceK = _climateApplied.UsesPhysicalIce
                ? _climateApplied.IcePersistenceMarginK : _climateApplied.MeanSurfaceK;
            thresholdK = _climateApplied.PermanentIceThresholdK;
            return true;
        }

        /// <summary>
        /// A Buildben, a klima fogyasztasa elott hivjuk: ez rogziti a
        /// vilag-pillanatkepet a hatterszal szamara. A JEGMENTES felszintipus-
        /// terkep itt keszul (ND-158: a jeg a homodell KIMENETE).
        ///
        /// AZ ERVENYTELENITES ITT TORTENIK, es SZANDEKOSAN nem az
        /// InvalidateAdaptiveCaches mellett: az MINDEN Buildben fut, tehat az
        /// eghajlat sosem keszulne el (a kesz eredmeny egy ujabb Buildet ker,
        /// ami azonnal ervenytelenitene - vegtelen kor). Helyette a BEMENETEK
        /// sajat azonositojat hasonlitjuk ossze: a revizio csak akkor no, ha a
        /// vilag tenylegesen mas.
        /// </summary>
        private void CaptureThermalClimateInputs(
            Dictionary<TileId, double> field, Dictionary<TileId, bool> isOceanField,
            HashSet<TileId> lakeTiles, double seaLevel, ulong seed, double axialTiltRad)
        {
            if (!useThermalClimateIce) return;

            DenseGridMetrics grid = DenseGridMetrics.Build(level);
            var kinds = new SurfaceThermalKind[grid.CellCount];
            var elevation = new double[grid.CellCount];
            int side = grid.Side;
            for (int face = 0; face < 6; face++)
                for (int u = 0; u < side; u++)
                    for (int v = 0; v < side; v++)
                    {
                        int index = DenseGridMetrics.Index(face, u, v, side);
                        TileId id = TileId.FromFaceLevelUV(face, level, (uint)u, (uint)v);
                        elevation[index] = field.TryGetValue(id, out double h) ? h : seaLevel;
                        bool ocean = isOceanField.TryGetValue(id, out bool o) ? o : elevation[index] < seaLevel;
                        // Land / Ocean / Freshwater - Ice NINCS es nem is lehet.
                        kinds[index] = ocean
                            ? SurfaceThermalKind.Ocean
                            : (lakeTiles != null && lakeTiles.Contains(id)
                                ? SurfaceThermalKind.Freshwater
                                : SurfaceThermalKind.Land);
                    }

            var inputs = new ThermalClimateInputs
            {
                Level = level,
                Seed = seed,
                SeaLevelM = seaLevel,
                TYears = deepTimeMyr * 1.0e6,
                Orbit = new ThermalOrbit(climateOrbitalPeriodDays, climateRotationPeriodDays, axialTiltRad),
                IceFreeKinds = kinds,
                ElevationM = elevation,
            };
            ulong identity = ComputeInputsIdentity(inputs);
            inputs.Identity = identity;
            bool sameWorld = _hasClimateInputsIdentity && identity == _climateInputsIdentity;

            // Van-e BARMI, ami ebbol az eghajlatbol meg elo? A pending bemenet,
            // egy futo munka, egy kesz eredmeny vagy egy mar atvett eredmeny.
            bool anyLiveWork = _climatePendingInputs != null || _climateTask != null
                || _climateCompleted != null || _climateApplied != null;

            if (sameWorld && anyLiveWork)
            {
                // Ugyanaz a vilag: a mar kesz vagy epp futo munka ervenyes marad.
                if (_climatePendingInputs != null) _climatePendingInputs.Revision = _climateRevision;
                return;
            }

            if (sameWorld)
            {
                // UGYANAZ a vilag, de MINDEN allapot eltunt. Ez a DOMAIN RELOAD
                // (script-ujraforditas Play kozben, vagy Play-be lepes reload-dal):
                // a Unity az ERTEKTIPUSU mezoket atmenti, a REFERENCIAKAT nem -
                // tehat a `_climateInputsIdentity` es a `_hasClimateInputsIdentity`
                // TULELI, a `_climatePendingInputs` / `_climateApplied` /
                // `_climateCompleted` / `_climateTask` viszont null lesz.
                //
                // A regi kod ilyenkor a fenti "ugyanaz a vilag" agon ment ki, es
                // SOHA nem fegyverezte ujra a bemenetet: az UpdateThermalClimate
                // `inputs == null` miatt sosem indult el, tehat az eghajlat a
                // session vegeig HALOTT maradt - a jeg, a biome es a parolgas
                // nemán az analitikus elonezeten ragadt. Elo Play-ben megfigyelve
                // (2026-09-28): rev=5, pending/applied/completed mind null,
                // valtozatlan azonosito mellett.
                //
                // Ujrafegyverzes REVIZIO-EMELES NELKUL: a vilag nem valtozott,
                // tehat a lemez-cache talalni fog, es ez ms-ba kerul.
                inputs.Revision = _climateRevision;
                _climatePendingInputs = inputs;
                _climateStatus = "éghajlat: újrafegyverezve (domain reload után)";
                return;
            }

            _climateInputsIdentity = identity;
            _hasClimateInputsIdentity = true;
            _climateRevision++;
            _climateCancel?.Cancel();
            _climateApplied = null;
            inputs.Revision = _climateRevision;
            _climatePendingInputs = inputs;
            _climateStatus = "éghajlat: új világ, számítás következik";
        }

        /// <summary>
        /// A bemenetek olcso azonositoja. NEM kriptografiai: az a dolga, hogy
        /// a vilag VALTOZASAT elkapja, nem az, hogy tamadas ellen vedjen - a
        /// tenyleges betoltes-ellenorzes a ThermalClimateDiskCache dolga (ND-143
        /// modellazonosito + solver-lenyomat + ellenorzoosszeg).
        /// </summary>
        private static ulong ComputeInputsIdentity(ThermalClimateInputs inputs)
        {
            ulong hash = 1469598103934665603UL;
            // A Play közbeni script-frissítésnél azonos fizikai bemenetek mellett
            // is más klímát adhat az új algoritmus. A revíziós kapu ezért a
            // verziókat is olvassa, nem csak a lemez-cache kulcsa.
            hash = MixIdentity(hash, (ulong)ThermalModelParameters.ModelVersion);
            foreach (char digit in WorldGen.Core.Persistence.WorldGeneratorVersion.Current)
                hash = MixIdentity(hash, digit);
            hash = MixIdentity(hash, (ulong)inputs.Level);
            hash = MixIdentity(hash, inputs.Seed);
            hash = MixIdentity(hash, (ulong)BitConverter.DoubleToInt64Bits(inputs.SeaLevelM));
            hash = MixIdentity(hash, (ulong)BitConverter.DoubleToInt64Bits(inputs.TYears));
            hash = MixIdentity(hash, (ulong)BitConverter.DoubleToInt64Bits(inputs.Orbit.OrbitalPeriodDays));
            hash = MixIdentity(hash, (ulong)BitConverter.DoubleToInt64Bits(inputs.Orbit.RotationPeriodDays));
            hash = MixIdentity(hash, (ulong)BitConverter.DoubleToInt64Bits(inputs.Orbit.AxialTiltRad));
            for (int c = 0; c < inputs.ElevationM.Length; c++)
            {
                hash = MixIdentity(hash, (ulong)BitConverter.DoubleToInt64Bits(inputs.ElevationM[c]));
                hash = MixIdentity(hash, (byte)inputs.IceFreeKinds[c]);
            }
            return hash;
        }

        private static ulong MixIdentity(ulong hash, ulong value)
        {
            for (int b = 0; b < 8; b++)
            {
                hash ^= (byte)(value >> (b * 8));
                hash *= 1099511628211UL;
            }
            return hash;
        }

        /// <summary>Az Update-bol: inditas, illetve a kesz eredmeny atvetele.</summary>
        private void UpdateThermalClimate()
        {
            if (!useThermalClimateIce) return;

            ThermalClimateResult completed = _climateCompleted;
            if (completed != null && completed.Revision == _climateRevision && _climateApplied != completed)
            {
                // A kesz eghajlat egy UJABB Build-del kerul kepre: az mar
                // cache-talalat lesz, tehat a Build maga tolti be.
                _climateApplied = completed;
                _climateRebuildRequested = true;
                _climateStatus = completed.FromCache
                    ? $"éghajlat: gyorsítótárból ({completed.ComputeMs:F0} ms)"
                    : $"éghajlat: kiszámolva ({completed.ComputeMs / 1000.0:F1} s)";
                PerfLog($"thermalClimate ready(level={completed.Level}, fromCache={completed.FromCache}) "
                    + $"= {completed.ComputeMs:F1}ms");
            }

            if (_climateTask != null && _climateTask.IsCompleted)
            {
                if (_climateTask.IsFaulted)
                {
                    Debug.LogWarning($"Éves éghajlat háttérmunka hibája: {_climateTask.Exception?.GetBaseException()}");
                    _climateStatus = "éghajlat: hiba (ld. Console)";
                }
                _climateTask = null;
            }

            if (_climateTask != null) return;
            ThermalClimateInputs inputs = _climatePendingInputs;
            if (inputs == null || inputs.Revision != _climateRevision) return;
            if (_climateApplied != null && _climateApplied.Revision == _climateRevision) return;

            // A Unity API-t IGENYLO reszeket MEG a foszalon intezzuk el.
            EnsureThermalClimateCacheDirectory();

            _climateCancel?.Dispose();
            _climateCancel = new CancellationTokenSource();
            CancellationToken token = _climateCancel.Token;
            _climateStatus = "éghajlat: számítás fut (háttérszálon)";
            _climateTask = Task.Run(() => RunThermalClimateJob(inputs, token), token);
        }

        /// <summary>
        /// ND-164: a BIOME es a PAROLGAS homerseklet-mezoi, ha van ervenyes
        /// eghajlat ehhez a vilaghoz. Ugyanaz a kapu, mint a jegnel: a
        /// TryGetThermalClimateIce mar atvette a kesz eredmenyt, ez csak
        /// kiolvassa - igy a harom fogyaszto SOSEM lathat kulonbozo evet.
        /// </summary>
        private bool TryGetThermalClimateBiomeFields(
            out Dictionary<TileId, double> meanAirK, out Dictionary<TileId, double> surfaceAllK,
            out Dictionary<TileId, double> elevationM, out double climateSeaLevelM,
            out Dictionary<TileId, SurfaceWindSample> meanWind)
        {
            meanAirK = null;
            surfaceAllK = null;
            elevationM = null;
            climateSeaLevelM = 0.0;
            meanWind = null;
            if (!useThermalClimateIce || !useThermalClimateBiome) return false;
            if (_climateApplied == null || _climateApplied.Revision != _climateRevision) return false;
            if (_climateApplied.MeanAirK.Count == 0) return false;

            meanAirK = _climateApplied.MeanAirK;
            surfaceAllK = _climateApplied.SurfaceAllK;
            elevationM = _climateApplied.ElevationM;
            climateSeaLevelM = _climateApplied.SeaLevelM;
            meanWind = _climateApplied.MeanWind;
            return true;
        }

        /// <summary>Igaz, ha a kesz eghajlat miatt UJ Build kell (es a kerest el is fogyasztja).</summary>
        private bool ConsumeThermalClimateRebuildRequest()
        {
            if (!_climateRebuildRequested) return false;
            _climateRebuildRequested = false;
            return true;
        }

        private void RunThermalClimateJob(ThermalClimateInputs inputs, CancellationToken token)
        {
            var timer = Stopwatch.StartNew();
            // ND-192 MERES: a job fazisidoi. A `fromCache=True` menetek is
            // 4,2-6,1 s-ot vettek, ami NEM lehet mind fajlolvasas - a
            // dupla Build megszunteteséhez tudni kell, melyik fazis mennyi.
            var phase = Stopwatch.StartNew();
            DenseGridMetrics grid = DenseGridMetrics.Build(inputs.Level);
            double gridMs = phase.Elapsed.TotalMilliseconds; phase.Restart();
            token.ThrowIfCancellationRequested();

            long[] sampleDays = ThermalAnnualStatisticsCalculator.SampleDayIndices(
                inputs.Orbit.OrbitalPeriodDays, ThermalAnnualStatisticsCalculator.DefaultSampleDays);

            // ND-192: a MEZO felepitese KESON tortenik - csak akkor, ha tenylegesen
            // kell. A mezo ket dologhoz kellhet: a cache-KULCSHOZ (modellazonosito
            // + solver-lenyomat) es a teljes szamitashoz. MERVE: cache-talalatnal a
            // job 6380 ms-abol a mezo 5838 ms (91%), a lemez-olvasas 5,8 ms (0,09%)
            // - vagyis az ismetelt deep-time lepteteskor masodpercekig epitunk fel
            // egy mezot, amit aztan eldobunk. Ezert a KULCSOT session-memoriaban
            // tartjuk a bemenet-azonosito mellett.
            //
            // MIERT BIZTONSAGOS. A memoria-gyorsitotar nem kerüli meg a cache
            // helyesseg-ellenorzeset: a kulccsal beolvasott fajlt a
            // ThermalClimateDiskCache.TryRead ugyanugy validalja (modellazonosito,
            // mintanapok, percentilis, lenyomat, ellenorzoosszeg), tehat egy hibas
            // memoria-bejegyzes NEM tud rossz tartalmat behozni - legrosszabb
            // esetben teveszt, es ujraszamolunk. A gyorsitotar PELDANY-mezo (nem
            // static), ezert a Play kozbeni szkript-ujraforditas (domain reload)
            // kiuriti - egy numerikusan megvaltozott kod sosem lat elavult kulcsot.
            //
            // A lenyomathoz es a szamitashoz UGYANAZ a mezo kell, kulonben a
            // cache nem azt validalna, amit futtatunk - ezert ha a mezot fel kell
            // epiteni, MINDKET fogyasztoja ugyanazt a peldanyt kapja.
            // MERVE az Editorban (Mono, level 5, 6144 cella): a parhuzamos
            // lokalis lepes NEM gyorsit - 200 tick szekvencialisan 3,56 ms/tick,
            // parhuzamosan 3,55 ms/tick. A Parallel.For tickenkenti particionalasi
            // koltsege Monoban felemeszti a nyereseget ekkora munkacsomagnal
            // (tickenkent egyetlen, 6144 elemu ciklus). Play kozben ez ROSSZABB
            // is: a TPL ugyanazon a ThreadPoolon versenyez az Editor sajat
            // munkaival. Ezert a viewer-oldali worker SZEKVENCIALISAN lep;
            // a .NET 8 CLI-mérés (ahol a parhuzamositas segit) ettol fuggetlen.
            SurfaceTemperatureField field = null;
            double fieldMs = 0, fingerprintMs = 0;
            long percentileBits = ThermalClimateDiskCache.Key.PercentileBits(
                ThermalIceClassification.DefaultPermanentIcePercentile);
            bool keyFromMemory = TryGetCachedClimateKey(inputs.Identity,
                out string modelIdentity, out ulong fingerprint);
            if (!keyFromMemory)
            {
                field = new SurfaceTemperatureField(grid, inputs.IceFreeKinds, inputs.ElevationM,
                    inputs.SeaLevelM, inputs.Seed, inputs.TYears, inputs.Orbit);
                fieldMs = phase.Elapsed.TotalMilliseconds; phase.Restart();
                modelIdentity = field.ModelIdentity;
                fingerprint = ThermalClimateDiskCache.ComputeSolverFingerprint(field, 0);
                fingerprintMs = phase.Elapsed.TotalMilliseconds;
                RememberClimateKey(inputs.Identity, modelIdentity, fingerprint);
            }
            phase.Restart();
            token.ThrowIfCancellationRequested();

            var key = new ThermalClimateDiskCache.Key(modelIdentity, grid.CellCount, sampleDays,
                percentileBits, fingerprint);

            ThermalClimateDiskCache.Payload payload = TryLoadThermalClimateFromDisk(key);
            double diskMs = phase.Elapsed.TotalMilliseconds; phase.Restart();
            bool fromCache = payload != null;
            if (payload == null)
            {
                // Teveszteskor a mezo mindenkeppen kell. Ha a kulcs memoriabol
                // jott, MOST epitjuk fel - es a kulcsot a mezobol ujraszamoljuk,
                // hogy a MENTES biztosan a tenyleges tartalomhoz tartozzon.
                if (field == null)
                {
                    field = new SurfaceTemperatureField(grid, inputs.IceFreeKinds, inputs.ElevationM,
                        inputs.SeaLevelM, inputs.Seed, inputs.TYears, inputs.Orbit);
                    fieldMs = phase.Elapsed.TotalMilliseconds; phase.Restart();
                    string verifiedIdentity = field.ModelIdentity;
                    ulong verifiedFingerprint = ThermalClimateDiskCache.ComputeSolverFingerprint(field, 0);
                    fingerprintMs = phase.Elapsed.TotalMilliseconds; phase.Restart();
                    if (verifiedIdentity != modelIdentity || verifiedFingerprint != fingerprint)
                    {
                        // Azonos bemenetre azonos kulcs jar, tehat ez nem fordulhat
                        // elo; ha megis, a memoria-bejegyzes a hibas. Eldobjuk, es a
                        // TENYLEGES kulccsal meg egyszer megprobaljuk a lemezt.
                        ForgetClimateKeys();
                        modelIdentity = verifiedIdentity;
                        fingerprint = verifiedFingerprint;
                        RememberClimateKey(inputs.Identity, modelIdentity, fingerprint);
                        key = new ThermalClimateDiskCache.Key(modelIdentity, grid.CellCount, sampleDays,
                            percentileBits, fingerprint);
                        payload = TryLoadThermalClimateFromDisk(key);
                        fromCache = payload != null;
                    }
                }
            }
            if (payload == null)
            {
                ThermalClimate climate = ThermalClimateCalculator.Compute(grid, inputs.IceFreeKinds,
                    inputs.ElevationM, inputs.SeaLevelM, inputs.Seed, inputs.TYears, inputs.Orbit,
                    useParallelLocalStep: false, includeRefinedWind: true);
                token.ThrowIfCancellationRequested();
                payload = ThermalClimateDiskCache.Payload.From(climate);
                SaveThermalClimateToDisk(key, payload);
            }
            double computeMs = phase.Elapsed.TotalMilliseconds; phase.Restart();

            // ND-162: a deep-time glaciacios eltolas (ND-44) EGYELORE a
            // homodell eves atlagara adodik - ld. a fajl fejlecet.
            double glaciationOffsetK = WorldGen.Core.Tectonics.DeepTimeErosionGlaciation.GlobalTempOffset(
                inputs.TYears / 1.0e6);
            if (payload.UsesPhysicalIce) glaciationOffsetK = 0.0; // A Core energiamérlegében már szerepel.

            var result = new ThermalClimateResult
            {
                Revision = inputs.Revision,
                Level = inputs.Level,
                FromCache = fromCache,
                ComputeMs = timer.Elapsed.TotalMilliseconds,
                PermanentIceThresholdK = payload.RefinedThresholdK + glaciationOffsetK,
                UsesPhysicalIce = payload.UsesPhysicalIce,
                SeaLevelM = inputs.SeaLevelM,
            };
            int side = grid.Side;
            for (int face = 0; face < 6; face++)
                for (int u = 0; u < side; u++)
                    for (int v = 0; v < side; v++)
                    {
                        int index = DenseGridMetrics.Index(face, u, v, side);
                        TileId id = TileId.FromFaceLevelUV(face, inputs.Level, (uint)u, (uint)v);
                        // ND-164: a deep-time glaciacios eltolas (ND-44) ITT MAR a
                        // biome tengelyere IS ramegy, nem csak a jegre. A korabbi,
                        // analitikus uton ez szandekosan szuk hatokoru volt ("kulon
                        // munka lenne a Temperature minden hivasi helyere athuzni"),
                        // de most EGY mezo taplalja a jeget ES a biome-ot: ha csak a
                        // jeg mozdulna a deep-time csuszkaval, a ket reteg ugyanazon
                        // a kepen mondana mast.
                        result.MeanAirK[id] = payload.Refined.MeanAirK[index] + glaciationOffsetK;
                        result.SurfaceAllK[id] = payload.Refined.MeanSurfaceK[index] + glaciationOffsetK;
                        result.MeanWind[id] = new SurfaceWindSample(
                            payload.Refined.MeanWindX[index], payload.Refined.MeanWindY[index],
                            payload.Refined.MeanWindZ[index], payload.Refined.MeanWindSpeedMs[index]);
                        result.ElevationM[id] = inputs.ElevationM[index];
                        if (payload.UsesPhysicalIce) result.IcePersistenceMarginK[id] = payload.IcePersistenceMarginK[index];
                        if (inputs.IceFreeKinds[index] == SurfaceThermalKind.Ocean) continue; // SeaIce a biome-bol
                        result.MeanSurfaceK[id] = payload.Refined.MeanSurfaceK[index] + glaciationOffsetK;
                    }

            token.ThrowIfCancellationRequested();
            PerfLog($"[ND-192 climate job] fromCache={fromCache} keyFromMemory={keyFromMemory} "
                + $"totalMs={timer.Elapsed.TotalMilliseconds:F1} "
                + $"grid={gridMs:F1} field={fieldMs:F1} fingerprint={fingerprintMs:F1} disk={diskMs:F1} "
                + $"compute={computeMs:F1} unpack={phase.Elapsed.TotalMilliseconds:F1}");
            _climateCompleted = result;
        }

        /// <summary>
        /// ND-192: bemenet-azonosito -> (modellazonosito, solver-lenyomat), azaz a
        /// lemez-gyorsitotar KULCSA. A tartalom helyesseget tovabbra is a
        /// ThermalClimateDiskCache.TryRead ellenorzi.
        ///
        /// ND-195 (2026-10-05): a tabla MOSTANTOL LEMEZEN IS megmarad
        /// (<see cref="ClimateKeyFileName"/>), mert peldany-mezoként minden
        /// INDITASKOR ures volt - es epp az indulas az az eset, amikor a
        /// szinkron betoltes (<see cref="TryLoadThermalClimateDuringBuild"/>)
        /// a legtobbet erne. MERVE harom menetben: a lemez-cache MEGVAN
        /// (fromCache=True, compute=0,0 ms), megis 6662/7119/7872 ms-ot
        /// vartunk, amibol 6234/6327/7359 ms (~90%) a SurfaceTemperatureField
        /// felepitese - KIZAROLAG a kulcs eloallitasahoz. Kozben a tenyleges
        /// lemez-olvasas 5,7-115,4 ms. A felhasznalo ebbol azt latta, hogy az
        /// indulas utan par masodperccel a biome atrendezodik (1402/6144 tile).
        /// </summary>
        private Dictionary<ulong, (string ModelIdentity, ulong Fingerprint)> _climateKeyMemory;

        /// <summary>Nehany vilag eleg: a felhasznalo par idopont kozott lepteti a csuszkat.</summary>
        private const int ClimateKeyMemoryLimit = 24;

        /// <summary>ND-195: a perzisztalt kulcstabla fajlneve a gyorsitotar-konyvtarban.</summary>
        private const string ClimateKeyFileName = "climate_keys.v1.txt";

        /// <summary>ND-195: a fajl elso sorat jelolo magic - formatum-valtasnal emeld.</summary>
        private const string ClimateKeyFileMagic = "WGTCLIMKEY1";

        private bool _climateKeysLoadedFromDisk;
        private readonly object _climateKeyFileGate = new object();

        /// <summary>
        /// ND-195: a MODELL-oldali verzio-kapu a perzisztalt tablahoz, mezo-epites
        /// NELKUL. Egy FIX, szintetikus (level 1, a DenseGridMetrics legkisebb
        /// engedett szintje - a level 0 ArgumentOutOfRangeException) bemenetre
        /// vett ModelIdentity:
        /// azert pont ez, mert UGYANAZ a kod
        /// (ThermalCheckpoint.ComputeModelIdentity) szamolja, amelyik a valodi
        /// kulcsot is - igy a parameter-lista NINCS ketszer leirva, tehat egy uj
        /// modell-parameter nem tud csendben kimaradni a kapubol.
        ///
        /// MIERT KELL. A peldany-mezos valtozat biztonsagi erve az volt, hogy a
        /// domain reload kiuriti, tehat "egy numerikusan megvaltozott kod sosem
        /// lat elavult kulcsot". A perzisztalas ezt az ervet elveszi, ezert
        /// potolni kell. A ComputeInputsIdentity mar olvassa a
        /// ThermalModelParameters.ModelVersion-t es a
        /// WorldGeneratorVersion.Current-et, de NEM olvassa a
        /// ThermalModelParameters.Default egyedi ertekeit (napallando, albedok,
        /// emisszivitasok, hokapacitasok, meridionalis skala, szezonalis
        /// fazisok) es a bolygo-sugarat - pont azokat, amiket egy A/B meres
        /// modellverzio-emeles NELKUL allit at (ld. az ND-160 figyelmezteteset a
        /// ComputeModelIdentity-ben). A ComputeModelIdentity MINDET olvassa,
        /// tehat ez a proba-azonosito mindegyiken valt.
        ///
        /// Ha a fajl fejlecebe irt proba-azonosito nem egyezik a mostanival, az
        /// EGESZ tabla elavult: eldobjuk, es a regi ut fut (mezo-epites). Ez
        /// idobe kerul, tartalmi kovetkezmenye nincs.
        /// </summary>
        private static string _climateKeyModelProbe;

        private static string ClimateKeyModelProbe()
        {
            if (_climateKeyModelProbe != null) return _climateKeyModelProbe;
            DenseGridMetrics probeGrid = DenseGridMetrics.Build(1);
            var kinds = new SurfaceThermalKind[probeGrid.CellCount];
            var elevation = new double[probeGrid.CellCount];
            var probe = new SurfaceTemperatureField(probeGrid, kinds, elevation,
                0.0, 0UL, 0.0, new ThermalOrbit(1.0, 1.0, 0.0));
            _climateKeyModelProbe = probe.ModelIdentity;
            return _climateKeyModelProbe;
        }

        private bool TryGetCachedClimateKey(ulong identity, out string modelIdentity, out ulong fingerprint)
        {
            modelIdentity = null;
            fingerprint = 0;
            EnsureClimateKeysLoaded();
            Dictionary<ulong, (string ModelIdentity, ulong Fingerprint)> memory = _climateKeyMemory;
            if (memory == null) return false;
            lock (memory)
            {
                if (!memory.TryGetValue(identity, out var entry)) return false;
                modelIdentity = entry.ModelIdentity;
                fingerprint = entry.Fingerprint;
            }
            return modelIdentity != null;
        }

        private void RememberClimateKey(ulong identity, string modelIdentity, ulong fingerprint)
        {
            if (modelIdentity == null) return;
            Dictionary<ulong, (string, ulong)> memory = _climateKeyMemory;
            if (memory == null) _climateKeyMemory = memory = new Dictionary<ulong, (string, ulong)>();
            lock (memory)
            {
                // Egyszeru felso korlat: tulcsorduláskor uritunk. A gyorsitotar
                // kenyelem - egy uritesnek csak ido-, nem tartalom-kovetkezmenye van.
                if (memory.Count >= ClimateKeyMemoryLimit) memory.Clear();
                memory[identity] = (modelIdentity, fingerprint);
            }
            SaveClimateKeysToDisk();
        }

        private void ForgetClimateKeys()
        {
            Dictionary<ulong, (string, ulong)> memory = _climateKeyMemory;
            if (memory == null) return;
            lock (memory) memory.Clear();
            // ND-195: a lemezrol IS tunjon el - kulonben a hibas bejegyzes a
            // kovetkezo inditasnal visszajon, es ugyanaz az onjavito kor fut le
            // megint. A fajl ujrairasa (legfeljebb 24 sor) ms-os muvelet.
            SaveClimateKeysToDisk();
        }

        /// <summary>
        /// ND-195: a perzisztalt kulcstabla betoltese, egyszer peldanyonkent.
        /// Hibanal (nincs fajl, serult sor, nem egyezo fejlec) CSENDBEN ures
        /// marad: a kovetkezmeny a REGI ut (mezo-epites), nem rossz adat.
        ///
        /// A konyvtarat a FOSZAL rogziti (Application.persistentDataPath Unity
        /// API) - ha meg nincs, ez a hivas nem tolt be, es a kovetkezo
        /// (fo szalrol inditott) kor ujraprobalja.
        /// </summary>
        private void EnsureClimateKeysLoaded()
        {
            if (_climateKeysLoadedFromDisk) return;
            string directory = ThermalClimateCacheDirectory;
            if (directory == null) return;
            lock (_climateKeyFileGate)
            {
                if (_climateKeysLoadedFromDisk) return;
                _climateKeysLoadedFromDisk = true;
                try
                {
                    string path = Path.Combine(directory, ClimateKeyFileName);
                    if (!File.Exists(path)) return;
                    string[] lines = File.ReadAllLines(path);
                    if (lines.Length == 0) return;
                    string[] header = lines[0].Split(' ');
                    if (header.Length != 2 || header[0] != ClimateKeyFileMagic) return;
                    if (header[1] != ClimateKeyModelProbe())
                    {
                        // Megvaltozott modell/parameterek: az egesz tabla elavult.
                        PerfLog("[ND-195 climate keys] discarded=stale-model-probe");
                        return;
                    }
                    var loaded = new Dictionary<ulong, (string, ulong)>();
                    for (int i = 1; i < lines.Length && loaded.Count < ClimateKeyMemoryLimit; i++)
                    {
                        string[] parts = lines[i].Split(' ');
                        if (parts.Length != 3) continue;
                        if (!ulong.TryParse(parts[0], NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out ulong identity)) continue;
                        if (parts[1].Length == 0) continue;
                        if (!ulong.TryParse(parts[2], NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out ulong fingerprint)) continue;
                        loaded[identity] = (parts[1], fingerprint);
                    }
                    if (loaded.Count == 0) return;
                    Dictionary<ulong, (string, ulong)> existing = _climateKeyMemory;
                    if (existing == null)
                    {
                        _climateKeyMemory = loaded;
                    }
                    else
                    {
                        // A MOSTANI session bejegyzesei nyernek: azok a FUTO kodtol
                        // szarmaznak, nem egy korabbi mentestol.
                        lock (existing)
                            foreach (KeyValuePair<ulong, (string, ulong)> kv in loaded)
                                if (!existing.ContainsKey(kv.Key)) existing[kv.Key] = kv.Value;
                    }
                    PerfLog("[ND-195 climate keys] loaded=" + loaded.Count);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("ND-195: az éghajlat-kulcstábla olvasása nem sikerült ("
                        + ex.GetType().Name + ": " + ex.Message + ") - mezőépítés következik.");
                }
            }
        }

        /// <summary>
        /// ND-195: a tabla kiirasa. Teljes ujrairas, mert legfeljebb
        /// <see cref="ClimateKeyMemoryLimit"/> sor - nincs ertelme inkrementalis
        /// formatumot epiteni hozza. Hiba eseten csendben kihagyjuk: a
        /// gyorsitotar kenyelem, nem adat.
        /// </summary>
        private void SaveClimateKeysToDisk()
        {
            string directory = ThermalClimateCacheDirectory;
            if (directory == null) return;
            Dictionary<ulong, (string ModelIdentity, ulong Fingerprint)> memory = _climateKeyMemory;
            if (memory == null) return;
            try
            {
                var builder = new StringBuilder();
                builder.Append(ClimateKeyFileMagic).Append(' ').Append(ClimateKeyModelProbe()).Append('\n');
                lock (memory)
                {
                    foreach (KeyValuePair<ulong, (string ModelIdentity, ulong Fingerprint)> kv in memory)
                        builder.Append(kv.Key.ToString("x16", CultureInfo.InvariantCulture)).Append(' ')
                            .Append(kv.Value.ModelIdentity).Append(' ')
                            .Append(kv.Value.Fingerprint.ToString("x16", CultureInfo.InvariantCulture))
                            .Append('\n');
                }
                lock (_climateKeyFileGate)
                {
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, ClimateKeyFileName), builder.ToString());
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("ND-195: az éghajlat-kulcstábla írása nem sikerült ("
                    + ex.GetType().Name + ": " + ex.Message + ") - a gyorsítótár működik, csak nem perzisztál.");
            }
        }

        /// <summary>
        /// ND-192: a Build MAGA tolti be a kesz eghajlatot, ha az ehhez a vilaghoz
        /// mar a lemezen van ES a cache-kulcsot session-memoriabol tudjuk (tehat a
        /// betoltes nehany ms). Ezzel a dokumentalt szandek teljesul - "cache-
        /// talalat: a Build maga tolti be, nincs elonezet, nincs csere" -, es
        /// elmarad a MASODIK, teljes Build (MERVE 9,0-12,4 s).
        ///
        /// Ha a kulcs NINCS memoriaban, ez a metodus AZONNAL visszater: a kulcs
        /// eloallitasa (mezo + lenyomat) MERVE 6,3 s, amit nem rakunk a fo szalra.
        /// Olyankor a megszokott ut fut: analitikus elonezet + hatterszal + egy
        /// ujabb Build.
        /// </summary>
        private void TryLoadThermalClimateDuringBuild()
        {
            if (!useThermalClimateIce) return;
            ThermalClimateInputs inputs = _climatePendingInputs;
            if (inputs == null || inputs.Revision != _climateRevision) return;
            if (_climateApplied != null && _climateApplied.Revision == _climateRevision) return;
            if (_climateTask != null) return; // Mar fut a hatterszal: ne ketszerezzuk.
            // ND-195: a konyvtar MEG a kulcs-kereses elott - a perzisztalt
            // kulcstabla ebbol a konyvtarbol tolt be, es ez a FOSZAL (a
            // persistentDataPath Unity API). Enelkul az elso Build sosem latna a
            // lemezen levo tablat, es epp az inditas maradna a regi uton.
            EnsureThermalClimateCacheDirectory();
            if (!TryGetCachedClimateKey(inputs.Identity, out string cachedIdentity, out ulong cachedFingerprint))
                return;
            // ND-195: a KULCS megleteben nem szabad megbizni onmagaban. A kulcs
            // mostantol PERZISZTENS, a .bin fajlokra viszont vonatkozik a
            // kvota-takaritas (EnforceThermalClimateCacheQuota) - tehat
            // eloallhat, hogy a kulcsot tudjuk, de a fajl mar nincs ott. Ha
            // ilyenkor belepnenk a szinkron utra, a RunThermalClimateJob a FO
            // SZALON epitene mezot (6,2-7,4 s) es szamolna teljes eghajlatot
            // (hidegen ~118 s level 5-on) - vagyis a kepernyo megfagyna. Ezert
            // itt a FAJL letezeset is ellenorizzuk; ha nincs, a megszokott ut
            // fut (analitikus elonezet + hatterszal), ami SOSEM fagyaszt.
            if (!ClimateCacheFileExists(inputs, cachedIdentity, cachedFingerprint))
            {
                PerfLog("[ND-195 build climate load] skipped=cache-file-missing");
                return;
            }
            var timer = Stopwatch.StartNew();
            try
            {
                // Ugyanaz a jobtest fut, csak a FO szalon: a kulcs memoriabol jon,
                // a lemez-olvasas MERVE 5,8 ms, a kipakolas 1,8 ms.
                RunThermalClimateJob(inputs, CancellationToken.None);
            }
            catch (Exception error)
            {
                // A szinkron ut SOSEM allithatja meg a Buildet: ha nem sikerult, a
                // Build az analitikus elonezettel megy tovabb, es a hatterszal
                // ujraprobalja.
                Debug.LogWarning($"ND-192: az éghajlat Build-beli betöltése nem sikerült ({error.GetType().Name}: {error.Message}) - előnézet következik.");
                PerfLog($"[ND-192 build climate load] failed={error.GetType().Name} ms={timer.Elapsed.TotalMilliseconds:F1}");
                return;
            }

            ThermalClimateResult completed = _climateCompleted;
            if (completed == null || completed.Revision != _climateRevision)
            {
                PerfLog($"[ND-192 build climate load] applied=False ms={timer.Elapsed.TotalMilliseconds:F1}");
                return;
            }
            // A Build MOST fogja fogyasztani: a jelolo nelkul a kesz eredmeny egy
            // ujabb Buildet kerne - epp azt, amit el akarunk kerulni.
            _climateApplied = completed;
            _climateRebuildRequested = false;
            _climateStatus = $"éghajlat: Build-ben betöltve ({completed.ComputeMs:F0} ms)";
            PerfLog($"[ND-192 build climate load] applied=True fromCache={completed.FromCache} "
                + $"ms={timer.Elapsed.TotalMilliseconds:F1}");
        }

        /// <summary>
        /// ND-195: megvan-e a kulcshoz tartozo gyorsitotar-fajl? A kulcs tobbi
        /// eleme (cellaszam, mintanapok, jeg-percentilis) a MOSTANI kodbol es a
        /// MOSTANI bemenetbol szamolodik, pontosan ugy, mint a
        /// RunThermalClimateJob-ban - igy a vizsgalt fajlnev UGYANAZ, amit a
        /// betoltes is keresne.
        /// </summary>
        private bool ClimateCacheFileExists(ThermalClimateInputs inputs, string modelIdentity, ulong fingerprint)
        {
            string directory = ThermalClimateCacheDirectory;
            if (directory == null || modelIdentity == null) return false;
            try
            {
                long[] sampleDays = ThermalAnnualStatisticsCalculator.SampleDayIndices(
                    inputs.Orbit.OrbitalPeriodDays, ThermalAnnualStatisticsCalculator.DefaultSampleDays);
                long percentileBits = ThermalClimateDiskCache.Key.PercentileBits(
                    ThermalIceClassification.DefaultPermanentIcePercentile);
                var key = new ThermalClimateDiskCache.Key(modelIdentity,
                    DenseGridMetrics.Build(inputs.Level).CellCount, sampleDays, percentileBits, fingerprint);
                return File.Exists(Path.Combine(directory, key.ToFileName()));
            }
            catch (Exception ex)
            {
                // Nem csendes elnyeles: a kovetkezmeny az, hogy a REGI (mindig
                // helyes) ut fut, de a kiiras megmutatja, ha ez rendszeres.
                Debug.LogWarning("ND-195: a gyorsítótár-fájl ellenőrzése nem sikerült ("
                    + ex.GetType().Name + ": " + ex.Message + ") - előnézet következik.");
                return false;
            }
        }

        private ThermalClimateDiskCache.Payload TryLoadThermalClimateFromDisk(in ThermalClimateDiskCache.Key key)
        {
            if (!useThermalClimateDiskCache || ThermalClimateCacheDirectory == null) return null;
            PurgeStaleThermalClimateCacheFilesOnce();
            try
            {
                string path = Path.Combine(ThermalClimateCacheDirectory, key.ToFileName());
                if (!File.Exists(path)) return null;
                using FileStream stream = File.OpenRead(path);
                ThermalClimateDiskCache.Payload payload =
                    ThermalClimateDiskCache.TryRead(stream, key, out string reason);
                if (payload == null)
                    Debug.LogWarning($"Az éghajlat-gyorsítótár elutasítva ({reason}) - újraszámolás: {path}");
                return payload;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Az éghajlat-gyorsítótár olvasása nem sikerült ({ex.GetType().Name}: {ex.Message}) - újraszámolás.");
                return null;
            }
        }

        private void SaveThermalClimateToDisk(in ThermalClimateDiskCache.Key key, ThermalClimateDiskCache.Payload payload)
        {
            if (!useThermalClimateDiskCache || ThermalClimateCacheDirectory == null) return;
            try
            {
                Directory.CreateDirectory(ThermalClimateCacheDirectory);
                string path = Path.Combine(ThermalClimateCacheDirectory, key.ToFileName());
                string temporary = path + ".tmp";
                using (FileStream stream = File.Create(temporary))
                    ThermalClimateDiskCache.Write(stream, key, payload);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
                EnforceThermalClimateCacheQuota();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Az éghajlat-gyorsítótár írása nem sikerült ({ex.GetType().Name}: {ex.Message}) - a világ ettől változatlan.");
            }
        }

        /// <summary>A korabbi FORMATUMU fajlok egyszeri takaritasa (soha nem olvassuk oket ujra).</summary>
        private void PurgeStaleThermalClimateCacheFilesOnce()
        {
            if (_climateCachePurged || ThermalClimateCacheDirectory == null) return;
            _climateCachePurged = true;
            try
            {
                if (!Directory.Exists(ThermalClimateCacheDirectory)) return;
                foreach (string path in Directory.GetFiles(ThermalClimateCacheDirectory, "climate_*.bin"))
                    if (!ThermalClimateDiskCache.IsCurrentFormatFileName(Path.GetFileName(path)))
                        File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Az éghajlat-gyorsítótár takarítása nem sikerült ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        private void EnforceThermalClimateCacheQuota()
        {
            if (ThermalClimateCacheDirectory == null) return;
            try
            {
                if (!Directory.Exists(ThermalClimateCacheDirectory)) return;
                var files = new List<FileInfo>();
                foreach (string path in Directory.GetFiles(ThermalClimateCacheDirectory, "climate_*.bin"))
                    files.Add(new FileInfo(path));
                long total = 0;
                foreach (FileInfo file in files) total += file.Length;
                if (total <= ThermalClimateCacheQuotaBytes) return;

                files.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                foreach (FileInfo file in files)
                {
                    if (total <= ThermalClimateCacheQuotaBytes) break;
                    long size = file.Length;
                    file.Delete();
                    total -= size;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Az éghajlat-gyorsítótár kvótájának betartása nem sikerült ({ex.GetType().Name}: {ex.Message}).");
            }
        }
    }
}
