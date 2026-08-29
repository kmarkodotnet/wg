using System;
using System.Text;
using TMPro;
using UnityEngine;

namespace WorldGen.Viewer
{
    /// <summary>
    /// M8 panel-UI: a PlanetGridMesh.ComputePanelData() eredményét írja ki
    /// három TextMeshPro mezőbe (World / Continents / Regions). Csak
    /// megjelenítés - semmilyen adat itt nem számolódik, minden a Core-ból
    /// jön (I4).
    /// </summary>
    public class WorldGenPanelUI : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("A bolygó-mesh, aminek az adataiból a panelek épülnek. Build()-nek " +
                 "már le kellett futnia rajta, mielőtt Refresh Panels-t hívsz.")]
        private PlanetGridMesh planetGridMesh;

        [SerializeField] private TMP_Text worldPanelText;
        [SerializeField] private TMP_Text continentPanelText;
        [SerializeField] private TMP_Text regionPanelText;

        private void OnEnable()
        {
            if (planetGridMesh != null)
                planetGridMesh.Built.AddListener(RefreshPanels);
        }

        private void OnDisable()
        {
            if (planetGridMesh != null)
                planetGridMesh.Built.RemoveListener(RefreshPanels);
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
                    $"Ocean coverage: {data.World.OceanCoveragePercent:F1}%";
            }

            if (continentPanelText != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<b>Continents</b>");
                foreach (ContinentPanelData c in data.Continents)
                {
                    sb.AppendLine($"{c.Name} — {c.AreaTiles} tile, {c.BiomeCount} biome, "
                        + $"dominant: {c.DominantBiome}, {c.RiverBasinCount} basins, {c.RiverMouthCount} mouths");
                }
                continentPanelText.text = sb.ToString();
            }

            if (regionPanelText != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<b>Regions (top 10)</b>");
                foreach (RegionPanelData r in data.Regions)
                {
                    sb.AppendLine($"{r.Name} — {r.AreaTiles} tile, dominant: {r.DominantBiome}, {r.RiverMouthCount} mouths");
                }
                regionPanelText.text = sb.ToString();
            }
        }
    }
}
