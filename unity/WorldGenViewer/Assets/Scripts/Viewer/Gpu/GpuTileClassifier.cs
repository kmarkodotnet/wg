using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Core.Events;

namespace WorldGen.Viewer.Gpu
{
    /// <summary>
    /// Egy tile-klasszifikáció GPU-n kiszámított eredménye - a
    /// PlanetGridMesh saját (privát) AdaptiveTileClassification-jévé
    /// alakítandó a hívó oldalon (Category/Bucket, ami render-specifikus,
    /// itt nem szerepel).
    /// </summary>
    public struct GpuClassificationResult
    {
        public float Elevation;
        public bool IsOceanic;
        public byte Biome; // WorldGen.Core.Climate.Biome sorrendje: Ocean=0..Tropical=5
        public bool IsCratered;
    }

    /// <summary>
    /// A TileClassification.compute dispatch-oldali fele. A shader a
    /// WorldGen.Core determinisztikus lánc EGY RÉSZÉT (DomainWarp,
    /// PlateGeneration, PlateBoundaryEffect, CrustElevation, Temperature,
    /// BiomeClassification) reprodukálja float32-ben - ld. a .compute
    /// fájl fejlécét a dokumentált CPU/GPU eltérésekről.
    ///
    /// SZÁNDÉKOSAN NEM MonoBehaviour - egyszerű, újrahasználható
    /// dispatch-objektum, amit a PlanetGridMesh tart életben és hív.
    /// </summary>
    public sealed class GpuTileClassifier
    {
        private readonly ComputeShader _shader;
        private readonly int _kernel;

        public GpuTileClassifier(ComputeShader shader)
        {
            _shader = shader != null ? shader : throw new ArgumentNullException(nameof(shader));
            _kernel = shader.FindKernel("CSClassifyTiles");
        }

