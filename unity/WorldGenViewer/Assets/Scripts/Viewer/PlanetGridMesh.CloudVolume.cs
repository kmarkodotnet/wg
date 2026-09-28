using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;
using WorldGen.Core.Terrain;
using WorldGen.Viewer.Lod;
using UpsampleTable = WorldGen.Viewer.Lod.CloudSkyAtlas.UpsampleTable;
using Debug = UnityEngine.Debug;

namespace WorldGen.Viewer
{
    /// <summary>
    /// ND-154 / ND-155 (M13): a TÉRFOGATI felhő és az égbolt-nyitottság
    /// adatútja — atlasz-építés háttérszálon, burkoló héj-mesh, uniformok.
    ///
    /// EZ A FÁJL CSAK ADATÚT ÉS ÁTADÁS. A fizika a Core-ban van
    /// (<see cref="CloudVolume"/>, <see cref="SurfaceSkyOpenness"/>), a
    /// nézetfüggő lépésszám a motorfüggetlen <see cref="CloudRaymarchPlan"/>-ban,
    /// a csomagolás a <see cref="CloudSkyAtlas"/>-ban, a raymarch pedig a
    /// <c>WorldGen/CloudVolume</c> shaderben.
    ///
    /// AZ ADATÚT (mind a NÉGY csatorna a MÁR KISZÁMÍTOTT mezőkből, I3):
    /// <list type="number">
    /// <item>a <see cref="MoisturePrecipitation.PrecipitationField"/> csapadéka
    /// és óceán-maszkja + eleváció-mezője — NULLA új kiértékelés;</item>
    /// <item>domain-relatív (óceán/szárazföld KÜLÖN) percentilis-alakítás →
    /// lefedettség (<see cref="CloudVolume.ShapeCoverage"/>);</item>
    /// <item>felhőalap és -vastagság (<see cref="CloudSkyAtlas.BuildChannels"/>);</item>
    /// <item>égbolt-nyitottság (<see cref="SurfaceSkyOpenness.Evaluate"/>);</item>
    /// <item>RGBA32 kocka-atlasz (396×66) → egyetlen textúra-feltöltés.</item>
    /// </list>
    ///
    /// MIÉRT HÁTTÉRSZÁLON. Ugyanaz a hibaosztály, amit a felhő-sodródás
    /// (ApplyCloudOnlyRebuild) és a hőmező (ND-104) is így kezel: a
    /// 24576 cella alakítása + a nyitottság frame-hitchet adna a főszálon.
    /// Egyetlen munka fut egyszerre (single-flight), az eredményt a főszál
    /// veszi át.
    ///
    /// MIÉRT NEM SEED-TÖRŐ. Minden itteni érték render-KIMENET: egyetlen
    /// mezőbe, gyorsítótárba, hash-be vagy mentésbe sem folyik vissza.
    /// </summary>
    public partial class PlanetGridMesh
    {
        [Header("Térfogati felhő (ND-154, M13)")]
        [SerializeField]
        [Tooltip("Gömbi raymarch a csapadék-mezőből származó felhőhéjban (alj = a talaj fölötti kondenzációs szint, vastagság = lefedettség + orografikus emelés). " +
                 "Kikapcsolva a kép bitre az ND-154 előtti: a réteg nem is rajzolódik, és a felszíni felhőárnyék egzaktul 1-es szorzót ad.")]
        private bool cloudVolumetric = true;

        [SerializeField]
        [Range(0f, 2f)]
        [Tooltip("A felhő OPTIKAI MÉLYSÉGÉNEK skálája. 1 = a Core-ban kalibrált, fizikai érték (egy 500 m-es rétegfelhő optikai mélysége 10, azaz opak); " +
                 "kisebb érték fizikai módon vékonyítja a felhőt, tehát átlátszik rajta a felszín - a sűrű mag ilyenkor is opakabb marad a peremnél. 0 = láthatatlan.")]
        // FELHASZNÁLÓI VISSZAJELZÉS (2026-09-27): „a felhő átlátszósága nem elég
        // magas, totálisan takarja minden felhő a területet". Az alapérték
        // ezért 1,0-ről 0,45-re csökkent. Ez TUDATOS, VISSZAFORDÍTHATÓ
        // render-döntés, nem a modell meghamisítása: a felhő optikai mélysége
        // fizikailag TÉNYLEG opak (egy 700 m-es vízfelhő τ-ja ~10, azon nem
        // látni át), de a bolygó megismerhetősége fontosabb, mint a felhő
        // fotometriai hűsége. 1,0-ra állítva a fizikai érték áll vissza.
        private float cloudVolumeOpacity = 0.45f;

        [SerializeField]
        [Range(0f, 0.5f)]
        [Tooltip("A felhő ambiens (égbolt-) megvilágítása. Ugyanaz a szerep, mint a surfaceAmbient-nél: az éjszakai oldalon se legyen teljesen fekete.")]
        private float cloudVolumeAmbient = 0.08f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("A felszínre vetett felhőárnyék erőssége. 1 = a Core szerinti teljes Beer–Lambert-csillapítás (a borult égbolt diffúz padlójával); 0 = nincs árnyék.")]
        private float cloudShadowStrength = 1f;

        [SerializeField]
        [Range(0.5f, 4f)]
        [Tooltip("Az atlasz újraépítésének minimális időköre másodpercben (felhő-sodródásnál).")]
        private float cloudVolumeRebuildSeconds = 1.5f;

        [SerializeField]
        [Range(500f, 8000f)]
        [Tooltip("A felhődekk VÍZSZINTES alapszintje méterben a tengerszint fölött. A réteg nem követi a terepet - csak ott emelkedik meg, " +
                 "ahol a talaj fölötti kondenzációs szint már e fölé kerülne. 2500 m a WMO középszintű osztályának alsó pereme, és a mért " +
                 "domborzat (max 2308 m) fölé teszi a lapot.")]
        private float cloudDeckBaseMeters = (float)CloudVolume.MidLevelBaseMeters;

