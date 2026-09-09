using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// M9/ND-47 "az igazi fix": a dinamikus (adaptiv) reteg jelenleg MINDEN
    /// cut-valtaskor a TELJES geometriat ujraepiti es feltolti, meg akkor is,
    /// ha a cutnak csak egy kis resze valtozott a kamera mozgasa miatt - ez a
    /// per-frame fo-szalu mesh-feltoltes koltseget (Mesh.SetVertices/
    /// SetTriangles) a TELJES cut meretevel teszi aranyossa, nem a
    /// TENYLEGESEN valtozott resszel. Emiatt a budgetet (adaptiveRenderBudget)
    /// nem lehet tovabb emelni anelkul, hogy minden kameramozgas
    /// akadna - ez az oka annak, hogy a felszin reszletessege plafont
    /// er el meg mielott a felhasznalo altal elvart finomsagot elerne.
    ///
    /// EZ A MODUL a cut-ot RÖGZÍTETT MERETU "chunk"-okra bontja (minden
    /// chunk egy `chunkLevel`-szintu tile leszarmazottjainak halmaza a
    /// cutban), es KISZAMOLJA, mely chunk-ok VALTOZTAK az elozo kerethez
    /// kepest. A hivo (PlanetGridMesh) ebbol mar csak a VALTOZOTT chunk-okra
    /// epiti/tolti fel ujra a geometriat - a valtozatlan chunk-ok MEGLEVO
    /// Unity mesh-objektumai erintetlenul maradnak. Igy a per-frame koltseg
    /// a VALTOZAS meretevel aranyos, nem a teljes lathato cut meretevel.
    ///
    /// TISZTA, MOTORFUGGETLEN logika (mint az AdaptiveQuadTree) - nincs
    /// UnityEngine-referencia, `dotnet test`-tel kozvetlenul tesztelheto.
    /// </summary>
    public static class DynamicMeshChunking
    {
        /// <summary>
        /// Egy cut-beli levelet a `chunkLevel`-szintu OSTOL (ez a "chunk gyoker")
        /// csoportosit. Ha a level MAR &lt;= chunkLevel (nem vart eset a normal
        /// hasznalatban, mert a cut levelei mindig a staticBaseLevel FOLOTT
        /// vannak, es chunkLevel &lt;= staticBaseLevel), a level ONMAGA a sajat
        /// chunk-gyokere (nem lehet nala durvabb chunk-ot kepezni).
        /// </summary>
        public static TileId ChunkRootOf(TileId leaf, int chunkLevel)
        {
            TileId current = leaf;
            while (current.Level > chunkLevel)
                current = current.Parent();
            return current;
        }

        /// <summary>
        /// A teljes cut csoportositasa chunk-gyoker szerint. A visszaadott
        /// Dictionary kulcsai PONTOSAN azok a chunk-gyokerek, amiknek legalabb
        /// egy levele van a cutban (üres chunk nem szerepel).
        /// </summary>
        public static Dictionary<TileId, HashSet<TileId>> GroupByChunk(IEnumerable<TileId> cut, int chunkLevel)
        {
            var chunks = new Dictionary<TileId, HashSet<TileId>>();
            foreach (TileId leaf in cut)
            {
                TileId root = ChunkRootOf(leaf, chunkLevel);
                if (!chunks.TryGetValue(root, out HashSet<TileId>? leaves))
                {
                    leaves = new HashSet<TileId>();
                    chunks[root] = leaves;
                }
                leaves.Add(leaf);
            }
            return chunks;
        }

        /// <summary>
        /// Egy chunk-diff eredmenye: mely chunk-gyokereket kell UJRAEPITENI
        /// (mert ujak, vagy a levelhalmazuk valtozott), es melyeket TOROLNI
        /// (mert korabban volt tartalmuk, most nincs - a hozzajuk tartozo
        /// Unity mesh-objektumot ki kell uriteni/deaktivalni). A fel NEM
        /// sorolt (regi cutban is, ujban is jelen levo, VALTOZATLAN
        /// levelhalmazu) chunk-ok gyorsitotara: a hivo semmit nem csinal
        /// veluk.
        /// </summary>
        public readonly struct ChunkDiff
        {
            public readonly List<TileId> ChangedOrNewChunks;
            public readonly List<TileId> RemovedChunks;
            public readonly int UnchangedChunkCount;

            public ChunkDiff(List<TileId> changedOrNewChunks, List<TileId> removedChunks, int unchangedChunkCount)
            {
                ChangedOrNewChunks = changedOrNewChunks;
                RemovedChunks = removedChunks;
                UnchangedChunkCount = unchangedChunkCount;
            }
        }

        /// <summary>
        /// Ket egymast koveto keret chunk-csoportositasat hasonlitja ossze.
        /// Egy chunk "valtozott", ha UJ (nem volt korabban), vagy a
        /// levelhalmaza (HashSet-egyenloseg, sorrendfuggetlen) eltero a
        /// korabbitol. Egy chunk "torolt", ha korabban volt, de az uj
        /// csoportositasban mar nincs jelen (a cut ATT a resze mar durvabb/
        /// masik chunkba tartozik, vagy a kamera elfordult).
        /// </summary>
        public static ChunkDiff DiffChunks(
            IReadOnlyDictionary<TileId, HashSet<TileId>> previousChunks,
            IReadOnlyDictionary<TileId, HashSet<TileId>> newChunks)
        {
            var changedOrNew = new List<TileId>();
            int unchanged = 0;

            foreach (KeyValuePair<TileId, HashSet<TileId>> kv in newChunks)
            {
                if (previousChunks.TryGetValue(kv.Key, out HashSet<TileId>? previousLeaves)
                    && previousLeaves.SetEquals(kv.Value))
                {
                    unchanged++;
                }
                else
                {
                    changedOrNew.Add(kv.Key);
                }
            }

            var removed = new List<TileId>();
            foreach (TileId oldRoot in previousChunks.Keys)
            {
                if (!newChunks.ContainsKey(oldRoot))
                    removed.Add(oldRoot);
            }

            return new ChunkDiff(changedOrNew, removed, unchanged);
        }
    }
}
