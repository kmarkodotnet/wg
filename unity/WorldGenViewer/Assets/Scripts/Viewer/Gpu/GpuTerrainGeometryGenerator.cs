using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Core.Events;

namespace WorldGen.Viewer.Gpu
{
    /// <summary>
    /// Egy negyszög (4 sarok) GPU-n kiszámított geometriája - a sarkok
    /// sorrendje MINDIG p00,p10,p11,p01 (ugyanaz a konvenció, mint a
    /// CPU-s PlanetGridMesh.EmitAdaptiveTile/AddQuad-ban).
    /// </summary>
    public struct GpuQuadResult
    {
        public Vector3 P00, P10, P11, P01;
        public Vector3 Normal;
        public Color C00, C10, C11, C01;
        /// <summary>Tile-KÖZÉPPONT elevációja/óceáni státusza/biome-ja - a kategória/bucket eldöntéséhez (ld. AdaptiveTileClassification).</summary>
        public float CenterElevation;
        public bool CenterIsOceanic;
        public byte CenterBiome;
    }

    /// <summary>
    /// M13 Fázis 3: a TileClassification.compute CSGenerateTerrainGeometry
    /// kernelének dispatch-oldali fele. A drága per-tile láncot (fraktál-
    /// zaj + hőmérséklet + folytonos szín, ld. PlanetGridMesh.
    /// ContinuousCornerColorAuto/ContinuousLandBiomeColor/
    /// ContinuousOceanRockColor CPU-s megfelelőit) GPU-n, több ezer szálon
    /// futtatja - a CPU csak a tile-listát tölti fel és az eredményt
    /// olvassa vissza, NEM maga számol.
    ///
    /// SZÁNDÉKOSAN NEM zéró-másolásos (nincs DrawProceduralIndirect) - az
    /// eredmény visszaolvasódik a CPU-ra és a MEGLÉVŐ, bevált
    /// AddQuad/BuildMultiMaterialMesh csővezetékbe táplálódik. Ez a
    /// kockázat-kezelt köztes lépés: a drága SZÁMÍTÁS GPU-ra kerül, de a
    /// RENDERELÉSI út (amit élő Unity nélkül nem lehet leellenőrizni)
    /// változatlan marad.
    /// </summary>
    public sealed class GpuTerrainGeometryGenerator
    {
        // Unity NEM ad "Vector4Int"-et (csak Vector2Int/Vector3Int) - sajat,
        // a HLSL uint4-gyel (16 byte, szekvencialis 4x int) MEGEGYEZO
        // elrendezesu struct kell a _tileDescriptors bufferhez.
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private readonly struct TileDescriptor
        {
            public readonly int Face, Level, U, V;
            public TileDescriptor(int face, int level, int u, int v)
            {
                Face = face; Level = level; U = u; V = v;
            }
        }

        private readonly ComputeShader _shader;
        private readonly int _kernel;

        public GpuTerrainGeometryGenerator(ComputeShader shader)
        {
            _shader = shader != null ? shader : throw new ArgumentNullException(nameof(shader));
            _kernel = shader.FindKernel("CSGenerateTerrainGeometry");
        }

