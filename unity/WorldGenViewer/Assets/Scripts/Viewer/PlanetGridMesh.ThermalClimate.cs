using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            meanSurfaceK = _climateApplied.MeanSurfaceK;
            thresholdK = _climateApplied.PermanentIceThresholdK;
            return true;
        }

        /// <summary>
        /// A Build vegen hivjuk, a mar kiszamolt bemenetekkel: ez rogziti a
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
            DenseGridMetrics grid = DenseGridMetrics.Build(inputs.Level);
            token.ThrowIfCancellationRequested();

            // A lenyomathoz es a szamitashoz UGYANAZ a mezo kell, kulonben a
            // cache nem azt validalna, amit futtatunk.
            // MERVE az Editorban (Mono, level 5, 6144 cella): a parhuzamos
            // lokalis lepes NEM gyorsit - 200 tick szekvencialisan 3,56 ms/tick,
            // parhuzamosan 3,55 ms/tick. A Parallel.For tickenkenti particionalasi
            // koltsege Monoban felemeszti a nyereseget ekkora munkacsomagnal
            // (tickenkent egyetlen, 6144 elemu ciklus). Play kozben ez ROSSZABB
            // is: a TPL ugyanazon a ThreadPoolon versenyez az Editor sajat
            // munkaival. Ezert a viewer-oldali worker SZEKVENCIALISAN lep;
            // a .NET 8 CLI-mérés (ahol a parhuzamositas segit) ettol fuggetlen.
            var field = new SurfaceTemperatureField(grid, inputs.IceFreeKinds, inputs.ElevationM,
                inputs.SeaLevelM, inputs.Seed, inputs.TYears, inputs.Orbit);
            long[] sampleDays = ThermalAnnualStatisticsCalculator.SampleDayIndices(
                inputs.Orbit.OrbitalPeriodDays, ThermalAnnualStatisticsCalculator.DefaultSampleDays);
            ulong fingerprint = ThermalClimateDiskCache.ComputeSolverFingerprint(field, 0);
            token.ThrowIfCancellationRequested();

            var key = new ThermalClimateDiskCache.Key(field.ModelIdentity, grid.CellCount, sampleDays,
                ThermalClimateDiskCache.Key.PercentileBits(ThermalIceClassification.DefaultPermanentIcePercentile),
                fingerprint);

            ThermalClimateDiskCache.Payload payload = TryLoadThermalClimateFromDisk(key);
            bool fromCache = payload != null;
            if (payload == null)
            {
                ThermalClimate climate = ThermalClimateCalculator.Compute(grid, inputs.IceFreeKinds,
                    inputs.ElevationM, inputs.SeaLevelM, inputs.Seed, inputs.TYears, inputs.Orbit,
                    useParallelLocalStep: false, includeRefinedWind: true);
                token.ThrowIfCancellationRequested();
                payload = ThermalClimateDiskCache.Payload.From(climate);
                SaveThermalClimateToDisk(key, payload);
            }

            // ND-162: a deep-time glaciacios eltolas (ND-44) EGYELORE a
            // homodell eves atlagara adodik - ld. a fajl fejlecet.
            double glaciationOffsetK = WorldGen.Core.Tectonics.DeepTimeErosionGlaciation.GlobalTempOffset(
                inputs.TYears / 1.0e6);

            var result = new ThermalClimateResult
            {
                Revision = inputs.Revision,
                Level = inputs.Level,
                FromCache = fromCache,
                ComputeMs = timer.Elapsed.TotalMilliseconds,
                PermanentIceThresholdK = payload.RefinedThresholdK + glaciationOffsetK,
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
                        if (inputs.IceFreeKinds[index] == SurfaceThermalKind.Ocean) continue; // SeaIce a biome-bol
                        result.MeanSurfaceK[id] = payload.Refined.MeanSurfaceK[index] + glaciationOffsetK;
                    }

            token.ThrowIfCancellationRequested();
            _climateCompleted = result;
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
