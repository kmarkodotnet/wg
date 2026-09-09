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

        /// <summary>
        /// Az ÁTLÓS szomszéd (pl. jobb-felső) EGYETLEN geometriai lépésben,
        /// az `id` SAJÁT (uc,vc) érintő-koordinátájából - NEM két egymást
        /// követő <see cref="Neighbor"/>-hívással (`Neighbor(Neighbor(id,h),v)`).
        ///
        /// MÉRÉSSEL FELTÁRT OK, MIÉRT KELLETT EZ A VÁLTOZAT (2026-09-06): a
        /// két-lépéses módszer a KÖZTES tile SAJÁT (uc,vc) keretében
        /// értelmezi a második lépést - ha a köztes tile egy MÁSIK,
        /// ELTÉRŐEN ORIENTÁLT kocka-lapon van (mert az első lépés átlépte a
        /// lap-határt), a "felfelé" ott NEM feltétlenül ugyanaz a térbeli
        /// irány. Mérve egy level-5 rácson: `Neighbor(Neighbor(t,Right),Up)`
        /// vs `Neighbor(Neighbor(t,Up),Right)` a tile-ok 3.12%-ánál (a
        /// lap-SZÉLEN lévő összes tile) KÜLÖNBÖZIK.
        ///
        /// DE EZ A VÁLTOZAT SEM TÖKÉLETES - EZT IS MÉRÉSSEL ELLENŐRIZTÜK, ÉS
        /// EZÉRT NEM HASZNÁLJUK: egy oda-vissza kör-teszttel
        /// (`DiagonalNeighbor(DiagonalNeighbor(t,h,v), Opp(h), Opp(v)) == t`)
        /// a kocka ÉLEI/CSÚCSAI közelében (ahol az átlós lépés MAGA is
        /// átlépi a lap-határt, és az új lapon a visszafelé átlós lépés MÉG
        /// EGY határt átléphet) a kör NEM zárul: 24576 esetből 768 (3.125%)
        /// nem tér vissza az eredeti tile-ra. Ez azt jelzi, hogy az "átlós
        /// szomszéd" fogalma MATEMATIKAILAG SEM egyértelműen definiált a
        /// kocka éle/csúcsa közelében - nincs olyan egyszerű képlet, ami
        /// mindig a "helyes" negyedik sarok-tile-t adná vissza. A hívó
        /// (`PlanetGridMesh.PrecipAndOceanFractionAtCorner`) ezért NEM ezt a
        /// metódust használja, hanem lap-határ-átlépésnél EGYSZERŰEN
        /// KIHAGYJA a bizonytalan negyedik (átlós) mintát a sarok-átlagból,
        /// ahelyett hogy egy pontatlan értéket erőltetne bele. A metódus
        /// ITT MARAD dokumentálva (ne próbálja újra megírni valaki ugyanígy,
        /// ugyanezzel a hibával), de jelenleg nincs hívója.
        /// </summary>
        public static TileId DiagonalNeighbor(TileId id, TileDirection horizontal, TileDirection vertical)
        {
            id.GetUV(out uint u, out uint v);
            int level = id.Level;
            long n = 1L << level;
            double step = 2.0 / n;

            double uc = (u + 0.5) / n * 2.0 - 1.0;
            double vc = (v + 0.5) / n * 2.0 - 1.0;
            uc += horizontal == TileDirection.Right ? step : -step;
            vc += vertical == TileDirection.Up ? step : -step;

            TileGeometry.PositionFromFaceUV(id.Face, uc, vc, out double x, out double y, out double z);
            return TileGeometry.FromPosition(x, y, z, level);
        }
    }
}
