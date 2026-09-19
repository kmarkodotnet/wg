namespace WorldGen.Core.Terrain
{
    /// <summary>
    /// Talaj/regolit profil MVP-je (spec §39 <c>RegolithProfile</c>, ld.
    /// docs/01-architecture.md §13 és ND-117 a docs/04-decisions.md-ben). A
    /// spec 10 mezőjéből itt csak három van: a maradék hét (MineralDiversity,
    /// PhosphorusAvailability, NitrogenAvailability, Iron, Sulfur, Salinity,
    /// pHProxy) mind litológia-/vulkanizmus-függő forrást igényelne, ami a
    /// Core-ban tile-szinten még nem létezik — az I4 invariáns szerint
    /// inkább hiányozzon a mező, mint kitalált érték szerepeljen rajta.
    /// </summary>
    public readonly struct RegolithProfile
    {
        /// <summary>Regolit-vastagság, méter. [0, RegolithModel.DepthAbsoluteCapM].</summary>
        public readonly double DepthMeters;

        /// <summary>Porozitás, dimenziómentes. [0, 1].</summary>
        public readonly double Porosity;

        /// <summary>Vízmegtartó-kapacitás (nem aktuális nedvességtartalom), dimenziómentes. [0, 1].</summary>
        public readonly double WaterRetention;

        public RegolithProfile(double depthMeters, double porosity, double waterRetention)
        {
            DepthMeters = depthMeters;
            Porosity = porosity;
            WaterRetention = waterRetention;
        }
    }
}
