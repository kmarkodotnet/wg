using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.Viewer
{
    /// <summary>
    /// M8 panel-UI: a PlanetGridMesh.ComputePanelData() eredményét írja ki
    /// három TextMeshPro mezőbe (World / Continents / Regions). Csak
    /// megjelenítés - semmilyen adat itt nem számolódik, minden a Core-ból
    /// jön (I4).
    ///
    /// FELHASZNÁLÓI KÉRÉS (2026-09-06): "a canvason megjelenített infó
    /// (kontinensek/régiók neveire) kattintva odaugrik-e a kamera?" - a
    /// kontinens/régió NEVEK TMP `&lt;link&gt;` rich-text tag-be vannak
    /// csomagolva (nem külön Button-onként, hogy a MEGLÉVŐ, egy nagy
    /// szöveg-blokkos elrendezés ne változzon), és az `Update()` egy
    /// PADDELT (a link karakterhatárainál nagyobb) területen teszteli a
    /// kattintást.
    ///
    /// FELHASZNÁLÓI VISSZAJELZÉS (2026-09-06): "jobb lenne ha... inkább
    /// egy gomb lenne, mert ha nem a karakterre kattintok, nem történik
    /// semmi" - az ELSŐ verzió `TMP_TextUtilities.FindIntersectingLink`-et
    /// használt, ami KIZÁRÓLAG a glyph-tinta tényleges (pixel-pontos)
    /// négyszögén belüli kattintást ismerte fel. VALÓDI Unity UI `Button`-ra
    /// váltás NEM lehetséges kockázat nélkül: a scene-ben az `EventSystem`
    /// GameObject `m_IsActive: 0` (KIKAPCSOLVA), és egyik Canvas-on sincs
    /// `GraphicRaycaster` komponens - ezek nélkül egy `Button.onClick` SOHA
    /// nem tüzelne, és az élő bekapcsolásuk (scene-szerkesztés élő
    /// Unity-teszt nélkül) kockázatos lenne (pl. az orbit-kamera egér-
    /// drag kezelése is `Input.GetMouseButton(0)`-t figyel - egy aktív
    /// EventSystem/GraphicRaycaster új interakciót vezetne be közéjük,
    /// amit nem tudok élőben leellenőrizni). EHELYETT: a kattintás-
    /// detektálás MEGTARTJA a bevált, MŰKÖDŐ `Input.mousePosition`-alapú
    /// utat, de a link KARAKTEREINEK bounding boxát (ld.
    /// <see cref="TryGetPaddedLinkBounds"/>) a SORMAGASSÁGGAL (ascender/
    /// descender, nem a glyph-tinta) ÉS explicit pixel-paddinggal
    /// (<see cref="LinkClickPaddingX"/>/<see cref="LinkClickPaddingY"/>)
    /// bővíti ki - ez GYAKORLATILAG ugyanazt a felhasználói élményt adja,
    /// mint egy valódi Button (nagyvonalú, megbocsátó kattint-terület),
    /// anélkül, hogy a scene esemény-infrastruktúráját élő teszt nélkül
    /// kellene módosítani.
    /// </summary>
    public class WorldGenPanelUI : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("A bolygó-mesh, aminek az adataiból a panelek épülnek. Build()-nek " +
                 "már le kellett futnia rajta, mielőtt Refresh Panels-t hívsz.")]
        private PlanetGridMesh planetGridMesh;

        [SerializeField]
        [Tooltip("A kamera, ami a kontinens/régió névre kattintáskor a célpont " +
                 "fölé repül (ld. FlyToDirection). Üresen hagyva automatikusan " +
                 "megkeresi a jelenetben lévő PlanetOrbitCamera-t.")]
        private PlanetOrbitCamera orbitCamera;

        [SerializeField]
        [Tooltip("A kamera-ugrás animáció hossza másodpercben.")]
        private float flyToDurationSeconds = 1.0f;

        [SerializeField]
        [Tooltip("Kattintáskor a kamera erre a felszín-feletti magasságra áll be " +
                 "(a régió/kontinens fölé közelítve) - a PlanetOrbitCamera saját " +
                 "surfaceRadius+ez adja a tényleges distance-t.")]
        private float flyToAltitude = 30f;

        [SerializeField] private TMP_Text worldPanelText;
        [SerializeField] private TMP_Text continentPanelText;
        [SerializeField] private TMP_Text regionPanelText;

        [SerializeField]
        [Tooltip("M9 'nézetszint-váltás' (docs/backlog.md \"Kontinens/régió kamera-átmenetek\") - " +
                 "minden képkockán frissül a PlanetOrbitCamera.CurrentViewLevel + a legközelebbi " +
                 "kontinens/régió nevével. Üresen hagyva ez a funkció egyszerűen nem jelenik meg " +
                 "(nem kötelező UI-elem).")]
        private TMP_Text viewLevelText;

        private WorldGenPanelData _lastData;

        private void OnEnable()
        {
            if (planetGridMesh != null)
                planetGridMesh.Built.AddListener(RefreshPanels);
            EnsureOrbitCameraReference();
        }

        private void EnsureOrbitCameraReference()
        {
            if (orbitCamera == null)
                orbitCamera = FindObjectOfType<PlanetOrbitCamera>();
        }

        private void OnDisable()
        {
            if (planetGridMesh != null)
                planetGridMesh.Built.RemoveListener(RefreshPanels);
        }

        /// <summary>
        /// A kontinens/régió-panel szövegen belüli `&lt;link&gt;`-kattintás
        /// figyelése - IMGUI helyett valódi Canvas/TMP UI-n ez az egyetlen
        /// mód, hogy egy hosszú, összefűzött szövegblokkon BELÜL egyes
        /// neveket kattinthatóvá tegyünk anélkül, hogy soronként külön
        /// Button-GameObject-eket kellene generálni.
        /// </summary>
        private void Update()
        {
            // ÖNGYÓGYÍTÁS (ld. PlanetGridMesh.Update() hasonló mintája): Play
            // közbeni szkript-újrafordítás (hot reload) után egy MÁR AKTÍV
            // komponensen az OnEnable() ebben a projektben tapasztaltak
            // szerint NEM fut le újra, így az ottani FindObjectOfType-fallback
            // soha nem pótolná egy hiányzó orbitCamera-referenciát egy már
            // futó Play session-ben - ezért itt, minden Update()-ben újra
            // próbálkozunk, ha még mindig null.
            EnsureOrbitCameraReference();
            UpdateViewLevelDisplay();

            if (!Input.GetMouseButtonDown(0))
                return;

            if (_lastData == null || orbitCamera == null)
            {
                Debug.LogWarning($"WorldGenPanelUI: kattintás érkezett, de nem dolgozható fel " +
                    $"(_lastData null: {_lastData == null}, orbitCamera null: {orbitCamera == null}).");
                return;
            }

            if (TryGetClickedLinkIndex(continentPanelText, out int continentIdx)
                && continentIdx >= 0 && continentIdx < _lastData.Continents.Count)
            {
                FlyToRegion(_lastData.Continents[continentIdx].CenterDirection);
                return;
            }

            if (TryGetClickedLinkIndex(regionPanelText, out int regionIdx)
                && regionIdx >= 0 && regionIdx < _lastData.Regions.Count)
            {
                FlyToRegion(_lastData.Regions[regionIdx].CenterDirection);
                return;
            }

            Debug.Log("WorldGenPanelUI: kattintás történt, de nem talált linket a kattintás pozíciójában.");
        }

        // Pixelben mert padding a link karaktereinek bounding boxa KORE -
        // ld. az osztaly-doksi "gomb helyett paddelt terulet" resze.
        private const float LinkClickPaddingX = 8f;
        private const float LinkClickPaddingY = 6f;

        // IDEIGLENES DIAGNOSZTIKA (2026-09-06, HARMADIK "nem mukodik"
        // visszajelzes utan): harom vak javitasi kor (glyph -> paddelt
        // glyph -> teljes sor) sem oldotta meg - ahelyett hogy negyedszer
        // is talalgatnank, minden tenyleges kattintaskor (nem minden
        // frame-ben, tehat nem spammel) reszletes naplot irunk, hogy a
        // KOVETKEZO teszt PONTOSAN megmutassa, hol akad el a lanc: nincs
        // link a szovegben? rossz koordinata-ter? rossz bounds? Ezt a
        // logolast erdemes eltavolitani, amint a gyokerok kiderult.
        private static bool TryGetClickedLinkIndex(TMP_Text text, out int index)
        {
            index = -1;
            if (text == null)
            {
                Debug.Log("WorldGenPanelUI [diag]: TryGetClickedLinkIndex - a text mezo NULL.");
                return false;
            }
            if (text.textInfo == null)
            {
                Debug.Log($"WorldGenPanelUI [diag]: '{text.name}' - textInfo NULL.");
                return false;
            }

            // A Canvas Screen Space - Overlay (ld. PlanetView.unity
            // m_RenderMode: 0), ezert a kamera-parameter itt szandekosan
            // null - UGYANAZ a konvencio, mint amit a korabbi
            // FindIntersectingLink-alapu verzio is hasznalt.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    text.rectTransform, Input.mousePosition, null, out Vector2 localPoint))
            {
                Debug.Log($"WorldGenPanelUI [diag]: '{text.name}' - ScreenPointToLocalPointInRectangle FALSE " +
                    $"(mousePos={Input.mousePosition}).");
                return false;
            }

            TMP_TextInfo info = text.textInfo;
            Debug.Log($"WorldGenPanelUI [diag]: '{text.name}' - mousePos={Input.mousePosition}, " +
                $"localPoint={localPoint}, rect={text.rectTransform.rect}, linkCount={info.linkCount}, " +
                $"characterCount={info.characterCount}.");

            // GYOKEROK-JAVITAS (code review, 2026-09-07): a padded sorsavok
            // FUGGOLEGESEN atfedhetik egymast szomszedos sorok kozott (a
            // 6px padding + a sor ascender/descender-je egyutt tobbnyire
            // SZELESEBB, mint maga a sormagassag - merve: kb. ketszeres
            // atfedes egy tipikus 10pt panelnel) - az ELSO egyezes (kisebb
            // linkIdx) mindig "nyert" volna, FUGGETLENUL attol, melyik sor
            // kozelebb van a tenyleges kattintashoz, csendben rossz
            // kontinenst/regiot celozva meg. JAVITAS: az OSSZES egyezo
            // linket osszegyujtjuk, majd a SOR KOZEPPONTJAHOZ (nem a sor
            // elejehez/indexehez) LEGKOZELEBBIT valasztjuk - ez helyesen
            // dont at atfedo savok eseten is.
            int bestIndex = -1;
            float bestDistance = float.PositiveInfinity;
            for (int linkIdx = 0; linkIdx < info.linkCount; linkIdx++)
            {
                bool hasBounds = TryGetPaddedLinkBounds(text, info, linkIdx, out Rect bounds);
                bool contains = hasBounds && bounds.Contains(localPoint);
                Debug.Log($"WorldGenPanelUI [diag]:   link[{linkIdx}] id=\"{info.linkInfo[linkIdx].GetLinkID()}\" " +
                    $"firstChar={info.linkInfo[linkIdx].linkTextfirstCharacterIndex} " +
                    $"length={info.linkInfo[linkIdx].linkTextLength} hasBounds={hasBounds} bounds={bounds} " +
                    $"contains={contains}.");
                if (!hasBounds || !contains)
                    continue;
                float centerY = (bounds.yMin + bounds.yMax) * 0.5f;
                float distance = Mathf.Abs(localPoint.y - centerY);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = linkIdx;
                }
            }
            if (bestIndex < 0)
                return false;
            if (int.TryParse(info.linkInfo[bestIndex].GetLinkID(), out index))
                return true;
            Debug.Log($"WorldGenPanelUI [diag]:   link[{bestIndex}] bounds matched, DE int.TryParse elbukott az id-n.");
            return false;
        }

        /// <summary>
        /// A `linkIdx`-edik link SORÁNAK teljes szélességű kattint-sávja, a
        /// TEXT SAJÁT lokális terében (ugyanaz a tér, amit a
        /// `RectTransformUtility.ScreenPointToLocalPointInRectangle` is ad).
        ///
        /// FELHASZNÁLÓI VISSZAJELZÉS (2026-09-06, MÁSODIK kör): a korábbi,
        /// csak a NÉV karaktereinek szélességére paddelt terület MÉG MINDIG
        /// túl szűk volt ("továbbra se jól kattinthatók... megküzdök").
        /// JAVÍTÁS: mivel egy kontinens/régió az esetek túlnyomó
        /// többségében EGYETLEN sorra fér (a `RefreshPanels()` egy
        /// `AppendLine`-t hív soronként), a vízszintes tesztet a SZÖVEG
        /// TELJES SZÉLESSÉGÉRE terjesztjük ki (nem csak a név glyph-
        /// szélességére) - tehát a sor BÁRMELY pontjára kattintva (a
        /// statisztika-szövegre is, nem csak a névre) aktiválja a linket.
        /// FÜGGŐLEGESEN továbbra is a SOR ascender/descender-je (nem a
        /// glyph-tinta) - ez azért fontos, mert egy csupa nagybetűs/
        /// leszáró-szár nélküli név (pl. "MOUNTAINS") glyph-tinta szerint
        /// ALACSONYABB lenne, mint egy "y"/"g" betűket tartalmazó név, ami
        /// inkonzisztens, nehezen megjósolható kattint-magasságot adna - a
        /// sormagasság mindig ugyanakkora.
        /// </summary>
        private static bool TryGetPaddedLinkBounds(TMP_Text text, TMP_TextInfo info, int linkIdx, out Rect bounds)
        {
            bounds = default;
            TMP_LinkInfo link = info.linkInfo[linkIdx];
            int firstChar = link.linkTextfirstCharacterIndex;
            int lastChar = firstChar + link.linkTextLength - 1;
            if (firstChar < 0 || lastChar < firstChar || lastChar >= info.characterCount)
                return false;

            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            bool any = false;
            for (int ci = firstChar; ci <= lastChar; ci++)
            {
                TMP_CharacterInfo ch = info.characterInfo[ci];
                if (!ch.isVisible) continue;
                any = true;
                TMP_LineInfo line = info.lineInfo[ch.lineNumber];
                minY = Mathf.Min(minY, line.descender);
                maxY = Mathf.Max(maxY, line.ascender);
            }
            if (!any) return false;

            // Vizszintesen a TELJES szoveg-szelesseg (nem csak a nev) - ld.
            // osztaly-doksi. A rect mar a szoveg pivot-jahoz viszonyitott
            // lokalis teret hasznalja, UGYANAZT, mint a fuggolegesen
            // hasznalt ascender/descender.
            Rect textRect = text.rectTransform.rect;

            bounds = Rect.MinMaxRect(
                textRect.xMin - LinkClickPaddingX, minY - LinkClickPaddingY,
                textRect.xMax + LinkClickPaddingX, maxY + LinkClickPaddingY);
            return true;
        }

        /// <summary>
        /// M9 "nézetszint-váltás" (docs/backlog.md "Kontinens/régió kamera-
        /// átmenetek") - a `PlanetOrbitCamera.CurrentViewLevel`-t (tisztán a
        /// kamera-magasságból származtatott) és a JELENLEGI nézet-irányhoz
        /// legközelebbi kontinens/régió nevét jeleníti meg. Ez a legkisebb,
        /// biztonságosan (élő Unity-teszt nélkül) megvalósítható szelete a
        /// teljes "folyamatos átmenet" célnak - a tényleges kamera-pivot
        /// áthelyezése (a nézet a kontinens/régió KÖZEPPONTJA köré, nem a
        /// bolygó középpontja köré forogjon) egy jóval nagyobb, a
        /// PlanetOrbitCamera forgás/zoom-matematikáját mélyen érintő
        /// átalakítás lenne - KÜLÖN munka.
        /// </summary>
        private void UpdateViewLevelDisplay()
        {
            if (viewLevelText == null || orbitCamera == null || _lastData == null)
                return;

            Transform planetTransform = planetGridMesh != null ? planetGridMesh.transform : null;
            Vector3 viewDirWorld = orbitCamera.CurrentViewDirection;

            string text = orbitCamera.CurrentViewLevel switch
            {
                PlanetOrbitCamera.ViewLevel.Region =>
                    "Nézet: Régió" + NearestName(_lastData.Regions, r => r.CenterDirection, r => r.Name, viewDirWorld, planetTransform),
                PlanetOrbitCamera.ViewLevel.Continent =>
                    "Nézet: Kontinens" + NearestName(_lastData.Continents, c => c.CenterDirection, c => c.Name, viewDirWorld, planetTransform),
                _ => "Nézet: Bolygó",
            };
            viewLevelText.text = text;

            // GYOKEROK-JAVITAS (2026-09-07): a rovid, egysoros doboz konnyen
            // kisebbre kerulhet, mint a beallitott fontSize tenyleges
            // sormagassaga - ez a TMP SAJAT, beepitett fuggoleges
            // tulcsordulas-vagasaval a TELJES sort lathatatlanna teheti.
            // Kenyszeritett "Overflow" mod, fuggetlenul a RectTransform
            // meretetol, hogy ez sose okozzon eltuno szoveget.
            if (viewLevelText.overflowMode != TextOverflowModes.Overflow)
                viewLevelText.overflowMode = TextOverflowModes.Overflow;

            // Minden frame-ben (nem csak OnEnable()-ben) a legfelso
            // testverre kenyszeritve, hogy hot-reload utan is biztosan
            // ervenyben maradjon, es ne takarhassa el egy masik,
            // kesobb hozzaadott Canvas-gyerek.
            viewLevelText.transform.SetAsLastSibling();
        }

        /// <summary>A `viewDirWorld`-höz (kamera aktuális nézet-iránya) LEGKÖZELEBBI elem neve, " — Név" formában (üres string, ha nincs elem).</summary>
        private static string NearestName<T>(
            List<T> items, Func<T, Vector3> centerDirectionLocal, Func<T, string> name,
            Vector3 viewDirWorld, Transform planetTransform) where T : class
        {
            if (items == null || items.Count == 0) return "";
            T best = null;
            float bestDot = float.NegativeInfinity;
            foreach (T item in items)
            {
                Vector3 dirLocal = centerDirectionLocal(item);
                Vector3 dirWorld = planetTransform != null ? planetTransform.TransformDirection(dirLocal) : dirLocal;
                float dot = Vector3.Dot(dirWorld.normalized, viewDirWorld);
                if (dot > bestDot) { bestDot = dot; best = item; }
            }
            return best != null ? $" — {name(best)}" : "";
        }

        private void FlyToRegion(Vector3 centerDirectionLocal)
        {
            // A CenterDirection a bolygo LOKALIS tereben van (ld.
            // WorldGenPanelData.CenterDirection doksi) - ha a bolygo
            // transform-ja el van forgatva/skalazva, at kell alakitani
            // vilagterbe, mielott a kamera (ami vilagter-iranyat var) megkapja.
            Transform planetTransform = planetGridMesh != null ? planetGridMesh.transform : null;
            Vector3 worldDirection = planetTransform != null
                ? planetTransform.TransformDirection(centerDirectionLocal)
                : centerDirectionLocal;

            // IDEIGLENES DIAGNOSZTIKA (2026-09-06): a kattintas-detektalas
            // MAR bizonyítottan mukodik (ld. a WorldGenPanelUI [diag] naplo
            // a TryGetClickedLinkIndex-bol) - ha megis "semmi nem tortenik",
            // a hiba EBBEN a lancban van (nulla-iranyu CenterDirection ->
            // FlyToDirection nema early-return, VAGY a pitch/yaw inverz
            // keplet rossz celra viszi a kamerat). Ez a naplo pontosan
            // megmutatja, melyik.
            Debug.Log($"WorldGenPanelUI [diag]: FlyToRegion - centerDirectionLocal={centerDirectionLocal} " +
                $"(sqrMagnitude={centerDirectionLocal.sqrMagnitude}), worldDirection={worldDirection}, " +
                $"planetTransform={(planetTransform != null ? planetTransform.name : "NULL")}, " +
                $"orbitCamera={(orbitCamera != null ? orbitCamera.name : "NULL")}, flyToAltitude={flyToAltitude}, " +
                $"flyToDurationSeconds={flyToDurationSeconds}.");

            orbitCamera.FlyToDirection(worldDirection, newAltitudeAboveSurface: flyToAltitude, durationSeconds: flyToDurationSeconds);
        }

        [ContextMenu("Refresh Panels")]
        public void RefreshPanels()
        {
            if (planetGridMesh == null)
            {
                Debug.LogWarning("WorldGenPanelUI: nincs beállítva a Planet Grid Mesh referencia.");
                return;
            }

            WorldGenPanelData data;
            try
            {
                data = planetGridMesh.ComputePanelData();
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogWarning($"WorldGenPanelUI: {ex.Message} (előbb futtasd a Rebuild-et a Planet Grid Mesh-en).");
                return;
            }

            if (worldPanelText != null)
            {
                worldPanelText.text =
                    $"<b>{data.World.Name}</b>\n" +
                    $"Seed: {data.World.SeedDisplay}\n" +
                    $"Ocean coverage: {data.World.OceanCoveragePercent:F1}%\n" +
                    $"Habitability: {data.World.HabitabilityLevel} ({data.World.HabitabilityPercent:F1}%)";
            }

            // A nev <link="index">-be csomagolva (+ ala huzva/szinezve, hogy
            // lathatoan kattinthato legyen) - az Update() ezt a linket
            // azonositja FindIntersectingLink-kel kattintaskor, ld. FlyToRegion.
            if (continentPanelText != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<b>Continents</b>");
                for (int i = 0; i < data.Continents.Count; i++)
                {
                    ContinentPanelData c = data.Continents[i];
                    sb.AppendLine($"<link=\"{i}\"><u><color=#7FD1FF>{c.Name}</color></u></link> — {c.AreaTiles} tile, {c.BiomeCount} biome, "
                        + $"dominant: {c.DominantBiome}, {c.RiverBasinCount} basins, {c.RiverMouthCount} mouths, "
                        + $"coastal complexity: {c.CoastalComplexityLevel} ({c.CoastalComplexity:F2})");
                }
                continentPanelText.text = sb.ToString();
            }

            if (regionPanelText != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<b>Regions (top 10)</b>");
                for (int i = 0; i < data.Regions.Count; i++)
                {
                    RegionPanelData r = data.Regions[i];
                    sb.AppendLine($"<link=\"{i}\"><u><color=#7FD1FF>{r.Name}</color></u></link> — {r.AreaTiles} tile, dominant: {r.DominantBiome}, "
                        + $"{r.RiverMouthCount} mouths, landform: {r.LandformType}");
                }
                regionPanelText.text = sb.ToString();
            }

            _lastData = data;
        }
    }
}
