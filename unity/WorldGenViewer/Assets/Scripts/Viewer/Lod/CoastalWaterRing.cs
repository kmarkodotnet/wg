using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>ND-149: egy parti gyűrű-jelölt base-tile sorsa.</summary>
    public enum CoastalRingTileState
    {
        /// <summary>Mind a négy sarok a vízszint FÖLÖTT - a terep úgyis eltakarná a vízlapot.</summary>
        Skipped = 1,
        /// <summary>Vegyes sarkok: a valódi partvonal ezen a tile-on BELÜL fut.</summary>
        Coast = 2,
        /// <summary>Mind a négy sarok a vízszint ALATT - a tile teljesen elöntött.</summary>
        Submerged = 3,
    }

    /// <summary>
    /// ND-149 (A18): a parti víz-gyűrű TISZTA döntései. A vízfelszín
    /// TÚLNYÚLIK a valódi parton, és a már renderelt terep vágja ki belőle a
    /// partvonalat - így a part nem a level-8 tile-él, hanem a terep
    /// tengerszint-kontúrja.
    ///
    /// Ugyanaz a minta, mint az <see cref="OceanRefinement"/>-nél: a döntés
    /// tiszta függvény (csak a négy sarok víz-alattisága a bemenet), hogy
    /// Unity Editor NÉLKÜL, közvetlenül tesztelhető legyen. A sarok-elevációk
    /// beolvasása és a bejárás a hívóé (<c>PlanetGridMesh</c>).
    /// </summary>
    public static class CoastalWaterRing
    {
        /// <summary>
        /// A gyűrű-jelölt tile besorolása a négy sarkának víz-alattiságából.
        /// A sarkok sorrendje a quad-emisszióé: 00, 10, 11, 01.
        /// </summary>
        public static CoastalRingTileState Classify(bool below00, bool below10, bool below11, bool below01)
        {
            if (!below00 && !below10 && !below11 && !below01) return CoastalRingTileState.Skipped;
            return below00 && below10 && below11 && below01
                ? CoastalRingTileState.Submerged
                : CoastalRingTileState.Coast;
        }

        /// <summary>
        /// A tile <paramref name="direction"/> irányú ÉLÉNEK mindkét közös
        /// sarka a vízszint alatt van-e. Ez a NYERS vízperem pontos feltétele:
        /// ha igen, a terep sehol nem emelkedik az élen a vízlap fölé, tehát
        /// nem tudja kivágni a partot - a gyűrűnek ezen az élen TOVÁBB kell
        /// terjednie.
        ///
        /// A tile-alapú („csak a teljesen elöntött tile-ok körül terjedj")
        /// szabály ennél gyengébb: élő mérésben 12 nyers élt hagyott, az
        /// élenkénti szabály nullát (ld. ND-149).
        /// </summary>
        public static bool EdgeFullyBelowWater(
            TileDirection direction, bool below00, bool below10, bool below11, bool below01)
        {
            switch (direction)
            {
                case TileDirection.Right: return below10 && below11; // u+1 oldal
                case TileDirection.Left: return below00 && below01;  // u oldal
                case TileDirection.Up: return below01 && below11;    // v+1 oldal
                default: return below00 && below10;                  // Down: v oldal
            }
        }
    }
}
