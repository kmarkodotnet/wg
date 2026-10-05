using System;
using System.Collections.Generic;

namespace WorldGen.Viewer.Lod
{
    /// <summary>ND-85: időkeretes, kizárólag előkészítő munkasor; a publikálás külön kapu.</summary>
    public sealed class LodUploadBatch<T>
    {
        private readonly T[] _items;
        private bool _closed;
        public int CompletedCount { get; private set; }
        public int Count => _items.Length;
        public bool ReadyToCommit => !_closed && CompletedCount == Count;

        public LodUploadBatch(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            _items = new List<T>(items).ToArray();
        }

        /// <summary>
        /// Egy keret-szelet elvégzése: a <paramref name="budgetMs"/> a FŐ SZÁL
        /// CPU-idejét, a <paramref name="maxItems"/> a tételszámot fékezi.
        /// Mindkét kapu CSAK tételek KÖZÖTT zár, és legalább egy tétel minden
        /// szeletben lefut, tehát a tranzakció garantáltan haladik.
        ///
        /// ND-190 MÉRÉS: megmértem egy GPU-oldali (vertex-alapú) harmadik kaput
        /// is, mert a hitch-frame-ekben a fő szál CPU-ideje csak 7 ms, a render
        /// szál viszont 96-134 ms-ot tölt GPU-várakozásban. Négy, egyenként
        /// 85 s-os kamerakörön a hatás ELLENTMONDÓ volt (>100 ms-os frame:
        /// 59 → 26, de 14 → 24), tehát a kameraút-szórás nagyobb, mint a
        /// beavatkozás: a kaput NEM tartjuk fenn. A szeletenkénti vertex-szám
        /// mérése (`sliceVertices`) megmaradt a PerfLogban.
        /// </summary>
        public int StageSlice(Action<T> stage, Func<double> elapsedMilliseconds, double budgetMs, int maxItems)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));
            if (elapsedMilliseconds == null) throw new ArgumentNullException(nameof(elapsedMilliseconds));
            if (!(budgetMs > 0) || double.IsInfinity(budgetMs)) throw new ArgumentOutOfRangeException(nameof(budgetMs));
            if (maxItems < 1) throw new ArgumentOutOfRangeException(nameof(maxItems));
            if (_closed) throw new InvalidOperationException("Lezárt upload-tranzakció.");
            int done = 0;
            try
            {
                double start = elapsedMilliseconds();
                while (!_closed && CompletedCount < Count && done < maxItems)
                {
                    if (done > 0 && elapsedMilliseconds() - start >= budgetMs) break;
                    stage(_items[CompletedCount]);
                    CompletedCount++;
                    done++;
                }
            }
            catch { _closed = true; throw; }
            return done;
        }

        public void Commit(Action publish)
        {
            if (publish == null) throw new ArgumentNullException(nameof(publish));
            if (!ReadyToCommit) throw new InvalidOperationException("A teljes upload előtt nem publikálható fedés.");
            _closed = true; // A hibás/újrahívott commit sem futhat le még egyszer.
            publish();
        }

        public void Cancel() { _closed = true; }
    }
}