        [SerializeField]
        [Range(1f, 120f)]
        [Tooltip("A felhő VASTAGSÁGÁNAK függőleges nagyítása. Az ALAP a terep nagyításával (terrainReliefExaggeration) emelkedik, hogy a dekk a rajzolt hegyek fölé kerüljön; " +
                 "a vastagságra ugyanez a szorzó viszont egy 9,5 km-es zivatarfelhőből 111-szeres nagyításnál 1054 km-es tornyot csinálna, ami kipúposodik a bolygóból. " +
                 "20 = a tipikus dekk rétegnek látszik, a mély cellák nem toronynak. 1 = valódi lépték (közelről lapos).")]
        private float cloudThicknessExaggeration = 20f;

        [SerializeField]
        [Range(0f, 2f)]
        [Tooltip("A felhőrendszerek VONULÁSI sebessége radián/bolygó-nap. A mintázat a gömbön FORGATÁSSAL vándorol (torzulás és telítődés nélkül, tetszőlegesen sokáig). " +
                 "0,3 = 2225 km/nap = 25,8 m/s, futóáramlás-szintű sebesség, ami a középszintű dekkhez illik. 0 = álló felhők; 1,0 már látványosan gyors.")]
        private float cloudAdvectionRadiansPerDay = (float)CloudVolume.WeatherAdvectionRadiansPerDay;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Mennyire uralja az IDŐJÁRÁS a felhőképet a klimatológiához képest. A csapadék-mező földrajzhoz kötött (ITCZ, orografikus), az időjárás-tag viszont " +
                 "vándorol - ez a szorzó mondja meg, mekkora a vándorló rész. A modell saját kalibrált értéke 0,7 (a szorzó 0,3…1,7 között mozog); 1,0-nál az időjárás " +
                 "teljesen el tudja nyomni vagy meg tudja duplázni a csapadékot, tehát a rendszerek láthatóan KELETKEZNEK és ELVONULNAK.")]
        private float cloudWeatherStrength = (float)WindPrecipitation.MaxWeatherPrecipDeviationFraction;

        [SerializeField]
        [Range(0.1f, 0.9f)]
        [Tooltip("A felhőfolt PEREMÉNEK lágysága: a cella mekkora hányada esik a felhő-mag és a derült ég közti átmenetbe. " +
                 "Nagyobb érték = átlátszóbb, rétegfelhős jelleg; kisebb = élesebb, gomolyfelhős. A cella-átlagos lefedettség " +
                 "MINDEN értéknél pontosan a modellezett marad.")]
        private float cloudEdgeSoftness = (float)CloudVolume.SubGridEdgeWidth;

        [SerializeField]
        [Range(0, 4)]
        [Tooltip("Felhő-diagnosztika: 0 = ki (a kép bitre a diagnosztika nélküli), 1 = a burkoló tömör kitöltése (renderelődik-e a pass), " +
                 "2 = a menet ablakának hossza, 3 = a lefedettség-atlasz nyersen, 4 = a számolt alfa szürkében.")]
        private int cloudVolumeDiagnostic;

        /// <summary>
        /// A burkoló héj-mesh kockagömb-szintje. 3 → laponként 8×8 quad, 384
        /// quad összesen: a sziluett-hiba 1/cos(halfDiag) = 1,0097, amit a
        /// <see cref="CloudRaymarchPlan.ShellPaddingFactor"/> kompenzál. Ennél
        /// finomabb mesh semmit nem adna: a héj METSZÉSE analitikus, a
        /// geometria csak a fragmenteket állítja elő.
        /// </summary>
        private const int CloudShellMeshLevel = 3;

        private static readonly int CloudSkyTexId = Shader.PropertyToID("_CloudSkyTex");
        private static readonly int CloudWorldToPlanetId = Shader.PropertyToID("_CloudWorldToPlanet");
        private static readonly int CloudShellId = Shader.PropertyToID("_CloudShell");
        private static readonly int CloudScaleId = Shader.PropertyToID("_CloudScale");
        private static readonly int CloudProfileId = Shader.PropertyToID("_CloudProfile");
        private static readonly int CloudDetailId = Shader.PropertyToID("_CloudDetail");
        private static readonly int CloudDetailPhaseId = Shader.PropertyToID("_CloudDetailPhase");
        private static readonly int CloudMarchId = Shader.PropertyToID("_CloudMarch");
        private static readonly int CloudShadowId = Shader.PropertyToID("_CloudShadow");
        private static readonly int CloudDiagnosticId = Shader.PropertyToID("_CloudDiagnostic");
        private static readonly int CloudScatterId = Shader.PropertyToID("_CloudScatter");
        private static readonly int CloudTwilightId = Shader.PropertyToID("_CloudTwilight");
        private static readonly int CloudThicknessScaleId = Shader.PropertyToID("_CloudThicknessScale");
        private static readonly int CloudNoiseId = Shader.PropertyToID("_CloudNoise");

        private static readonly Lazy<DenseGridMetrics> CloudGrid =
            new Lazy<DenseGridMetrics>(() => DenseGridMetrics.Build(CloudSkyAtlas.Level), LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<int[]> CloudTexelMap =
            new Lazy<int[]>(ThermalOverlayPacking.BuildTexelSourceMap, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>A háttérmunka TISZTA (Unity API-t nem hívó) eredménye.</summary>
        private sealed class CloudAtlasResult
        {
            public int Revision;
            public byte[] Texels;
            public double CoverageMean;
            public double CoverageMax;
            public int CoveredCells;
            public double OpennessMean;
            public double OpennessMin;
            public double BuildMs;
        }

        private Material _cloudVolumeMaterial;
        private Texture2D _cloudSkyTexture;
        private Task<CloudAtlasResult> _cloudAtlasTask;
        private CancellationTokenSource _cloudAtlasCancel;
        private CloudAtlasResult _cloudAtlasLatest;
        private int _cloudAtlasRevision;
        private int _cloudAtlasBuiltRevision = -1;
        private bool _cloudBuiltListenerAdded;
        private float _cloudAtlasLastBuildRealtime = float.NegativeInfinity;
        private double _cloudAtlasWeatherTime = double.NaN;
        private double _cloudAtlasWeatherStrength = double.NaN;
        private double _cloudAtlasDeckBase = double.NaN;
        private bool _cloudFormatWarningShown;
        private string _cloudVolumeStatus = "térfogati felhő: nincs adat";

        /// <summary>A térfogati felhő akkor és csak akkor aktív, ha be van kapcsolva és van csapadék-mező.</summary>
        private bool CloudVolumeActive => cloudVolumetric && cloudVolumeOpacity > 0f && _lastPrecipField != null;

        /// <summary>
        /// Az ADATÚT lépése: az <see cref="Update"/> hívja, a hőmező munkája
        /// mellett. Az UNIFORMOK ellenben a <see cref="LateUpdate"/>-ból mennek
        /// ki (<see cref="ApplyCloudVolumeUniforms"/>) — ld. ott, miért.
        /// </summary>
        private void UpdateCloudVolume()
        {
            if (!_cloudBuiltListenerAdded)
            {
                Built.AddListener(InvalidateCloudAtlas);
                _cloudBuiltListenerAdded = true;
            }

            PollCloudAtlasTask();

            if (!CloudVolumeActive)
                return;

            MaybeStartCloudAtlasJob();
        }

        private void InvalidateCloudAtlas()
        {
            _cloudAtlasRevision++;
            _cloudAtlasCancel?.Cancel();
            _cloudVolumeStatus = "térfogati felhő: új világ, újraszámítás";
        }

        /// <summary>
        /// Új atlasz-munka akkor indul, ha (a) a világ újraépült, (b) a
        /// sodródási idő elmozdult az utolsó építés óta, vagy (c) a
        /// lefedettség-alakítás paramétere változott — és nem fut már munka.
        /// </summary>
        private void MaybeStartCloudAtlasJob()
        {
            if (_cloudAtlasTask != null)
                return;

            // A `_cloudAtlasLatest == null` ág NEM redundáns: Play közbeni
            // szkript-újrafordításkor a Unity domain-reload a NEM szerializált
            // mezőket a TÍPUS alapértékére állítja (a mező-inicializátorok nem
            // futnak újra), tehát a "-1" őrszem elveszik, és a revízió-alapú
            // kapu önmagában azt hinné, hogy az atlasz kész - miközben sem
            // eredmény, sem textúra nincs. MÉRT eset: a réteg Play közbeni
            // újrafordítás után némán eltűnt.
            bool worldChanged = _cloudAtlasLatest == null || _cloudAtlasBuiltRevision != _cloudAtlasRevision;
            // A dekk alapszintje az atlasz G csatornájában van, tehát a csúszka
            // elmozdulása ÚJRACSOMAGOLÁST igényel - különben a változás csak a
            // következő világ-újraépítéskor látszana.
            bool deckMoved = !_cloudAtlasDeckBase.Equals((double)cloudDeckBaseMeters)
                || !_cloudAtlasWeatherStrength.Equals((double)cloudWeatherStrength);
            // AZ IDŐJÁRÁS ÓRÁJA A SZIMULÁCIÓS IDŐ, nem a valós eltelt idő.
            // FELHASZNÁLÓI VISSZAJELZÉS (2026-09-28): „a felhők pozíciója
            // statikus, ahogy a nap forog körbe, úgy bőségesen kellene ezeknek
            // is mozogni". A korábbi út a valós időt integrálta, ÉS
            // alapértelmezésben ki volt kapcsolva - a felhő ezért állt.
            double weatherTime = CloudWeatherTime();
            bool weatherMoved = !_cloudAtlasWeatherTime.Equals(weatherTime)
                && Time.unscaledTime - _cloudAtlasLastBuildRealtime >= Mathf.Max(0.5f, cloudVolumeRebuildSeconds);
            if (!worldChanged && !weatherMoved && !deckMoved)
                return;

            MoisturePrecipitation.PrecipitationField precipField = _lastPrecipField;
            if (precipField == null)
                return;

            _cloudAtlasCancel?.Dispose();
            _cloudAtlasCancel = new CancellationTokenSource();
            CancellationToken token = _cloudAtlasCancel.Token;
            int revision = _cloudAtlasRevision;
            ulong seed = _adaptiveSeed;
            double seaLevel = _adaptiveSeaLevel;
            double driftTime = weatherTime;
            double threshold = cloudDensityThreshold;
            double gamma = cloudDensityGamma;
            double deckBase = cloudDeckBaseMeters;
            double weatherStrength = cloudWeatherStrength;
            double exaggeration = terrainReliefExaggeration;

            _cloudAtlasWeatherTime = weatherTime;
            _cloudAtlasLastBuildRealtime = Time.unscaledTime;
            _cloudAtlasDeckBase = deckBase;
            _cloudAtlasWeatherStrength = weatherStrength;
            _cloudAtlasTask = Task.Run(
                () => BuildCloudAtlas(precipField, revision, seed, seaLevel, driftTime, threshold, gamma, exaggeration, deckBase, weatherStrength, token),
                token);
        }

        /// <summary>
        /// A négy atlasz-csatorna kiszámítása és csomagolása. TISZTA: nincs
        /// Unity API-hívás, minden bemenetet EXPLICIT PARAMÉTERKÉNT kap (egy
        /// közben elmozduló Inspector-csúszka nem okoz versenyhelyzetet).
        /// </summary>
        private static CloudAtlasResult BuildCloudAtlas(
            MoisturePrecipitation.PrecipitationField precipField, int revision, ulong seed,
            double seaLevelMeters, double driftTime, double thresholdPercentile, double gamma,
            double reliefExaggeration, double deckBaseMeters, double weatherStrength, CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            DenseGridMetrics grid = CloudGrid.Value;
            int cells = grid.CellCount;

            // A csapadék-mező SAJÁT szintje (a referencia-szint) - nem
            // feltételezzük, hogy azonos az atlaszéval.
            int precipLevel = CloudSkyAtlas.Level;
            foreach (TileId key in precipField.Precipitation.Keys)
            {
                precipLevel = key.Level;
                break;
            }
            // A referencia-szint a viewer `level` mezője, ami az atlasz szintje
            // FÖLÉ is mehet. Felskálázni csak lefelé-ről lehet, ezért a
            // finomabb mezőt az atlasz szintjére VÁGJUK: a cella-középpont
            // tile-ja az atlasz szintjén az ősét adja, ami ezen a felbontáson
            // a helyes minta (a csomagolás amúgy sem tudna többet ábrázolni).
            int sampleLevel = precipLevel > CloudSkyAtlas.Level ? CloudSkyAtlas.Level : precipLevel;

            // Domain-relatív percentilis-padló és -plafon, KÜLÖN az óceán és a
            // szárazföld csapadék-eloszlásán. Ugyanaz a csapda-elkerülés, mint
            // a lapos MVP-nél: a KÉSZ lefedettséget alakítjuk domainenként,
            // nem a küszöböt interpoláljuk (ld. CloudVolume.BlendByOceanFraction).
            var oceanVals = new List<double>();
            var landVals = new List<double>();
            foreach (KeyValuePair<TileId, double> kv in precipField.Precipitation)
                (precipField.IsOcean.TryGetValue(kv.Key, out bool oc) && oc ? oceanVals : landVals).Add(kv.Value);
            oceanVals.Sort();
            landVals.Sort();
            double oceanFloor = PercentileOf(oceanVals, thresholdPercentile);
            double oceanCeil = Math.Max(oceanFloor + 1e-6, PercentileOf(oceanVals, CloudVolume.CeilingPercentile));
            double landFloor = PercentileOf(landVals, thresholdPercentile);
            double landCeil = Math.Max(landFloor + 1e-6, PercentileOf(landVals, CloudVolume.CeilingPercentile));

            // A lefedettséget a FORRÁS szintjén alakítjuk (ott ismert a cella
            // saját óceán/szárazföld besorolása), és csak a KÉSZ mezőt
            // skálázzuk fel az atlasz rácsára - bilineárisan, mert a
            // legközelebbi cella átvétele a forrás cellaméretén hagyna hard
            // éleket (MÉRVE: szögletes felhőárnyék-foltok az első élő menetben).
            UpsampleTable table = CloudUpsample(sampleLevel);
            int sourceSide = 1 << sampleLevel;
            var sourceCoverage = new double[table.SourceCellCount];
            var sourceElevation = new double[table.SourceCellCount];
            int processed = 0;
            foreach (KeyValuePair<TileId, double> kv in precipField.Precipitation)
            {
                if ((processed++ & 1023) == 0) token.ThrowIfCancellationRequested();
                TileId tile = kv.Key;
                TileId sampleTile = tile;
                if (precipLevel > sampleLevel)
                {
                    // Finomabb forrás: az atlasz szintjén vett ős. Több tile
                    // esik ugyanarra a cellára - az UTOLSÓ nyer, ami ezen a
                    // felbontáson tetszőleges, de DETERMINISZTIKUS (a szótár
                    // bejárása a beszúrási sorrendet követi, az pedig a
                    // MoisturePrecipitation determinisztikus rácsbejárása).
                    TileGeometry.ToPosition(tile, out double sx, out double sy, out double sz);
                    sampleTile = TileGeometry.FromPosition(sx, sy, sz, sampleLevel);
                }
                sampleTile.GetUV(out uint tu, out uint tv);
                int si = DenseGridMetrics.Index(sampleTile.Face, (int)tu, (int)tv, sourceSide);

                double precip = kv.Value;
                precipField.Elevation.TryGetValue(tile, out double elev);
                sourceElevation[si] = elev;
                bool isOcean = precipField.IsOcean.TryGetValue(tile, out bool oc) && oc;

                if (driftTime != 0.0 && weatherStrength > 0.0)
                {
                    // AZ IDŐJÁRÁS-TAG VÁNDOROL, a klimatológia nem. A
                    // csapadék-mező földrajzhoz kötött (ITCZ, orografikus) -
                    // az marad a helyén; az időjárás-zajt viszont ELFORGATJUK
                    // az advekciós tengely körül, és így a rendszerek
                    // ténylegesen átvonulnak a bolygón.
                    //
                    // A modell SAJÁT sodródási tagját (a `t` paramétert) ezért
                    // NULLÁN hagyjuk: az additív eltolás + újranormálás π/2-nél
                    // telítődik, és t ≳ 100 fölött a zajmező elfajul (MÉRVE),
                    // tehát hosszú távú óraként nem használható - ld. ND-154.
                    TileGeometry.ToPosition(tile, out double tx, out double ty, out double tz);
                    CloudVolume.Advect(tx, ty, tz, driftTime, out double wx, out double wy, out double wz);
                    precip *= WindPrecipitation.WeatherPrecipitationMultiplier(seed, wx, wy, wz, 0.0, weatherStrength);
                }

                sourceCoverage[si] = isOcean
                    ? CloudVolume.ShapeCoverage(precip, oceanFloor, oceanCeil, gamma)
                    : CloudVolume.ShapeCoverage(precip, landFloor, landCeil, gamma);
            }

            var coverage = new double[cells];
            var elevation = new double[cells];
            table.Resample(sourceCoverage, coverage);
            table.Resample(sourceElevation, elevation);

            var baseMeters = new double[cells];
            var thicknessMeters = new double[cells];
            CloudSkyAtlas.BuildChannels(coverage, elevation, seaLevelMeters, baseMeters, thicknessMeters, deckBaseMeters);

            token.ThrowIfCancellationRequested();
            // Az égbolt-nyitottság a DOMBORZATTÓL függ, a felhő-sodródástól NEM,
            // ezért világonként EGYSZER számoljuk. Enélkül a sodródás minden
            // ütemében (1,5 s) újra lefutna 24576 cella × 8 azimut × 4 gyűrű =
            // ~786 000 mintavétel - tiszta pazarlás egy változatlan mezőre.
            double[] openness = CloudOpenness(revision, grid, elevation, seaLevelMeters, reliefExaggeration, token);

            int[] map = CloudTexelMap.Value;
            var texels = new byte[map.Length * CloudSkyAtlas.Channels];
            CloudSkyAtlas.Pack(map, coverage, baseMeters, thicknessMeters, openness, texels);

            double covSum = 0.0, covMax = 0.0, openSum = 0.0, openMin = 1.0;
            int covered = 0;
            for (int c = 0; c < cells; c++)
            {
                covSum += coverage[c];
                if (coverage[c] > covMax) covMax = coverage[c];
                if (coverage[c] > 0.0) covered++;
                openSum += openness[c];
                if (openness[c] < openMin) openMin = openness[c];
            }

            return new CloudAtlasResult
            {
                Revision = revision,
                Texels = texels,
                CoverageMean = covSum / cells,
                CoverageMax = covMax,
                CoveredCells = covered,
                OpennessMean = openSum / cells,
                OpennessMin = openMin,
                BuildMs = sw.Elapsed.TotalMilliseconds,
            };
        }

        /// <summary>
        /// Az IDŐJÁRÁS-ZAJ ideje: a szimulációs napból (a Nap órájából)
        /// származik, tehát a felhők azzal együtt vándorolnak, ahogy a Nap
        /// körbefordul. A <see cref="WindPrecipitation.WeatherNoiseRaw"/> a
        /// mintavételi pontot egy rögzített tengely mentén tolja el
        /// <c>WeatherTimeSpeed · t</c>-vel, tehát a mintázat ténylegesen
        /// TRANSZLÁLÓDIK a gömbön — nem csak helyben pulzál.
        ///
        /// A SKÁLA SZÁRMAZTATOTT: a modell <c>WeatherTimeSpeed = 0,01</c>-je
        /// mellett napi 12 időjárás-egység 0,12 radián eltolást ad, ami
        /// 7420 km-es sugáron ~890 km/nap ≈ 10,3 m/s — a mérsékelt övi
        /// időjárási rendszerek jellemző vonulási sebessége.
        /// </summary>
        private double CloudWeatherTime()
        {
            if (cloudAdvectionRadiansPerDay <= 0f)
                return 0.0;
            CurrentThermalOrbit(out double timeDays);
            return timeDays * cloudAdvectionRadiansPerDay;
        }

        private static readonly object CloudOpennessLock = new object();
        private static double[] _cloudOpennessCache;
        private static int _cloudOpennessRevision = -1;
        private static double _cloudOpennessSeaLevel = double.NaN;
        private static double _cloudOpennessExaggeration = double.NaN;

        /// <summary>
        /// Az égbolt-nyitottság világonkénti gyorsítótára. Ld. a hívás helyén:
        /// a mező a domborzattól függ, a felhő-sodródástól nem.
        /// </summary>
        private static double[] CloudOpenness(
            int revision, DenseGridMetrics grid, double[] elevation,
            double seaLevelMeters, double reliefExaggeration, CancellationToken token)
        {
            lock (CloudOpennessLock)
            {
                if (_cloudOpennessCache != null
                    && _cloudOpennessRevision == revision
                    && _cloudOpennessSeaLevel.Equals(seaLevelMeters)
                    && _cloudOpennessExaggeration.Equals(reliefExaggeration))
                    return _cloudOpennessCache;
            }

            double[] computed = SurfaceSkyOpenness.Evaluate(grid, elevation, seaLevelMeters, reliefExaggeration);
            token.ThrowIfCancellationRequested();
            lock (CloudOpennessLock)
            {
                _cloudOpennessCache = computed;
                _cloudOpennessRevision = revision;
                _cloudOpennessSeaLevel = seaLevelMeters;
                _cloudOpennessExaggeration = reliefExaggeration;
            }
            return computed;
        }

        private static readonly object CloudUpsampleLock = new object();
        private static UpsampleTable _cloudUpsample;

        /// <summary>
        /// A felskálázó tábla forrás-szintenként CACHE-elve: 24576 cella ×
        /// 4 sarok felépítése nem sok, de minden atlasz-újraépítésnél
        /// (felhő-sodródás!) újraszámolni pazarlás lenne.
        /// </summary>
        private static UpsampleTable CloudUpsample(int sourceLevel)
        {
            lock (CloudUpsampleLock)
            {
                if (_cloudUpsample == null || _cloudUpsample.SourceLevel != sourceLevel)
                    _cloudUpsample = UpsampleTable.Build(sourceLevel);
                return _cloudUpsample;
            }
        }

        private static double PercentileOf(List<double> sortedValues, double p)
        {
            if (sortedValues.Count == 0) return 0.0;
            double clamped = p < 0.0 ? 0.0 : (p > 1.0 ? 1.0 : p);
            int idx = (int)(clamped * (sortedValues.Count - 1));
            return sortedValues[idx];
        }

        private void PollCloudAtlasTask()
        {
            if (_cloudAtlasTask == null || !_cloudAtlasTask.IsCompleted)
                return;

            Task<CloudAtlasResult> task = _cloudAtlasTask;
            _cloudAtlasTask = null;
            if (task.IsCanceled)
                return;
            if (task.IsFaulted)
            {
                Debug.LogWarning($"PlanetGridMesh térfogati felhő: az atlasz-munka hibára futott: {task.Exception?.GetBaseException()}");
                _cloudVolumeStatus = "térfogati felhő: hiba az atlasz-építésben";
                return;
            }

            CloudAtlasResult result = task.Result;
            if (result == null || result.Revision != _cloudAtlasRevision)
                return; // közben új világ jött; a következő kör újraszámol

            if (!SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32))
            {
                if (!_cloudFormatWarningShown)
                {
                    Debug.LogWarning("PlanetGridMesh térfogati felhő: az RGBA32 textúraformátum nem támogatott, a réteg nem jeleníthető meg.");
                    _cloudFormatWarningShown = true;
                }
                _cloudVolumeStatus = "térfogati felhő: RGBA32 textúra nem támogatott";
                return;
            }

            var sw = Stopwatch.StartNew();
            _cloudSkyTexture = EnsureCloudSkyTexture(_cloudSkyTexture);
            _cloudSkyTexture.SetPixelData(result.Texels, 0);
            _cloudSkyTexture.Apply(false, false);
            double uploadMs = sw.Elapsed.TotalMilliseconds;

            _cloudAtlasLatest = result;
            _cloudAtlasBuiltRevision = result.Revision;
            _cloudVolumeStatus = string.Format(CultureInfo.InvariantCulture,
                "térfogati felhő: lefedettség átlag {0:F3}, max {1:F3}, cellák {2}",
                result.CoverageMean, result.CoverageMax, result.CoveredCells);
            PerfLog(string.Format(CultureInfo.InvariantCulture,
                "[ND-154 cloudVolume] buildMs={0:F1} uploadMs={1:F2} covMean={2:F4} covMax={3:F4} covered={4} " +
                "openMean={5:F6} openMin={6:F6} status={7}",
                result.BuildMs, uploadMs, result.CoverageMean, result.CoverageMax, result.CoveredCells,
                result.OpennessMean, result.OpennessMin, _cloudVolumeStatus));
        }

        private Texture2D EnsureCloudSkyTexture(Texture2D existing)
        {
            if (existing != null)
                return existing;
            var tex = new Texture2D(CloudSkyAtlas.AtlasWidth, CloudSkyAtlas.AtlasHeight, TextureFormat.RGBA32, false, true)
            {
                name = "CloudSkyAtlas",
                // A gutter (egycellás perem) miatt a bilineáris szűrés a
                // kockalap-éleken sem kever idegen értéket - ld. ThermalOverlayPacking.
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            return tex;
        }

        /// <summary>
        /// A héj-uniformok és a burkoló mesh frissítése. Minden frame fut
        /// (néhány uniform egy megosztott anyagon), mert a lépésszám
        /// nézetfüggő és a Nap mozog.
        ///
        /// MIÉRT A LateUpdate-BŐL. A <c>_CloudWorldToPlanet</c> a bolygó
        /// pillanatnyi transzformja, amit tengely-forgatásos módban egy MÁSIK
        /// komponens (<see cref="SunController"/>) állít a saját
        /// <c>Update</c>-jében — a két <c>Update</c> sorrendje nem definiált,
        /// tehát innen a felhő kerete egy frame-et késhetne a felszíni
        /// geometria mögött, és a felhőrajzolat „úszna" a terephez képest.
        /// A <c>LateUpdate</c> MINDEN <c>Update</c> után fut; ugyanezt teszi a
        /// mikro-részlet <c>_MicroWorldToPlanet</c>-je is (ND-151).
        /// </summary>
        private void ApplyCloudVolumeUniforms()
        {
            if (!CloudVolumeActive)
            {
                SetCloudShellActive(false);
                // A felszíni felhőárnyék kapuja: 0 = egzaktul 1-es szorzó a shaderben.
                Shader.SetGlobalVector(CloudShadowId, Vector4.zero);
                return;
            }

            if (_cloudAtlasLatest == null || _cloudSkyTexture == null)
            {
                SetCloudShellActive(false);
                Shader.SetGlobalVector(CloudShadowId, Vector4.zero);
                return;
            }

            // A RAJZOLT magasság a terep domborzat-nagyítását KÖVETI: ha a
            // domborzat k-szoros magasan van rajzolva, a fölötte lebegő
            // felhődekknek is k-szoros magasan kell lennie, különben a
            // nagyított hegyek átdöfnék. `_CloudScale.x` ezért "rajzolt egység
            // per MODELL méter" - a shader így közvetlenül modell-métert nyer
            // vissza, amit az atlasz alj/vastagság értékeivel össze lehet mérni.
            // KÉT KÜLÖN FÜGGŐLEGES SKÁLA, és ez szándékos:
            //  - az ALAP a TEREP nagyításával emelkedik, különben a 111-szeresen
            //    rajzolt hegyek átdöfnék a dekket (ez volt az első hiba);
            //  - a VASTAGSÁG saját, kisebb szorzót kap, mert ugyanaz a 111 egy
            //    9,5 km-es zivatarfelhőből 1054 km-es tornyot csinálna, ami
            //    kipúposodik a bolygóból (felhasználói visszajelzés, 2026-09-28).
            double exaggeration = terrainReliefExaggeration == 0.0 ? 1.0 : terrainReliefExaggeration;
            double baseScale = elevationScale * exaggeration;
            double thicknessScale = elevationScale * Mathf.Max(1f, cloudThicknessExaggeration);
            double seaLevelRadius = radius + _adaptiveSeaLevel * elevationScale;
            // A dekk alja SOHA nem megy a vízszintes lap alá, ezért a belső
            // héjgömb is ott kezdődik - így a menet nem pazarol lépést a lap
            // alatti üres légrétegre.
            double innerRadius = seaLevelRadius + cloudDeckBaseMeters * baseScale;
            double outerRadius = seaLevelRadius
                + CloudVolume.MaxBaseAboveSeaLevelMeters * baseScale
                + CloudVolume.MaxThicknessMeters * thicknessScale;
            double padding = CloudRaymarchPlan.ShellPaddingFactor(CloudShellMeshLevel);
            double meshRadius = outerRadius * padding;

            GameObject shell = EnsureCloudShell((float)meshRadius);
            shell.SetActive(true);

            Shader.SetGlobalTexture(CloudSkyTexId, _cloudSkyTexture);
            Shader.SetGlobalMatrix(CloudWorldToPlanetId, transform.worldToLocalMatrix);
            Shader.SetGlobalVector(CloudShellId, new Vector4(
                (float)innerRadius, (float)outerRadius, (float)seaLevelRadius, (float)meshRadius));
            Shader.SetGlobalVector(CloudScaleId, new Vector4(
                (float)baseScale,
                (float)CloudVolume.MaxBaseAboveSeaLevelMeters,
                (float)CloudVolume.MaxThicknessMeters,
                (float)CloudVolume.ExtinctionPerMeter));
            Shader.SetGlobalVector(CloudProfileId, new Vector4(
                (float)CloudVolume.ProfileBaseFadeFraction,
                (float)CloudVolume.ProfileTopFadeFraction,
                cloudEdgeSoftness,
                (float)CloudVolume.DetailVerticalStretch));
            Shader.SetGlobalFloat(CloudTwilightId, (float)CloudVolume.TwilightBandCos);
            Shader.SetGlobalFloat(CloudThicknessScaleId, (float)thicknessScale);
            Shader.SetGlobalVector(CloudNoiseId, new Vector4(
                (float)CloudVolume.LogisticNormalSlope, (float)CloudVolume.DetailNoiseStdDev, 0f, 0f));
            Shader.SetGlobalVector(CloudScatterId, new Vector4(
                CloudVolume.MultiScatterOctaves,
                (float)CloudVolume.OctaveEnergy,
                (float)CloudVolume.OctaveExtinction,
                (float)CloudVolume.OctaveEccentricity));
            Shader.SetGlobalVector(CloudDetailId, new Vector4(
                (float)CloudVolume.DetailBaseFrequency(CloudSkyAtlas.Level),
                (float)CloudVolume.VerticalProfileMean,
                (float)CloudVolume.ForwardScatterG,
                (float)CloudVolume.PhasePeakCap));
            Shader.SetGlobalVector(CloudDetailPhaseId, CloudDetailPhase());
            Shader.SetGlobalFloat(CloudDiagnosticId, cloudVolumeDiagnostic);
            Shader.SetGlobalVector(CloudMarchId, new Vector4(
                CloudRaymarchPlan.MarchSteps,
                (float)CloudRaymarchPlan.TransmittanceCutoff,
                cloudVolumeOpacity,
                diagForceZeroLighting ? 0f : cloudVolumeAmbient));

            // A FELSZÍNI felhőárnyék kapuja a terep-shaderhez (ND-154):
            // (erősség, extinction·profil-átlag, maxThickness, diffúz padló).
            //
            // OVERLAY-KAPU, ugyanaz az elv, mint az ND-151 mikro-részleténél:
            // az adat-overlay-ek (tektonika, szél, csapadék, hő) alatt a szín
            // egy MÉRT mennyiség palettája (I4), amit egy árnyék-moduláció
            // félreolvashatóvá tenne.
            float shadowStrength = surfaceOverlayMode == SurfaceOverlayMode.None ? cloudShadowStrength : 0f;
            Shader.SetGlobalVector(CloudShadowId, new Vector4(
                shadowStrength,
                (float)(CloudVolume.ExtinctionPerMeter * CloudVolume.VerticalProfileMean),
                (float)CloudVolume.MaxThicknessMeters,
                (float)CloudVolume.OvercastDiffuseTransmission));
        }

        private ulong _cloudDetailPhaseSeed;
        private bool _cloudDetailPhaseValid;
        private Vector4 _cloudDetailPhaseValue;

        private Vector4 CloudDetailPhase()
        {
            ulong seed = WorldSeedUnsigned;
            if (_cloudDetailPhaseValid && _cloudDetailPhaseSeed == seed)
                return _cloudDetailPhaseValue;
            CloudVolume.DetailPhase(seed, out double px, out double py, out double pz);
            _cloudDetailPhaseValue = new Vector4((float)px, (float)py, (float)pz, 0f);
            _cloudDetailPhaseSeed = seed;
            _cloudDetailPhaseValid = true;
            return _cloudDetailPhaseValue;
        }

        private void SetCloudShellActive(bool active)
        {
            Transform child = transform.Find("CloudVolumeShell");
            if (child != null && child.gameObject.activeSelf != active)
                child.gameObject.SetActive(active);
        }

        /// <summary>
        /// A burkoló héj-mesh. Kockagömb a <see cref="CloudShellMeshLevel"/>
        /// szinten, a padolt külső sugáron — a raymarch a fragmentekben fut,
        /// ezért a mesh SZEREPE csak annyi, hogy a héjat a képernyőn lefedje
        /// (és tartalmazza: ld. <see cref="CloudRaymarchPlan.ShellPaddingFactor"/>).
        /// </summary>
        private GameObject EnsureCloudShell(float meshRadius)
        {
            Transform child = transform.Find("CloudVolumeShell");
            GameObject go;
            if (child == null)
            {
                go = new GameObject("CloudVolumeShell");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _cloudVolumeMaterial ??= CreateCloudVolumeMaterial();
                mr.sharedMaterial = _cloudVolumeMaterial;
            }
            else
            {
                go = child.gameObject;
                // A GameObject TÚLÉLI a Play közbeni domain-reloadot, a NEM
                // szerializált `_cloudVolumeMaterial` viszont nullázódik. Ha
                // itt nem vennénk vissza a renderer anyagát, a
                // `UpdateSurfaceLightingUniforms` némán kihagyná a Nap-uniformokat,
                // és a felhő örökre a shader alapértelmezett (0,4;0,6;0,7)
                // Napjával renderelne - ugyanaz a hibaosztály, amit a
                // MaybeStartCloudAtlasJob is kezel.
                MeshRenderer existing = go.GetComponent<MeshRenderer>();
                if (_cloudVolumeMaterial == null)
                    _cloudVolumeMaterial = existing.sharedMaterial != null
                        ? existing.sharedMaterial
                        : CreateCloudVolumeMaterial();
                if (existing.sharedMaterial == null)
                    existing.sharedMaterial = _cloudVolumeMaterial;
            }

            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf.sharedMesh == null || !Mathf.Approximately(_cloudShellMeshRadius, meshRadius))
            {
                // A sugár a tengerszinttel együtt változik (világ-újraépítés,
                // deep-time lépés), tehát ez az ág RENDSZERESEN lefut - a régi
                // meshet el kell engedni, különben lépésenként szivárog egy.
                Mesh previous = mf.sharedMesh;
                mf.sharedMesh = BuildCloudShellMesh(meshRadius);
                _cloudShellMeshRadius = meshRadius;
                if (previous != null)
                    DestroyImmediate(previous);
            }
            return go;
        }

        private float _cloudShellMeshRadius = float.NaN;

        private static Mesh BuildCloudShellMesh(float meshRadius)
        {
            int side = 1 << CloudShellMeshLevel;
            var verts = new List<Vector3>((side + 1) * (side + 1) * 6);
            var tris = new List<int>(side * side * 6 * 6);
            for (int face = 0; face < 6; face++)
            {
                int baseIndex = verts.Count;
                for (int iu = 0; iu <= side; iu++)
                {
                    double u = -1.0 + 2.0 * iu / side;
                    for (int iv = 0; iv <= side; iv++)
                    {
                        double v = -1.0 + 2.0 * iv / side;
                        TileGeometry.PositionFromFaceUV(face, u, v, out double x, out double y, out double z);
                        // Unity-lokál tengelysorrend, ugyanaz a csere, mint a
                        // felszíni mesh-építésnél (Core (x,y,z) -> Unity (x,z,y)).
                        verts.Add(new Vector3((float)(x * meshRadius), (float)(z * meshRadius), (float)(y * meshRadius)));
                    }
                }
                for (int iu = 0; iu < side; iu++)
                {
                    for (int iv = 0; iv < side; iv++)
                    {
                        int i00 = baseIndex + iu * (side + 1) + iv;
                        int i10 = i00 + (side + 1);
                        int i01 = i00 + 1;
                        int i11 = i10 + 1;
                        tris.Add(i00); tris.Add(i10); tris.Add(i11);
                        tris.Add(i00); tris.Add(i11); tris.Add(i01);
                    }
                }
            }

            var mesh = new Mesh { name = "CloudVolumeShell", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A háttérszálas atlasz-munka leállítása (OnDisable). A letiltott
        /// komponens nem dolgozhat tovább; a következő engedélyezéskor a
        /// <see cref="MaybeStartCloudAtlasJob"/> úgyis újraindítja
        /// (`_cloudAtlasLatest == null` vagy revízió-eltérés).
        /// </summary>
        private void CancelCloudAtlasWork()
        {
            _cloudAtlasCancel?.Cancel();
        }

        /// <summary>
        /// A saját GPU-erőforrások elengedése (OnDestroy). A héj-GameObjectet a
        /// szülő megszűnése törli, de a textúra, az anyag és a mesh SAJÁT
        /// allokáció - jelenetváltásonként/Play-ciklusonként felhalmozódna.
        /// </summary>
        private void ReleaseCloudVolumeResources()
        {
            CancelCloudAtlasWork();
            _cloudAtlasCancel?.Dispose();
            _cloudAtlasCancel = null;
            if (_cloudSkyTexture != null) SafeDestroy(_cloudSkyTexture);
            _cloudSkyTexture = null;
            if (_cloudVolumeMaterial != null) SafeDestroy(_cloudVolumeMaterial);
            _cloudVolumeMaterial = null;
            Transform child = transform.Find("CloudVolumeShell");
            MeshFilter mf = child != null ? child.GetComponent<MeshFilter>() : null;
            if (mf != null && mf.sharedMesh != null) SafeDestroy(mf.sharedMesh);
            _cloudShellMeshRadius = float.NaN;
        }

        private Material CreateCloudVolumeMaterial()
        {
            Shader shader = Shader.Find("WorldGen/CloudVolume");
            if (shader == null)
            {
                Debug.LogWarning("PlanetGridMesh: a \"WorldGen/CloudVolume\" shader nem található - " +
                    "a térfogati felhő-réteg nem rajzolódik.");
                return CreateFlatColorMaterial(new Color(1f, 1f, 1f, 0f));
            }
            return new Material(shader);
        }

    }
}
