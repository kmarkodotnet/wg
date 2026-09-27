using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Viewer.Lod;

namespace WorldGen.Viewer
{
    /// <summary>
    /// ND-19 (A12/2) — a GEOMETRIA tényleges eltolása: a floating origin
    /// jelenet-oldali fele. Az A12 1. köre a matematikai magot
    /// (<see cref="FloatingOrigin"/>) és az origó-követést adta; ez a rész
    /// mondja meg, HOL van a render-origó a jelenetben, és MELYIK réteg
    /// koordinátái relatívak hozzá.
    ///
    /// ## A jelenet-konvenció
    ///
    /// Legyen `p` egy test-keretbeli (Unity-tengelyű, modell-egységű) pont, a
    /// bolygó középpontja `p = 0`; `O` a render-origó UGYANEBBEN a térben;
    /// `R` a test → szülő-tér forgatás (a Planet `localRotation`-ja, amit a
    /// <see cref="SunController"/> állít), `s` az egyenletes skála, `A` a
    /// jelenetben megadott (eltolás előtti) Planet-pozíció.
    ///
    /// A cél, hogy a kamera KÖZELÉBEN levő geometria világ-koordinátái KICSIK
    /// legyenek, mert a `float32` felbontása RELATÍV (2^-23 … 2^-22) — nem a
    /// lépték, hanem a NAGYSÁGREND számít (ld. ND-19 lépték-invariancia-mérés).
    /// Ezért minden pont leképezése:
    ///
    ///     p  →  A + s·R·(p − O)
    ///
    /// Ezt KÉT, egyenértékű úton állítjuk elő, rétegenként másképp:
    ///
    /// | réteg | lokális tér | transzform | a `float32` hol kerekít |
    /// |---|---|---|---|
    /// | **Planet** (statikus alap, víz, határvonal, folyó, tó, felhő, StarField, markerek) | ABSZOLÚT `p` | `localPosition = A − s·R·O` | a csúcsban ÉS a mátrixban (`s·R·p − s·R·O`, ~0,57 m) |
    /// | **RefinedLayerRoot** (a finomított/dinamikus réteg) | ORIGÓ-RELATÍV `p − O` | `localPosition = A` | csak a KICSI `p − O`-ban (mm alatt) |
    ///
    /// Így a **nyereség pontosan ott jelentkezik, ahol kell**: a kamera
    /// közelében renderelt, finomított rétegen. A durva rétegek megtartják a
    /// mai, ~0,57 m-es kvantálásukat — ez nem regresszió, mert a kamera
    /// közelében a finomított réteg TAKARJA őket (`ApplyTerrainCoverage`
    /// index-maszkja kivágja a statikus háromszögeket), a coverage szélén
    /// pedig a 0,57 m már jóval pixel alatti.
    ///
    /// ## Miért NEM kellett "test-keretes renderelésre" váltani
    ///
    /// Az A12 1. körének naplója azt írta, hogy origó-relatív csúcsok mellett
    /// „a bolygó tengelyforgása többé nem kifejezhető a transform-mal".
    /// **Ez az állítás téves volt**, és ez a kör megdönti: az origót a
    /// TEST-KERETBEN választjuk (a bolygóval együtt forog), ezért
    /// `R·(p − O)` maga is puszta forgatás + eltolás, vagyis pontosan egy
    /// Unity-transzform. A Planet `rotation`-ja VÁLTOZATLANUL a spin/dőlés
    /// hordozója marad (ND-153), csak a `localPosition`-ja kap eltolást — a
    /// kameramódok (Free / AxialRotation / OrbitalFollow) érintetlenek.
    ///
    /// ## Miért marad a Planet lokális tere ABSZOLÚT
    ///
    /// Ez a döntés szüntette meg a változás nagy részét. A LOD-kiválasztás,
    /// az overlay-ek, a diagnosztika és a léptékvonalzó mind
    /// `transform.InverseTransformPoint(...)` / `transform.localToWorldMatrix`
    /// úton kérdezi a kamerát és a geometriát — ha a Planet lokális tere
    /// origó-relatívvá vált volna, MINDEGYIK csendben eltolódik. Így viszont
    /// egyetlen ilyen hívás sem változott. (A finomított chunkok
    /// `worldToLocal(Planet) · localToWorld(chunk)` kompozíciója automatikusan
    /// visszaadja az abszolút Planet-lokális koordinátát, tehát az
    /// ND-72/ND-148 diagnosztika is érintetlen.)
    ///
    /// ## Bit-azonossági szerződés
    ///
    /// **Bolygóközepű origónál (`IsPlanetCenter`) a kivonás pontos 0,0-t von
    /// ki, tehát minden emittált csúcs bitre a korábbi.** A statikus
    /// alap-réteg és a másodlagos rétegek emit-útja SZÁNDÉKOSAN a régi,
    /// `float32`-es alakon maradt (`ToWaterVector3`, `_staticCornerPositions`),
    /// hogy ez akkor is igaz legyen, ha a finomított réteg double-lánca
    /// megváltozik. A finomított réteg viszont MOST double-ban számol, ezért
    /// bolygóközepű origónál is legfeljebb 1 `float32` ULP-pel (a mai
    /// kvantáláson belül) eltérhet a korábbitól — szigorúan pontosabb irányba.
    /// </summary>
    public partial class PlanetGridMesh
    {
        /// <summary>
        /// A STATIKUS/másodlagos rétegek emit-origója: a bolygóközép, vagyis
        /// „nincs eltolás". Nem `default`, mert a `default(RenderOrigin)`
        /// szándékosan ÉRVÉNYTELEN (ld. ND-19 1. kör).
        /// </summary>
        private static readonly RenderOrigin AbsoluteEmitOrigin = RenderOrigin.PlanetCenter(1.0);

