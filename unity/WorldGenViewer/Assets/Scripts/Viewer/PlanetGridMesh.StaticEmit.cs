using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer
{
    public partial class PlanetGridMesh
    {
        // ====================================================================
        // ND-123: a statikus alapreteg emitje PARHUZAMOSAN (todo.md 1. tabla
        // 8. sor). Elesben MERVE: `emit=583,9-687,5 ms` a
        // BuildStaticBaseLayer 1358-1685 ms-abol - a legnagyobb egyetlen tetel.
        //
        // MIERT NEM EGY HOTSPOT-JAVITAS. A feladat "direct-array emit"-et
        // javasolt, es a mereseim megmutattak, MIERT az a helyes irany:
        // 393 216 tile / 584 ms = 1,48 us/tile, de a gyanusitott vizfelszin-
        // geometria (GetContinuousBounds + 4x PositionFromFaceUV) offline
        // merve csak 104 ms, a 65%-os oceani aranyra vetitve ~68 ms - a
        // koltseg ~11%-a. Vagyis NINCS egyetlen hotspot: a maradek ~500 ms
        // sok apro muvelet (nagysagrendileg 12 millio List.Add es ~20 millio
        // NaN-ellenorzes), amit CSAK parhuzamositassal lehet erdemben
        // csokkenteni.
        //
        // HOGYAN MARAD A KIMENET AZONOS. Az emit HAROM helyre ir: a terep-
        // slot listaiba, a viz-bucket listaiba (+ WaterTiles), es a
        // hatarvonal-listakba. A particionalas ezt hasznalja ki:
        //
        //   1. passz  - PARHUZAMOS a terep-SLOTOK szerint, `emitWater: false`;
        //   2. passz  - PARHUZAMOS a viz-BUCKETEK szerint, `emitTerrain: false`.
        //
        // Igy minden szal KIZAROLAGOS listakra ir (nincs zar, nincs verseny),
        // es egy bucketen belul a tile-ok sorrendje VALTOZATLANUL tile-index
        // szerint novekvo - pontosan az, amit az egyszalu ciklus adott. A
        // haromszog-indexek `vertices.Count`-hoz relativak, ami bucketenkent
        // fut, tehat szinten valtozatlan.
        //
        // UGYANAZ A KOD FUT: nincs duplikalt quad-szamitas. Az
        // `EmitAdaptiveTile` ket uj zaszlot kapott (`emitTerrain`/`emitWater`),
        // semmi mast - igy a ket ut nem tud egymastol elcsuszni.
        //
        // AMIT NEM FED: `showBorders` eseten a hatarvonal-listak MEGOSZTOTTAK
        // minden tile kozott, tehat a hivo ilyenkor a regi, egyszalu ciklusra
        // esik vissza (a jelenetben `showBorders: 0`). A hatarvonal
        // particionalasa kulon lepes lenne, es nincs mogotte mert nyereseg.
        //
        // KORLAT, KIMONDVA: a parhuzamossagot a LEGNAGYOBB bucket hatarolja.
        // Az oceani terep-slot a tile-ok tobbsegét birtokolja, tehat a
        // varhato gyorsulas nem a magszam, hanem nagysagrendileg 2-3x. A
        // tovabblepes (bucketen BELULI, prefix-offsetes darabolas rendezett
        // osszefuzessel) mar index-aritmetikat igenyel a haromszogeken, ezert
        // KULON, meressel alatamasztott lepes - nem csusztatom bele ebbe.
        // ====================================================================

        [SerializeField]
        [Tooltip("ND-123 diagnosztika: a párhuzamos statikus emit MELLETT lefuttatja a " +
                 "régi, egyszálú emitet is, és elemenként összehasonlítja a két eredményt. " +
                 "Bekapcsolva a Build lassabb (kétszer emitel), viszont bizonyítja, hogy a " +
                 "párhuzamos út BITRE ugyanazt adja. Az első élő futás után kikapcsolható.")]
        private bool verifyParallelStaticEmit = true;

        private void EmitStaticBaseLayerInParallel(
            TileId[] leaves, StaticMeshBuckets buckets,
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> verticesByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<Vector3>> normalsByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<int>> trianglesByKey,
            Dictionary<(RenderCategory Category, int Bucket), List<Color>> colorsByKey,
            Dictionary<int, List<Vector3>> waterVerticesByBucket,
            Dictionary<int, List<Vector3>> waterNormalsByBucket,
            Dictionary<int, List<int>> waterTrianglesByBucket,
            Dictionary<int, List<Color>> waterColorsByBucket,
            List<Vector3> borderVerts, List<int> borderIndices, float waterSurfaceRadius)
        {
            // A tile-indexek particionalasa EGY szekvencialis passzban. A
            // darabszamok mar megvannak (CreateStaticMeshBuckets ugyanezt
            // szamolta a listak elorefoglalasahoz), ezert pontos meretu
            // tombokbe gyujtunk, novekvo tile-index szerint.
            int terrainSlotCount = buckets.TerrainVertices.Length;
            int waterBucketCount = buckets.WaterVertices.Length;
            var terrainCounts = new int[terrainSlotCount];
            var waterCounts = new int[waterBucketCount];
            var terrainSlotOf = new int[leaves.Length];
            var waterBucketOf = new int[leaves.Length];

            for (int i = 0; i < leaves.Length; i++)
            {
                AdaptiveTileClassification classification = _staticTileClassifications[i];
                int slot = StaticTerrainBucketIndex(classification.Category, classification.Bucket);
                terrainSlotOf[i] = slot;
                terrainCounts[slot]++;

                if (classification.IsOceanic
                    && (classification.Biome == Biome.Ocean || classification.Biome == Biome.SeaIce))
                {
                    int waterBucket = WaterDepthBucket(_adaptiveSeaLevel - classification.Elevation);
                    waterBucketOf[i] = waterBucket;
                    waterCounts[waterBucket]++;
                }
                else
                {
                    waterBucketOf[i] = -1;
                }
            }

            var terrainTiles = new int[terrainSlotCount][];
            for (int slot = 0; slot < terrainSlotCount; slot++)
                terrainTiles[slot] = new int[terrainCounts[slot]];
            var waterTiles = new int[waterBucketCount][];
            for (int bucket = 0; bucket < waterBucketCount; bucket++)
                waterTiles[bucket] = new int[waterCounts[bucket]];

            var terrainCursor = new int[terrainSlotCount];
            var waterCursor = new int[waterBucketCount];
            for (int i = 0; i < leaves.Length; i++)
            {
                int slot = terrainSlotOf[i];
                terrainTiles[slot][terrainCursor[slot]++] = i;
                int waterBucket = waterBucketOf[i];
                if (waterBucket >= 0)
                    waterTiles[waterBucket][waterCursor[waterBucket]++] = i;
            }

            // 1. PASSZ: terep-quadok, terep-slot szerint particionalva.
            Parallel.For(0, terrainSlotCount, slot =>
            {
                int[] tiles = terrainTiles[slot];
                for (int k = 0; k < tiles.Length; k++)
                {
                    int i = tiles[k];
                    EmitAdaptiveTile(
                        leaves[i], verticesByKey, normalsByKey, trianglesByKey, colorsByKey,
                        waterVerticesByBucket, waterNormalsByBucket, waterTrianglesByBucket, waterColorsByBucket,
                        borderVerts, borderIndices, waterSurfaceRadius, radialBias: 0f,
                        staticDenseIndex: i, staticBuckets: buckets,
                        emitTerrain: true, emitWater: false);
                }
            });

            // 2. PASSZ: viz-quadok, viz-bucket szerint particionalva.
            Parallel.For(0, waterBucketCount, bucket =>
            {
                int[] tiles = waterTiles[bucket];
                for (int k = 0; k < tiles.Length; k++)
                {
                    int i = tiles[k];
                    EmitAdaptiveTile(
                        leaves[i], verticesByKey, normalsByKey, trianglesByKey, colorsByKey,
                        waterVerticesByBucket, waterNormalsByBucket, waterTrianglesByBucket, waterColorsByBucket,
                        borderVerts, borderIndices, waterSurfaceRadius, radialBias: 0f,
                        staticDenseIndex: i, staticBuckets: buckets,
                        emitTerrain: false, emitWater: true);
                }
            });

            if (verifyParallelStaticEmit)
                VerifyParallelStaticEmit(leaves, buckets, waterSurfaceRadius);
        }

        /// <summary>
        /// A párhuzamos emit eredményét összehasonlítja a régi, egyszálú
        /// emittel - elemenként. A `verifyParallelStaticEmit` mező kapcsolja.
        ///
        /// MIÉRT VAN EZ. A párhuzamos út a fenti érvelés szerint BITRE ugyanazt
        /// adja, de a kimenete a látható bolygófelszín, és a hiba (elcsúszott
        /// háromszög-index, felcserélt sorrend) NEM dobna kivételt - csak
        /// csendben elrontaná a hálót. Ez a kapcsoló ezért alapból BE van,
        /// hogy az első élő futás BIZONYÍTSA az egyezést; utána kikapcsolható.
        /// </summary>
        private void VerifyParallelStaticEmit(
            TileId[] leaves, StaticMeshBuckets parallelBuckets, float waterSurfaceRadius)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            int terrainSlotCount = parallelBuckets.TerrainVertices.Length;
            int waterBucketCount = parallelBuckets.WaterVertices.Length;

            // Ugyanolyan, de FRISS bucket-halmaz az egyszalu utnak.
            var referenceVerticesByKey = new Dictionary<(RenderCategory Category, int Bucket), List<Vector3>>();
            var referenceNormalsByKey = new Dictionary<(RenderCategory Category, int Bucket), List<Vector3>>();
            var referenceTrianglesByKey = new Dictionary<(RenderCategory Category, int Bucket), List<int>>();
            var referenceColorsByKey = new Dictionary<(RenderCategory Category, int Bucket), List<Color>>();
            var referenceWaterVertices = new Dictionary<int, List<Vector3>>();
            var referenceWaterNormals = new Dictionary<int, List<Vector3>>();
            var referenceWaterTriangles = new Dictionary<int, List<int>>();
            var referenceWaterColors = new Dictionary<int, List<Color>>();
            StaticMeshBuckets reference = CreateStaticMeshBuckets(
                referenceVerticesByKey, referenceNormalsByKey, referenceTrianglesByKey, referenceColorsByKey,
                referenceWaterVertices, referenceWaterNormals, referenceWaterTriangles, referenceWaterColors);

            var throwaway = new List<Vector3>();
            var throwawayIndices = new List<int>();
            for (int i = 0; i < leaves.Length; i++)
            {
                EmitAdaptiveTile(
                    leaves[i], referenceVerticesByKey, referenceNormalsByKey,
                    referenceTrianglesByKey, referenceColorsByKey,
                    referenceWaterVertices, referenceWaterNormals, referenceWaterTriangles, referenceWaterColors,
                    throwaway, throwawayIndices, waterSurfaceRadius, radialBias: 0f,
                    staticDenseIndex: i, staticBuckets: reference);
            }

            string mismatch = null;
            for (int slot = 0; slot < terrainSlotCount && mismatch == null; slot++)
            {
                mismatch = FirstDifference($"terep-slot {slot} vertex",
                    reference.TerrainVertices[slot], parallelBuckets.TerrainVertices[slot]);
                mismatch ??= FirstDifference($"terep-slot {slot} normal",
                    reference.TerrainNormals[slot], parallelBuckets.TerrainNormals[slot]);
                mismatch ??= FirstDifference($"terep-slot {slot} szin",
                    reference.TerrainColors[slot], parallelBuckets.TerrainColors[slot]);
                mismatch ??= FirstDifference($"terep-slot {slot} haromszog",
                    reference.TerrainTriangles[slot], parallelBuckets.TerrainTriangles[slot]);
            }
            for (int bucket = 0; bucket < waterBucketCount && mismatch == null; bucket++)
            {
                mismatch = FirstDifference($"viz-bucket {bucket} vertex",
                    reference.WaterVertices[bucket], parallelBuckets.WaterVertices[bucket]);
                mismatch ??= FirstDifference($"viz-bucket {bucket} normal",
                    reference.WaterNormals[bucket], parallelBuckets.WaterNormals[bucket]);
                mismatch ??= FirstDifference($"viz-bucket {bucket} szin",
                    reference.WaterColors[bucket], parallelBuckets.WaterColors[bucket]);
                mismatch ??= FirstDifference($"viz-bucket {bucket} haromszog",
                    reference.WaterTriangles[bucket], parallelBuckets.WaterTriangles[bucket]);
                mismatch ??= FirstDifference($"viz-bucket {bucket} tile",
                    reference.WaterTiles[bucket], parallelBuckets.WaterTiles[bucket]);
            }

            timer.Stop();
            if (mismatch == null)
            {
                PerfLog($"[ND-123 emit verify] EGYEZIK (parhuzamos == egyszalu), "
                    + $"ellenorzes={timer.Elapsed.TotalMilliseconds:F0}ms - a "
                    + $"verifyParallelStaticEmit mezo kikapcsolhato.");
            }
            else
            {
                Debug.LogError($"PlanetGridMesh ND-123: a PARHUZAMOS statikus emit ELTER az "
                    + $"egyszalu referenciatol - {mismatch}. A parhuzamos utat ki kell kapcsolni, "
                    + $"amig ez nincs tisztazva.");
                PerfLog($"[ND-123 emit verify] ELTERES: {mismatch}");
            }
        }

        /// <summary>
        /// FONTOS FINOMSÁG: `IEquatable&lt;T&gt;.Equals` kell, NEM `==`. A Unity
        /// `Vector3.operator ==` EPSZILONOS (négyzetes távolság-küszöb),
        /// `Vector3.Equals` viszont PONTOS float-összehasonlítás. Ha valaki
        /// ezt `==`-re „egyszerűsíti", az ellenőrzés csendben elveszti az
        /// erejét: épp azokat a kicsi, de valódi eltéréseket engedné át,
        /// amiket keresünk.
        /// </summary>
        private static string FirstDifference<T>(string what, List<T> expected, List<T> actual)
            where T : IEquatable<T>
        {
            if (expected.Count != actual.Count)
                return $"{what}: elemszam {expected.Count} vs {actual.Count}";
            for (int i = 0; i < expected.Count; i++)
                if (!expected[i].Equals(actual[i]))
                    return $"{what}: a(z) {i}. elem {expected[i]} vs {actual[i]}";
            return null;
        }
    }
}
