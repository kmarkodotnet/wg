namespace WorldGen.Core.Grid
{
    /// <summary>4-szomszédos irány a lap-lokális (u,v) rácson.</summary>
    public enum TileDirection
    {
        Right = 0, // u+1
        Left = 1,  // u-1
        Up = 2,    // v+1
        Down = 3,  // v-1
    }

    /// <summary>
    /// Szomszédkeresés a cubed sphere rácson (docs/05-milestones.md §2.4).
    ///
    /// A módszer NEM kézzel írt, 24 esetes él-táblázat (lap × irány) - azt
    /// könnyű elrontani, pont ez a dokumentált fő hibaforrás. Ehelyett a már
    /// verifikált <see cref="TileGeometry"/> vetítést használjuk: a tile
    /// közepétől egy tile-nyit elmozdulunk a folytonos (uc,vc) térben (akár
    /// a [-1,1] tartományon túlra), majd a kapott 3D pontot a domináns-
    /// tengely-detektálással (<see cref="TileGeometry.FromPosition"/>)
    /// visszavetítjük - ez automatikusan a szomszédos lapra "landol", ha a
    /// lépés átlépte a lap élét. Kimerítően ellenőrizve
    /// (tools/reference/neighbor_ref.py): level 3/4/5-ön minden tile-nak
    /// pontosan 4 szomszédja van (a 24 lap-sarok-tile sem kivétel a
    /// 4-szomszédos él-adjacencia mellett - ld. docs/04-decisions.md), és a
    /// szomszédság szimmetrikus kivétel nélkül.
    /// </summary>
    public static class TileNeighbors
    {
        public static TileId Neighbor(TileId id, TileDirection direction)
        {
            id.GetUV(out uint u, out uint v);
            int level = id.Level;
            long n = 1L << level;
            double step = 2.0 / n;

            double uc = (u + 0.5) / n * 2.0 - 1.0;
            double vc = (v + 0.5) / n * 2.0 - 1.0;

            switch (direction)
            {
                case TileDirection.Right: uc += step; break;
                case TileDirection.Left: uc -= step; break;
                case TileDirection.Up: vc += step; break;
                case TileDirection.Down: vc -= step; break;
            }

            TileGeometry.PositionFromFaceUV(id.Face, uc, vc, out double x, out double y, out double z);
            return TileGeometry.FromPosition(x, y, z, level);
        }

        /// <summary>Mind a 4 szomszéd. A sarok-tile-oknál is 4 elemű (nem 3) - ld. osztály-doc.</summary>
        public static void GetAll(TileId id, out TileId right, out TileId left, out TileId up, out TileId down)
        {
            right = Neighbor(id, TileDirection.Right);
            left = Neighbor(id, TileDirection.Left);
            up = Neighbor(id, TileDirection.Up);
            down = Neighbor(id, TileDirection.Down);
        }
    }
}