        /// <summary>Amit a kamera JAVASOL (minden képkockán frissülhet).</summary>
        private RenderOrigin _pendingRenderOrigin = RenderOrigin.PlanetCenter(1.0);

        /// <summary>
        /// Amivel az ÉPPEN FUTÓ cut/emisszió számol. A worker szál ezt olvassa,
        /// ezért kérésenként EGYSZER, a `_requested*` snapshotokkal együtt
        /// íródik (ld. RecomputeCutAndRebuildAdaptiveMesh).
        /// </summary>
        private RenderOrigin _requestedRenderOrigin = RenderOrigin.PlanetCenter(1.0);

        /// <summary>
        /// Amihez a MÁR FELTÖLTÖTT finomított geometria tartozik. EZ vezérli a
        /// jelenet-transzformot és a kamerát — így a világ-eltolás és a
        /// geometria UGYANABBAN a képkockában vált, nem tud egy képkockára
        /// egész cellányit elcsúszni a két réteg.
        /// </summary>
        private RenderOrigin _appliedRenderOrigin = RenderOrigin.PlanetCenter(1.0);

        private Transform _refinedLayerRoot;
        private bool _hasAuthoredPlanetLocalPosition;
        private Vector3 _authoredPlanetLocalPosition;

        /// <summary>Hányszor váltott TÉNYLEGESEN alkalmazott origót — a rebase-költség mérőszáma.</summary>
        public int AppliedRenderOriginRebases { get; private set; }

        /// <summary>A jelenleg ÉRVÉNYES (alkalmazott) render-origó.</summary>
        public RenderOrigin AppliedRenderOrigin => _appliedRenderOrigin;

        /// <summary>
        /// Az a Transform, aminek a LOKÁLIS tere ORIGÓ-RELATÍV. A kamera ezen
        /// keresztül számolja a saját világ-pozícióját, mert csak itt marad
        /// kicsi (és ezért pontos) a `float32` koordináta.
        /// </summary>
        public Transform RenderOriginFrame => RefinedLayerRoot;

        /// <summary>
        /// A12/2 előtti (vagy hot-reload után visszamaradt) finomított-réteg
        /// gyerekek átköltöztetése a Planet alól az origó-relatív gyökér alá.
        ///
        /// MIÉRT KELL: ezek a GameObject-ek MEGÉLIK a domain reload-ot, de a
        /// `_refinedLayerRoot` mező nem. Enélkül az új kód nem találná meg
        /// őket (`LayerRoot(name).Find(name)`), hanem MÁSODIK példányt hozna
        /// létre a gyökér alatt — a régi, még aktív réteg pedig ottmaradna a
        /// Planet alatt, ABSZOLÚT koordinátákkal, tehát eltolt origónál
        /// láthatóan elcsúszva. ÉLŐ Play-ben mérve találtuk (2026-09-27):
        /// egy `IndependentWater1` maradt aktívan a Planet alatt.
        /// </summary>
        private void MigrateRefinedChildrenFromPlanet()
        {
            if (_refinedLayerRoot == null) return;
            List<Transform> moving = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (!IsRefinedLayerChild(child.name) && !IsChunkRenderTargetName(child.name)) continue;
                (moving ??= new List<Transform>()).Add(child);
            }
            if (moving == null) return;
            foreach (Transform child in moving)
                child.SetParent(_refinedLayerRoot, false);
            PerfLog($"[ND-19 layer migration] moved={moving.Count} a Planet alol a {RefinedLayerRootName} ala");
        }

