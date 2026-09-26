namespace WorldGen.Core.Tectonics
{
    /// <summary>
    /// A22: a deep-time kiértékelés HÁRMAS kontextusa egyetlen paraméterben.
    ///
    /// Az ND-136 (A19) és az ND-137 (A20) két köre után ugyanez a három érték —
    /// lemez-idő, eróziós idő, statikus tengerszint — vonult végig öt Core-osztály
    /// és négy viewer-segédfüggvény teljes hívási láncán. A struct TISZTÁN
    /// KOZMETIKAI, BIT-SEMLEGES összefogás: nincs numerikus változás, nincs
    /// generátorverzió-emelés (ld. CLAUDE.md "Verziózás" — bitazonos refaktor).
    ///
    /// MIÉRT KÜLÖN a két idő? A viewer "Erózió (kopás)" kapcsolója a kopást a
    /// lemezmozgás megtartása mellett is kikapcsolhatja, ezért
    /// <see cref="ErosionTimeMyr"/> nem származtatható a
    /// <see cref="PlateTimeMyr"/>-ből (ND-137).
    ///
    /// MIÉRT PROPERTY a tengerszint? A parti abrázió (ND-137 2. kör) konvenciója
    /// szerint NaN = "kikapcsolva". Ha a mező nyers <c>double</c> lenne, a
    /// <c>default(DeepTimeContext)</c> 0,0 m tengerszintet adna, azaz CSENDBEN
    /// bekapcsolná az abráziót a 0 m-es szint körül. A <see cref="_hasStaticSeaLevel"/>
    /// zászlóval a nullérték helyesen NaN-t (kikapcsolt abráziót) jelent.
    /// </summary>
    public readonly struct DeepTimeContext
    {
        /// <summary>Lemez-idő (Myr) — a lemezmozgás és a lemez-keretes zaj kora.</summary>
        public readonly double PlateTimeMyr;

        /// <summary>Eróziós idő (Myr) — a relief-kopás, a bevágódás és az uplift-relaxáció kora. 0 = nincs kopás.</summary>
        public readonly double ErosionTimeMyr;

        private readonly bool _hasStaticSeaLevel;
        private readonly double _staticSeaLevel;

        public DeepTimeContext(double plateTimeMyr, double erosionTimeMyr, double staticSeaLevelMeters)
        {
            PlateTimeMyr = plateTimeMyr;
            ErosionTimeMyr = erosionTimeMyr;
            _hasStaticSeaLevel = true;
            _staticSeaLevel = staticSeaLevelMeters;
        }

        private DeepTimeContext(double plateTimeMyr, double erosionTimeMyr)
        {
            PlateTimeMyr = plateTimeMyr;
            ErosionTimeMyr = erosionTimeMyr;
            _hasStaticSeaLevel = false;
            _staticSeaLevel = 0.0;
        }

        /// <summary>
        /// A statikus (t = 0) tengerszint a parti abrázióhoz, vagy NaN, ha nincs
        /// megadva (= az abrázió kikapcsolva, ND-137 2. kör).
        /// </summary>
        public double StaticSeaLevelMeters => _hasStaticSeaLevel ? _staticSeaLevel : double.NaN;

        /// <summary>Statikus M4-világ: nincs lemezmozgás, nincs erózió, nincs abrázió. Bitre az ND-136 előtti út.</summary>
        public static DeepTimeContext Static => default;

        /// <summary>Csak lemezmozgás, kopás nélkül (a viewer "Erózió" kapcsolója kikapcsolva).</summary>
        public static DeepTimeContext AtPlateTime(double plateTimeMyr)
            => new DeepTimeContext(plateTimeMyr, 0.0);

        /// <summary>A lemez-idő és az eróziós idő megegyezik; a parti abrázió tengerszint nélkül kikapcsolt.</summary>
        public static DeepTimeContext Uniform(double timeMyr)
            => new DeepTimeContext(timeMyr, timeMyr);

        /// <summary>Ugyanez a kontextus a parti abrázióhoz megadott statikus tengerszinttel.</summary>
        public DeepTimeContext WithStaticSeaLevel(double staticSeaLevelMeters)
            => new DeepTimeContext(PlateTimeMyr, ErosionTimeMyr, staticSeaLevelMeters);

        /// <summary>Ugyanez a kontextus más eróziós idővel.</summary>
        public DeepTimeContext WithErosionTime(double erosionTimeMyr)
            => _hasStaticSeaLevel
                ? new DeepTimeContext(PlateTimeMyr, erosionTimeMyr, _staticSeaLevel)
                : new DeepTimeContext(PlateTimeMyr, erosionTimeMyr);

        /// <summary>Se lemezmozgás, se kopás — a gyors, cache-elt (t = 0) úton mehet.</summary>
        public bool IsStatic => PlateTimeMyr == 0.0 && ErosionTimeMyr == 0.0;

        public override string ToString()
            => "DeepTimeContext(plate=" + PlateTimeMyr.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " Myr, erosion=" + ErosionTimeMyr.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " Myr, seaLevel=" + StaticSeaLevelMeters.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " m)";
    }
}
