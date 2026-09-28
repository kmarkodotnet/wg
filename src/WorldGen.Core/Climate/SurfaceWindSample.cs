namespace WorldGen.Core.Climate
{
    /// <summary>
    /// Egy pont felszíni szele: ÉRINTŐSÍKBELI 3D irány és a hozzá tartozó
    /// sebesség (m/s). Ez az az alak, amit a nedvesség-transzport
    /// (<see cref="MoisturePrecipitation"/>) vár: az irány a szomszédok felé
    /// menő kifolyás-súlyokhoz, a sebesség a párolgáshoz kell.
    ///
    /// Értéktípus, nincs benne állapot: a hívó EGYSZER állítja elő a mezőt és
    /// változatlanul adja tovább — így a transzport tiszta függvény marad (I2).
    ///
    /// A <see cref="SpeedMs"/> KÜLÖN mező, nem az (X,Y,Z) hosszából számolódik:
    /// a hőmodell a sebességet a helyi kelet/észak komponensekből képzi, és ezt
    /// a két utat nem akarjuk csendben összemosni egy gyökvonással (ND-163).
    /// </summary>
    public readonly struct SurfaceWindSample
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;
        public readonly double SpeedMs;

        public SurfaceWindSample(double x, double y, double z, double speedMs)
        {
            X = x;
            Y = y;
            Z = z;
            SpeedMs = speedMs;
        }
    }
}
