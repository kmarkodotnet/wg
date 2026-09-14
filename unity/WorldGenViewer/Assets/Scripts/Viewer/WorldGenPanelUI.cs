using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace WorldGen.Viewer
{
    /// <summary>
    /// M8 panel-UI (régi, Canvas/TextMeshPro alapú): a
    /// PlanetGridMesh.ComputePanelData() eredményéből a World-összefoglalót
    /// és a nézetszint-kijelzést írja ki. Csak megjelenítés - semmilyen adat
    /// itt nem számolódik, minden a Core-ból jön (I4).
    ///
    /// ELTÁVOLÍTVA (2026-09-13, felhasználói kérés): a korábbi kattintható
    /// kontinens-lista ("Continents") és régió-lista ("Regions (top 10)"),
    /// valamint a hozzájuk tartozó TMP `&lt;link&gt;`-kattintás-detektálás és
    /// kamera-repülés. Ezt a funkciót az új IMGUI-navigáció
    /// (PlanetGridMesh.DrawNavigationPanel - breadcrumb, Vissza gomb, lista)
    /// váltotta ki, ami ugyanarra a WorldGenPanelData-ra épül. A scene-ben a
    /// ContinentPanelText / RegionPanelText GameObjectek inaktívak.
    /// Részletek: history/2026-09-13-legacy-panel-removal.md.
    /// </summary>
    public class WorldGenPanelUI : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("A bolygó-mesh, aminek az adataiból a panelek épülnek. Build()-nek " +
                 "már le kellett futnia rajta, mielőtt Refresh Panels-t hívsz.")]
        private PlanetGridMesh planetGridMesh;

        [SerializeField]
        [Tooltip("A kamera, aminek a nézetszintjét és nézet-irányát a View Level Text " +
                 "kiírja. Üresen hagyva automatikusan megkeresi a jelenetben lévő " +
                 "PlanetOrbitCamera-t.")]
        private PlanetOrbitCamera orbitCamera;

        [SerializeField] private TMP_Text worldPanelText;

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

            _lastData = data;
        }
    }
}
