using UnityEngine;
using WorldGen.Core.Terrain;
using WorldGen.Viewer.Lod;

namespace WorldGen.Viewer
{
    /// <summary>
    /// ND-151 (M13 4. fázis): a per-pixel felszíni mikro-részlet uniformjai.
    ///
    /// EZ A FÁJL CSAK ÁTADÁS. A számok forrása a Core
    /// (<see cref="SurfaceMicroDetail"/>) és a motorfüggetlen, tesztelt
    /// sávválasztó (<see cref="MicroDetailBand"/>); a shader ezekből dolgozik.
    /// Se mesh-csatorna, se textúra, se extra memória nem tartozik hozzá:
    /// frame-enként néhány uniform, azon kívül nulla költség a CPU-n.
    ///
    /// MIÉRT NEM A GPU-GEOMETRIA (az eredeti M13 2-3. fázis). Ld. az ND-151
    /// döntést: a `TileClassification.compute` GPU-elevációs útját az ND-128
    /// MÉRÉS ALAPJÁN törölte (a tile-ok ~22%-a a tengerszint másik oldalára
    /// került, és lassabb is volt). Az a hibaosztály abból származott, hogy a
    /// GPU-n számolt érték a MODELL BEMENETE lett. A mikro-részlet ennek az
    /// ellentéte: a GPU-n számolt érték KIZÁRÓLAG kimenet (árnyalás), ezért
    /// ugyanazt a látványcélt (a mesh-felbontás alatti felszíni részletet)
    /// determinizmus-kockázat nélkül éri el.
    /// </summary>
    public partial class PlanetGridMesh
    {
        [Header("Felszíni mikro-részlet (ND-151, M13 4. fázis)")]
        [SerializeField, Tooltip("Per-pixel, a mesh-felbontás ALATTI felszíni részlet: a normál perturbációja és az albedó modulálása a modell saját fBm-létrájának folytatásából. Kikapcsolva a kép bitre az ND-151 előtti.")]
        private bool surfaceMicroDetail = true;

        [SerializeField, Range(0f, 2f), Tooltip("A mikro-részlet erőssége. 1 = a Core-ban kalibrált amplitúdó; 0 = kikapcsolva. A látvány-ítélethez élőben hangolható.")]
        private float microDetailStrength = 1f;

        private static readonly int MicroDetailBandId = Shader.PropertyToID("_MicroDetailBand");
        private static readonly int MicroDetailPhaseId = Shader.PropertyToID("_MicroDetailPhase");
        private static readonly int MicroDetailResponseId = Shader.PropertyToID("_MicroDetailResponse");
        private static readonly int MicroDetailShapeId = Shader.PropertyToID("_MicroDetailShape");
        private static readonly int MicroDetailSubmergedId = Shader.PropertyToID("_MicroDetailSubmerged");
        private static readonly int MicroDetailGeometryId = Shader.PropertyToID("_MicroDetailGeometry");
        private static readonly int MicroWorldToPlanetId = Shader.PropertyToID("_MicroWorldToPlanet");
        private static readonly int MicroDetailStrengthId = Shader.PropertyToID("_MicroDetailStrength");
        private static readonly int MicroDetailEnableId = Shader.PropertyToID("_MicroDetailEnable");

        private ulong _microDetailPhaseSeed;
        private bool _microDetailPhaseValid;
        private Vector4 _microDetailPhase;

        /// <summary>
        /// A mikro-részlet akkor és csak akkor aktív, ha be van kapcsolva, van
        /// erőssége, és NEM megy adat-overlay. Az overlay-ek (tektonika, szél,
        /// csapadék, hő) SZÁNDÉKOSAN kizárják: ott a szín egy MÉRT mennyiség
        /// palettája (I4), amit egy részlet-moduláció félreolvashatóvá tenne.
        /// </summary>
        private bool MicroDetailActive =>
            surfaceMicroDetail && microDetailStrength > 0f
            && surfaceOverlayMode == SurfaceOverlayMode.None;