        private static bool IsChunkRenderTargetName(string childName)
            => childName != null && childName.StartsWith(ChunkRenderTargetPrefix, StringComparison.Ordinal);

        private static bool IsRefinedLayerChild(string childName)
        {
            // A réteg-nevek MA IS ezek a gyerek-GameObject nevek (ld.
            // ApplyAdaptiveMeshBuffersCore / ApplyIndependentWater), tehát ez a
            // predikátum nem vezet be új fogalmat - csak kimondja, melyik név
            // tartozik a FINOMÍTOTT (origó-relatív) réteghez.
            return childName == "DynamicRefined"
                || childName == "DynamicWater"
                || childName == "DynamicBorders"
                || childName == "IndependentWater0"
                || childName == "IndependentWater1";
        }

        /// <summary>
        /// Melyik gyökér alatt éljen az adott nevű réteg-GameObject: a
        /// finomított réteg az origó-relatív gyökér alatt, minden más a
        /// Planet alatt (abszolút tér).
        /// </summary>
        private Transform LayerRoot(string childName)
            => IsRefinedLayerChild(childName) ? RefinedLayerRoot : transform;

        private Transform RefinedLayerRoot
        {
            get
            {
                if (_refinedLayerRoot != null) return _refinedLayerRoot;
                // TESTVÉR, nem gyerek. Gyerekként a világ-eltolása
                // `s·R·O + (A − s·R·O)` lenne, amit a Unity `float32`-ben
                // számol - a kioltás ~0,57 m maradékot hagyna, ÉS `R`-rel
                // változna, tehát forgás közben a finomított réteg
                // remegne a durvához képest. Testvérként a pozíciója
                // közvetlenül `A`, kioltás nélkül.
                Transform parent = transform.parent;
                Transform existing = parent != null
                    ? parent.Find(RefinedLayerRootName)
                    : FindRootSibling(RefinedLayerRootName);
                if (existing != null)
                {
                    _refinedLayerRoot = existing;
                }
                else
                {
                    var go = new GameObject(RefinedLayerRootName);
                    go.transform.SetParent(parent, false);
                    _refinedLayerRoot = go.transform;
                }
                SyncRefinedLayerTransform();
                MigrateRefinedChildrenFromPlanet();
                return _refinedLayerRoot;
            }
        }

        private const string RefinedLayerRootName = "PlanetRefinedLayer";

        /// <summary>A dinamikus chunk-GameObject-ek névelőtagja (ld. CreateInactiveChunkRenderTarget).</summary>
        internal const string ChunkRenderTargetPrefix = "Chunk_";

        private static Transform FindRootSibling(string name)
        {
            GameObject found = GameObject.Find("/" + name);
            return found != null ? found.transform : null;
        }

        /// <summary>
        /// A két réteg-gyökér összehangolása. AKKOR is meg kell hívni, amikor
        /// valaki a Planet `rotation`-ját állítja (ld. SunController) — a
        /// finomított gyökérnek UGYANAZT a forgatást kell mutatnia, különben
        /// forgás közben egy képkockára elhasad a két réteg.
        /// </summary>
        public void SyncRefinedLayerTransform()
        {
            CaptureAuthoredPlanetLocalPosition();

            RenderOrigin origin = _appliedRenderOrigin;
            bool shifted = origin.IsValid && !origin.IsPlanetCenter;

            if (shifted)
            {
                var originLocal = new Vector3((float)origin.X, (float)origin.Y, (float)origin.Z);
                Vector3 scaled = Vector3.Scale(originLocal, transform.localScale);
                transform.localPosition = _authoredPlanetLocalPosition - transform.localRotation * scaled;
            }
            else if (transform.localPosition != _authoredPlanetLocalPosition)
            {
                transform.localPosition = _authoredPlanetLocalPosition;
            }

            if (_refinedLayerRoot == null) return;
            _refinedLayerRoot.localRotation = transform.localRotation;
            _refinedLayerRoot.localScale = transform.localScale;
            _refinedLayerRoot.localPosition = _authoredPlanetLocalPosition;
            if (_refinedLayerRoot.gameObject.activeSelf != gameObject.activeSelf)
                _refinedLayerRoot.gameObject.SetActive(gameObject.activeSelf);
        }