        /// <summary>
        /// A `positions` sorrendjével megegyező eredménytömböt ad vissza.
        /// Minden bemeneti tömb/lista a hívó felelőssége alatt marad
        /// (nincs belső gyorsítótárazás) - a PlanetGridMesh dönti el, mikor
        /// éri meg egyáltalán meghívni (pl. csak akkor, ha a CPU-s
        /// gyorsítótárban hiányzó tile-ok száma indokolja a dispatch
        /// rezsijét).
        /// </summary>
        public GpuClassificationResult[] Classify(
            Vector3[] positions,
            ulong worldSeed,
            (double X, double Y, double Z)[] plateSeeds,
            bool[] plateIsOceanic,
            IReadOnlyList<ImpactCratering.CraterRecord> craters,
            double seaLevel,
            double dayT,
            double orbitalPeriodDays,
            double rotationPeriodDays,
            double axialTiltRad)
        {
            int tileCount = positions.Length;
            if (tileCount == 0)
                return Array.Empty<GpuClassificationResult>();

            int plateCount = plateSeeds.Length;
            var plateSeedsF = new Vector3[plateCount];
            var plateOceanicU = new uint[plateCount];
            for (int i = 0; i < plateCount; i++)
            {
                plateSeedsF[i] = new Vector3((float)plateSeeds[i].X, (float)plateSeeds[i].Y, (float)plateSeeds[i].Z);
                plateOceanicU[i] = plateIsOceanic[i] ? 1u : 0u;
            }

            int craterCount = craters?.Count ?? 0;
            var craterPosCos = new Vector4[craterCount];
            var craterDepths = new float[craterCount];
            for (int i = 0; i < craterCount; i++)
            {
                ImpactCratering.CraterRecord c = craters[i];
                craterPosCos[i] = new Vector4((float)c.X, (float)c.Y, (float)c.Z, (float)c.CosAngularRadius);
                craterDepths[i] = (float)c.DepthMeters;
            }

            using ComputeBuffer posBuf = new ComputeBuffer(tileCount, sizeof(float) * 3);
            posBuf.SetData(positions);

            using ComputeBuffer plateSeedBuf = new ComputeBuffer(Math.Max(1, plateCount), sizeof(float) * 3);
            if (plateCount > 0) plateSeedBuf.SetData(plateSeedsF);

            using ComputeBuffer plateOceanicBuf = new ComputeBuffer(Math.Max(1, plateCount), sizeof(uint));
            if (plateCount > 0) plateOceanicBuf.SetData(plateOceanicU);

            using ComputeBuffer craterBuf = new ComputeBuffer(Math.Max(1, craterCount), sizeof(float) * 4);
            if (craterCount > 0) craterBuf.SetData(craterPosCos);

            using ComputeBuffer craterDepthBuf = new ComputeBuffer(Math.Max(1, craterCount), sizeof(float));
            if (craterCount > 0) craterDepthBuf.SetData(craterDepths);

            using ComputeBuffer outElevation = new ComputeBuffer(tileCount, sizeof(float));
            using ComputeBuffer outIsOceanic = new ComputeBuffer(tileCount, sizeof(uint));
            using ComputeBuffer outBiome = new ComputeBuffer(tileCount, sizeof(uint));
            using ComputeBuffer outIsCratered = new ComputeBuffer(tileCount, sizeof(uint));

            _shader.SetBuffer(_kernel, "_positions", posBuf);
            _shader.SetBuffer(_kernel, "_plateSeeds", plateSeedBuf);
            _shader.SetBuffer(_kernel, "_plateIsOceanic", plateOceanicBuf);
            _shader.SetBuffer(_kernel, "_craters", craterBuf);
            _shader.SetBuffer(_kernel, "_craterDepths", craterDepthBuf);
            _shader.SetBuffer(_kernel, "_outElevation", outElevation);
            _shader.SetBuffer(_kernel, "_outIsOceanic", outIsOceanic);
            _shader.SetBuffer(_kernel, "_outBiome", outBiome);
            _shader.SetBuffer(_kernel, "_outIsCratered", outIsCratered);

            int seedLo = unchecked((int)(uint)(worldSeed & 0xFFFFFFFFUL));
            int seedHi = unchecked((int)(uint)(worldSeed >> 32));
            _shader.SetInts("_worldSeed", seedLo, seedHi);
            _shader.SetInt("_plateCount", plateCount);
            _shader.SetInt("_craterCount", craterCount);
            _shader.SetInt("_tileCount", tileCount);
            _shader.SetFloat("_seaLevel", (float)seaLevel);
            _shader.SetFloat("_climateDayT", (float)dayT);
            _shader.SetFloat("_orbitalPeriodDays", (float)orbitalPeriodDays);
            _shader.SetFloat("_rotationPeriodDays", (float)rotationPeriodDays);
            _shader.SetFloat("_axialTiltRad", (float)axialTiltRad);

            // D3D-limit: egy dispatch-dimenzioban max 65535 thread-group -
            // nagy tile-szamnal (pl. a teljes base-reteg egyetlen hivasban)
            // ezt tullepnenk 1D dispatch-csel, ami kivetelt dob (Dispatch()
            // maga ellenorzi es dob) - ELMENT, a bufferek soha nem irodnak,
            // GetData() szemetet ad vissza (ez okozta a "vörös bolygó, tul
            // magas eleváció" hibat elozo tesztkorben). 2D racsra bontva
            // 65535*65535*64 tile-ig skalazodik, tobb, mint amire barmikor
            // szukseg lehet.
            int totalGroups = Mathf.Max(1, Mathf.CeilToInt(tileCount / 64f));
            int groupsX = Mathf.Min(totalGroups, 65535);
            int groupsY = Mathf.CeilToInt((float)totalGroups / groupsX);
            _shader.SetInt("_dispatchGroupsX", groupsX);
            _shader.Dispatch(_kernel, groupsX, groupsY, 1);

            var elevationData = new float[tileCount];
            var isOceanicData = new uint[tileCount];
            var biomeData = new uint[tileCount];
            var isCrateredData = new uint[tileCount];
            outElevation.GetData(elevationData);
            outIsOceanic.GetData(isOceanicData);
            outBiome.GetData(biomeData);
            outIsCratered.GetData(isCrateredData);

            var results = new GpuClassificationResult[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                results[i] = new GpuClassificationResult
                {
                    Elevation = elevationData[i],
                    IsOceanic = isOceanicData[i] != 0,
                    Biome = (byte)biomeData[i],
                    IsCratered = isCrateredData[i] != 0,
                };
            }
            return results;
        }
    }
}
