using System.Collections.Generic;
using UnityEngine;
using WorldGen.Core.Tectonics;

namespace WorldGen.Viewer
{
    /// <summary>
    /// Felhasználói kérés (2026-09-13, docs/backlog.md): tektonikuslemez-
    /// overlay Play módban - deep time során a MÁR MEGLÉVŐ lemez-mozgást
    /// mutatja (<see cref="PlateMotion.MovedSeeds"/>, amit a Build() már
    /// kiszámol <c>_adaptiveSeeds</c>-be), lemezenként saját színnel és
    /// névvel (<see cref="PlatePresentation"/>). "Részletes domborzat nem
    /// kell egyelőre" - ez a réteg UGYANAZT a felszín-geometriát színezi
    /// újra, mint a többi overlay (wind/precipitation mintája), nem épít
    /// külön mesh-et vagy szimulációt.
    /// </summary>
    public partial class PlanetGridMesh
    {
        [Header("Tektonikus lemez overlay")]
        [SerializeField]
        [Tooltip("Be/ki - a felszín a lemez-hovatartozás szerint színeződik (kölcsönösen kizárja a szél-/csapadék-/hő-overlay-t).")]
        private bool tectonicPlateOverlay = false;

        /// <summary>
        /// Egy sarokpont színe a tektonikus overlay-ben - a MÁR MEGLÉVŐ,
        /// deep-time-mozgatott <c>_adaptiveSeeds</c>-hez legközelebbi lemez
        /// (<see cref="PlateGeneration.AssignPlate"/>), majd annak
        /// determinisztikus színe (<see cref="PlatePresentation.PlateColorRgb"/>).
        /// Csak OLVAS (Build óta változatlan _adaptiveSeed/_adaptiveSeeds) -
        /// szálbiztos a párhuzamos sarok-szín-előszámításból, ugyanúgy, mint
        /// a <see cref="WindSpeedColorAt"/>.
        /// </summary>
        private Color TectonicPlateColorAt(Vector3 displacedCornerPos)
        {
            Vector3 dir = displacedCornerPos.normalized;
            BodyFrameConversion.ToCore(dir, out double cx, out double cy, out double cz);

            int plateId = PlateGeneration.AssignPlate(cx, cy, cz, _adaptiveSeeds);
            if (plateId < 0) return Color.magenta; // elvben sose fordul elo (ld. AssignPlate doksi) - diagnosztikai jelzes, ha megis

            PlatePresentation.PlateColorRgb(_adaptiveSeed, plateId, out double r, out double g, out double b);
            return new Color((float)r, (float)g, (float)b);
        }

        /// <summary>
        /// A jelenlegi világ mind a <c>plateCount</c> lemezének neve/színe/
        /// kéregtípusa - a jelmagyarázat-dobozhoz. A lemez-AZONOSSÁG (szín,
        /// név, kéregtípus) nem függ a deep-time-tól, csak a MAGPONT
        /// POZÍCIÓJA mozog - ezért ez cache-elhető, és csak akkor kell
        /// újraszámolni, ha a Build() ÚJ világot generált (más seed/plateCount).
        /// </summary>
        private List<(string name, Color color, bool isOceanic)> _tectonicPlateLegendCache;
        private ulong _tectonicPlateLegendCacheSeed;
        private int _tectonicPlateLegendCachePlateCount;

        private List<(string name, Color color, bool isOceanic)> GetTectonicPlateLegend()
        {
            if (_tectonicPlateLegendCache != null
                && _tectonicPlateLegendCacheSeed == _adaptiveSeed
                && _tectonicPlateLegendCachePlateCount == plateCount)
                return _tectonicPlateLegendCache;

            var legend = new List<(string, Color, bool)>(plateCount);
            for (int p = 0; p < plateCount; p++)
            {
                bool isOceanic = WorldGen.Core.Tectonics.CrustElevation.IsOceanic(_adaptiveSeed, p);
                string name = PlatePresentation.PlateName(_adaptiveSeed, p, isOceanic);
                PlatePresentation.PlateColorRgb(_adaptiveSeed, p, out double r, out double g, out double b);
                legend.Add((name, new Color((float)r, (float)g, (float)b), isOceanic));
            }

            _tectonicPlateLegendCache = legend;
            _tectonicPlateLegendCacheSeed = _adaptiveSeed;
            _tectonicPlateLegendCachePlateCount = plateCount;
            return legend;
        }

        private Vector2 _tectonicLegendScroll;

        /// <summary>
        /// Jelmagyarázat-doboz - CSAK akkor jelenik meg, ha az overlay be
        /// van kapcsolva. Színminta (GUI.DrawTexture + GUI.color tint,
        /// Texture2D.whiteTexture - nincs új textúra-erőforrás) + név +
        /// óceáni/kontinentális jelzés, lemezenként.
        /// </summary>
        private float DrawTectonicPlateLegend(float x, float y, float w, float rowHeight)
        {
            if (!tectonicPlateOverlay) return 0f;

            List<(string name, Color color, bool isOceanic)> legend = GetTectonicPlateLegend();
            const int maxVisibleRows = 8;
            float listHeight = rowHeight * System.Math.Min(maxVisibleRows, legend.Count);
            float boxHeight = rowHeight * 1.6f + listHeight + 16f;
            GUI.Box(new Rect(x - 6f, y - 6f, w + 12f, boxHeight), "Tektonikus lemezek");

            float rowY = y + rowHeight * 0.6f;
            GUI.Label(new Rect(x, rowY, w, rowHeight), $"{legend.Count} lemez - deep time szerint mozgatva");
            rowY += rowHeight;

            var listArea = new Rect(x, rowY, w, listHeight);
            var viewRect = new Rect(0, 0, w - 20f, legend.Count * rowHeight);
            _tectonicLegendScroll = GUI.BeginScrollView(listArea, _tectonicLegendScroll, viewRect);
            const float swatchSize = 14f;
            for (int i = 0; i < legend.Count; i++)
            {
                float ry = i * rowHeight;
                var swatchRect = new Rect(0, ry + (rowHeight - swatchSize) * 0.5f, swatchSize, swatchSize);
                Color prevColor = GUI.color;
                GUI.color = legend[i].color;
                GUI.DrawTexture(swatchRect, Texture2D.whiteTexture);
                GUI.color = prevColor;

                string crustTag = legend[i].isOceanic ? "óceáni" : "kontinentális";
                GUI.Label(new Rect(swatchSize + 8f, ry, viewRect.width - swatchSize - 8f, rowHeight),
                    $"{legend[i].name} ({crustTag})");
            }
            GUI.EndScrollView();

            return boxHeight + 10f;
        }
    }
}