        /// <summary>
        /// A jelenetben MEGADOTT (eltolás előtti) Planet-pozíció rögzítése. Az
        /// eltolás ehhez képest történik, és bolygóközepű origónál ide áll
        /// vissza — enélkül egy nem-nulla jelenet-pozíciójú Planet csendben a
        /// 0 pontba ugorna.
        /// </summary>
        private void CaptureAuthoredPlanetLocalPosition()
        {
            if (_hasAuthoredPlanetLocalPosition) return;
            _authoredPlanetLocalPosition = transform.localPosition;
            _hasAuthoredPlanetLocalPosition = true;
        }

        private void DestroyRefinedLayerRoot()
        {
            if (_refinedLayerRoot == null) return;
            GameObject go = _refinedLayerRoot.gameObject;
            _refinedLayerRoot = null;
            SafeDestroy(go);
        }

        /// <summary>
        /// A kamera (PlanetOrbitCamera) ezen keresztül JAVASOL origót. A váltás
        /// nem azonnali: a következő cut-kérés veszi át, és csak a feltöltéssel
        /// együtt válik érvényessé (ld. `_appliedRenderOrigin`).
        /// </summary>
        public void RequestRenderOrigin(in RenderOrigin candidate)
        {
            if (!candidate.IsValid) return;
            if (SameOrigin(candidate, _pendingRenderOrigin)) return;
            _pendingRenderOrigin = candidate;
            // Kényszerített újraépítés: a kamera-mozgás küszöbe (ND-76) magában
            // nem feltétlenül lépne át, a geometria viszont MÁS origóhoz
            // tartozik, tehát újra kell emittálni.
            _adaptiveConfigDirty = true;
        }

        /// <summary>
        /// A kérés-snapshot része (ld. RecomputeCutAndRebuildAdaptiveMesh): itt
        /// dől el, milyen origóval emittál a most induló kérés. Ha ez MÁS, mint
        /// az előző kérésé, a chunk-cache-ek ÉRVÉNYTELENEK — nem a modelljük
        /// változott, hanem az a tér, amiben a csúcsaik ki vannak írva.
        /// </summary>
        private void SnapshotRequestedRenderOrigin()
        {
            RenderOrigin next = _pendingRenderOrigin.IsValid
                ? _pendingRenderOrigin
                : RenderOrigin.PlanetCenter(1.0);
            if (SameOrigin(next, _requestedRenderOrigin)) return;
            _requestedRenderOrigin = next;
            _previousChunkCache.Clear();
            _previousChunkPositions.Clear();
            _previousChunkGroups.Clear();
            InvalidateIndependentWaterForRenderOrigin();
        }

        /// <summary>
        /// A feltöltéssel EGY LÉPÉSBEN aktiválja azt az origót, amivel a most
        /// feltöltött geometria készült. Ha egy régebbi (elavult) kérés ér ide,
        /// a jelenet KONZISZTENSEN visszaáll arra az origóra — nincs olyan
        /// állapot, amiben a geometria és a világ-eltolás nem ugyanarra
        /// hivatkozik.
        /// </summary>
        private void AdoptAppliedRenderOrigin(in RenderOrigin origin)
        {
            if (!origin.IsValid) return;
            if (!SameOrigin(origin, _appliedRenderOrigin))
            {
                _appliedRenderOrigin = origin;
                AppliedRenderOriginRebases++;
                PerfLog($"[ND-19 geometry rebase] origin={origin} rebases={AppliedRenderOriginRebases} " +
                    $"planetLocalPosition={transform.localPosition.ToString("R")}");
            }
            SyncRefinedLayerTransform();
        }

        private static bool SameOrigin(in RenderOrigin a, in RenderOrigin b)
        {
            return a.IsValid == b.IsValid && a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        }

        /// <summary>
        /// Radiális (kifelé mutató) eltolás az ABSZOLÚT irány mentén, double-ban.
        /// Origó-relatív térben a `p.normalized` NEM a radiális irány, ezért ez
        /// a művelet kizárólag abszolút ponton értelmes — a hívónak tehát a
        /// float32-re váltás ELŐTT kell elvégeznie.
        /// </summary>
        private static SurfacePoint ApplyRadialBias(in SurfacePoint p, double bias)
        {
            SurfacePoint direction = p.Normalized;
            return p + direction * bias;
        }
    }
}