        /// <summary>
        /// Frame-enkénti uniform-frissítés. Az <see cref="UpdateSurfaceLightingUniforms"/>
        /// hívja, ugyanabban a LateUpdate-ben - ugyanaz a nagyságrend (néhány
        /// uniform egy megosztott anyagon).
        /// </summary>
        private void UpdateMicroDetailUniforms()
        {
            if (_vertexColorMaterial == null && _waterSurfaceMaterial == null)
                return;

            // ANYAG-szintű kapu: a vízfelszín ugyanezt a shadert használja, de
            // egy óceánfelszín nem kőzetes szemcsés (a víz-shader saját
            // csillanás-paramétereket kap, ld. CreateWaterSurfaceMaterial).
            if (_vertexColorMaterial != null)
                _vertexColorMaterial.SetFloat(MicroDetailEnableId, 1f);
            if (_waterSurfaceMaterial != null)
                _waterSurfaceMaterial.SetFloat(MicroDetailEnableId, 0f);

            if (!MicroDetailActive)
            {
                Shader.SetGlobalFloat(MicroDetailStrengthId, 0f);
                return;
            }

            MicroDetailBand.Band band = SelectMicroDetailBand();
            Shader.SetGlobalFloat(MicroDetailStrengthId, microDetailStrength);
            Shader.SetGlobalVector(MicroDetailBandId, new Vector4(
                (float)band.BaseFrequency, (float)band.Fraction, (float)band.Visibility, 0f));
            Shader.SetGlobalVector(MicroDetailPhaseId, MicroDetailPhase());
            Shader.SetGlobalVector(MicroDetailResponseId, new Vector4(
                (float)SurfaceMicroDetail.PlainsNormalAmplitude,
                (float)SurfaceMicroDetail.RockNormalAmplitude,
                (float)SurfaceMicroDetail.PlainsAlbedoJitter,
                (float)SurfaceMicroDetail.RockAlbedoJitter));
            Shader.SetGlobalVector(MicroDetailShapeId, new Vector4(
                (float)SurfaceMicroDetail.SlopeReferenceSin,
                (float)SurfaceMicroDetail.RockAltitudeMeters,
                (float)SurfaceMicroDetail.RockFrequencyScale,
                (float)SurfaceMicroDetail.SubmergenceBandMeters));
            Shader.SetGlobalVector(MicroDetailSubmergedId, new Vector4(
                (float)SurfaceMicroDetail.SubmergedNormalScale,
                (float)SurfaceMicroDetail.SubmergedAlbedoScale, 0f, 0f));
            // A shader ezekből nyeri vissza az elevációt - pontosan a
            // WorldElevationFromDisplacedRadius inverzeként, ÚJ Core-kiértékelés
            // nélkül (ugyanaz az elv, mint az ND-129/ND-149 parti gyűrűjénél).
            Shader.SetGlobalVector(MicroDetailGeometryId, new Vector4(
                radius, (float)elevationScale, (float)terrainReliefExaggeration, (float)_adaptiveSeaLevel));
            Shader.SetGlobalMatrix(MicroWorldToPlanetId, transform.worldToLocalMatrix);
        }

        /// <summary>
        /// A seedből származó zaj-fázis. Egyszer számolódik világonként; a
        /// <see cref="SurfaceMicroDetail.PhaseOffset"/> a Decorative
        /// random-domainben van, tehát NEM a világmodell része, de
        /// determinisztikus (ugyanaz a seed = ugyanaz a mikro-részlet).
        /// </summary>
        private Vector4 MicroDetailPhase()
        {
            ulong seed = WorldSeedUnsigned;
            if (_microDetailPhaseValid && _microDetailPhaseSeed == seed)
                return _microDetailPhase;
            SurfaceMicroDetail.PhaseOffset(seed, out double px, out double py, out double pz);
            _microDetailPhase = new Vector4((float)px, (float)py, (float)pz, 0f);
            _microDetailPhaseSeed = seed;
            _microDetailPhaseValid = true;
            return _microDetailPhase;
        }

        private MicroDetailBand.Band SelectMicroDetailBand()
        {
            Camera cam = GetAdaptiveCamera();
            if (cam == null)
                return new MicroDetailBand.Band(SurfaceMicroDetail.BaseFrequency, 0.0, 0.0);
            // A kamera-bolygóközéppont távolság a BOLYGÓ transzformjában -
            // így az ND-19 floating origin eltolása nem számít bele.
            double distance = transform.InverseTransformPoint(cam.transform.position).magnitude;
            return MicroDetailBand.Select(
                distance, radius, cam.fieldOfView * Mathf.Deg2Rad, Mathf.Max(cam.pixelHeight, 1));
        }
    }
}
