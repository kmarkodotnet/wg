using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>ND-93: csak használaton kívüli kulcsok, legrégebbi először; O(1) műveletek.</summary>
    public sealed class InactiveChunkQueue
    {
        private readonly LinkedList<TileId> _order = new LinkedList<TileId>();
        private readonly Dictionary<TileId, LinkedListNode<TileId>> _entries =
            new Dictionary<TileId, LinkedListNode<TileId>>();
        public int Count => _entries.Count;

        public void MarkInactive(TileId key)
        {
            if (!_entries.ContainsKey(key)) _entries.Add(key, _order.AddLast(key));
        }

        public void Remove(TileId key)
        {
            if (!_entries.TryGetValue(key, out var node)) return;
            _order.Remove(node);
            _entries.Remove(key);
        }

        public bool TryTakeExcess(int retainedLimit, out TileId key)
        {
            if (retainedLimit < 0) throw new ArgumentOutOfRangeException(nameof(retainedLimit));
            if (Count <= retainedLimit) { key = default; return false; }
            key = _order.First!.Value;
            Remove(key);
            return true;
        }

        public void Clear() { _order.Clear(); _entries.Clear(); }
    }
}
