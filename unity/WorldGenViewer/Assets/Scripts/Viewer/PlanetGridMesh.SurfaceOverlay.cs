using UnityEngine;
using UnityEngine.Serialization;
using WorldGen.Viewer.Lod;

namespace WorldGen.Viewer
{
    public partial class PlanetGridMesh
    {
        [Header("Felszíni adatnézet (ND-141)")]
        [SerializeField]
        private SurfaceOverlayMode surfaceOverlayMode;

        [SerializeField, HideInInspector] private int surfaceOverlaySerializationVersion;
        [SerializeField, HideInInspector, FormerlySerializedAs("windSpeedOverlay")] private bool legacyWindSpeedOverlay;
        [SerializeField, HideInInspector, FormerlySerializedAs("precipitationOverlay")] private bool legacyPrecipitationOverlay;
        [SerializeField, HideInInspector, FormerlySerializedAs("tectonicPlateOverlay")] private bool legacyTectonicPlateOverlay;
        [SerializeField, HideInInspector, FormerlySerializedAs("thermalOverlayMode")] private int legacyThermalOverlayMode;

        // Csak lekérdezések: egyik fogyasztó sem tarthat fenn külön állapotot.
        private bool windSpeedOverlay => surfaceOverlayMode == SurfaceOverlayMode.WindSpeed;
        private bool precipitationOverlay => surfaceOverlayMode == SurfaceOverlayMode.Precipitation;
        private bool tectonicPlateOverlay => surfaceOverlayMode == SurfaceOverlayMode.TectonicPlates;
        private bool ThermalOverlayVisible => SurfaceOverlaySelection.IsThermal(surfaceOverlayMode);

        private void MigrateSurfaceOverlay()
        {
            if (surfaceOverlaySerializationVersion >= 1) return;
            if (surfaceOverlayMode == SurfaceOverlayMode.None)
                surfaceOverlayMode = SurfaceOverlaySelection.FromLegacy(legacyThermalOverlayMode,
                    legacyTectonicPlateOverlay, legacyWindSpeedOverlay, legacyPrecipitationOverlay);
            surfaceOverlaySerializationVersion = 1;
            legacyThermalOverlayMode = 0;
            legacyTectonicPlateOverlay = legacyWindSpeedOverlay = legacyPrecipitationOverlay = false;
        }

        private void SetSurfaceOverlayToggle(SurfaceOverlayMode mode, bool enabled)
        {
            if (enabled) surfaceOverlayMode = mode;
            else if (surfaceOverlayMode == mode) surfaceOverlayMode = SurfaceOverlayMode.None;
        }
    }
}