        /// <summary>
        /// A `tiles` sorrendjével megegyező eredménytömböt ad vissza (egy
        /// GpuQuadResult / tile). Minden bemeneti adat a hívó felelőssége -
        /// nincs belső gyorsítótárazás.
        /// </summary>
        public GpuQuadResult[] GenerateGeometry(
            IReadOnlyList<(int Face, int Level, uint U, uint V)> tiles,
            ulong worldSeed,
            (double X, double Y, double Z)[] plateSeeds,
            bool[] plateIsOceanic,
            IReadOnlyList<ImpactCratering.CraterRecord> craters,
            double seaLevel,
            double dayT,
            double orbitalPeriodDays,
            double rotationPeriodDays,
            double axialTiltRad,
            double radius,
            double elevationScale)
        {
            int tileCount = tiles.Count;
            if (tileCount == 0)
                return Array.Empty<GpuQuadResult>();

            var descriptors = new TileDescriptor[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                (int face, int level, uint u, uint v) = tiles[i];
                descriptors[i] = new TileDescriptor(face, level, (int)u, (int)v);
            }

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

            using ComputeBuffer descBuf = new ComputeBuffer(tileCount, sizeof(int) * 4);
            descBuf.SetData(descriptors);

            using ComputeBuffer plateSeedBuf = new ComputeBuffer(Math.Max(1, plateCount), sizeof(float) * 3);
            if (plateCount > 0) plateSeedBuf.SetData(plateSeedsF);

            using ComputeBuffer plateOceanicBuf = new ComputeBuffer(Math.Max(1, plateCount), sizeof(uint));
            if (plateCount > 0) plateOceanicBuf.SetData(plateOceanicU);

            using ComputeBuffer craterBuf = new ComputeBuffer(Math.Max(1, craterCount), sizeof(float) * 4);
            if (craterCount > 0) craterBuf.SetData(craterPosCos);

            using ComputeBuffer craterDepthBuf = new ComputeBuffer(Math.Max(1, craterCount), sizeof(float));
            if (craterCount > 0) craterDepthBuf.SetData(craterDepths);

            int vertCount = tileCount * 4;
            using ComputeBuffer outVertices = new ComputeBuffer(vertCount, sizeof(float) * 3);
            using ComputeBuffer outNormals = new ComputeBuffer(vertCount, sizeof(float) * 3);
            using ComputeBuffer outColors = new ComputeBuffer(vertCount, sizeof(float) * 4);
            using ComputeBuffer outCenterElevation = new ComputeBuffer(tileCount, sizeof(float));
            using ComputeBuffer outCenterIsOceanic = new ComputeBuffer(tileCount, sizeof(uint));
            using ComputeBuffer outCenterBiome = new ComputeBuffer(tileCount, sizeof(uint));

            _shader.SetBuffer(_kernel, "_tileDescriptors", descBuf);
            _shader.SetBuffer(_kernel, "_plateSeeds", plateSeedBuf);
            _shader.SetBuffer(_kernel, "_plateIsOceanic", plateOceanicBuf);
            _shader.SetBuffer(_kernel, "_craters", craterBuf);
            _shader.SetBuffer(_kernel, "_craterDepths", craterDepthBuf);
            _shader.SetBuffer(_kernel, "_outVertices", outVertices);
            _shader.SetBuffer(_kernel, "_outNormals", outNormals);
            _shader.SetBuffer(_kernel, "_outColors", outColors);
            _shader.SetBuffer(_kernel, "_outCenterElevation", outCenterElevation);
            _shader.SetBuffer(_kernel, "_outCenterIsOceanic", outCenterIsOceanic);
            _shader.SetBuffer(_kernel, "_outCenterBiome", outCenterBiome);

            int seedLo = unchecked((int)(uint)(worldSeed & 0xFFFFFFFFUL));
            int seedHi = unchecked((int)(uint)(worldSeed >> 32));
            _shader.SetInts("_worldSeed", seedLo, seedHi);
            _shader.SetInt("_plateCount", plateCount);
            _shader.SetInt("_craterCount", craterCount);
            _shader.SetInt("_geomTileCount", tileCount);
            _shader.SetFloat("_seaLevel", (float)seaLevel);
            _shader.SetFloat("_climateDayT", (float)dayT);
            _shader.SetFloat("_orbitalPeriodDays", (float)orbitalPeriodDays);
            _shader.SetFloat("_rotationPeriodDays", (float)rotationPeriodDays);
            _shader.SetFloat("_axialTiltRad", (float)axialTiltRad);
            _shader.SetFloat("_radius", (float)radius);
            _shader.SetFloat("_elevationScale", (float)elevationScale);

            // Ugyanaz a D3D 65535-os thread-group-korlat, mint CSClassifyTiles-nel
            // (ld. ott a doksit) - 2D racsra bontva.
            int totalGroups = Mathf.Max(1, Mathf.CeilToInt(tileCount / 64f));
            int groupsX = Mathf.Min(totalGroups, 65535);
            int groupsY = Mathf.CeilToInt((float)totalGroups / groupsX);
            _shader.SetInt("_geomDispatchGroupsX", groupsX);
            _shader.Dispatch(_kernel, groupsX, groupsY, 1);

            var vertData = new Vector3[vertCount];
            var normData = new Vector3[vertCount];
            var colorData = new Vector4[vertCount];
            var centerElevationData = new float[tileCount];
            var centerIsOceanicData = new uint[tileCount];
            var centerBiomeData = new uint[tileCount];
            outVertices.GetData(vertData);
            outNormals.GetData(normData);
            outColors.GetData(colorData);
            outCenterElevation.GetData(centerElevationData);
            outCenterIsOceanic.GetData(centerIsOceanicData);
            outCenterBiome.GetData(centerBiomeData);

            var results = new GpuQuadResult[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                int b = i * 4;
                Vector4 c00 = colorData[b + 0], c10 = colorData[b + 1], c11 = colorData[b + 2], c01 = colorData[b + 3];
                results[i] = new GpuQuadResult
                {
                    P00 = vertData[b + 0], P10 = vertData[b + 1], P11 = vertData[b + 2], P01 = vertData[b + 3],
                    Normal = normData[b + 0],
                    C00 = new Color(c00.x, c00.y, c00.z, c00.w),
                    C10 = new Color(c10.x, c10.y, c10.z, c10.w),
                    C11 = new Color(c11.x, c11.y, c11.z, c11.w),
                    C01 = new Color(c01.x, c01.y, c01.z, c01.w),
                    CenterElevation = centerElevationData[i],
                    CenterIsOceanic = centerIsOceanicData[i] != 0,
                    CenterBiome = (byte)centerBiomeData[i],
                };
            }
            return results;
        }
    }
}
