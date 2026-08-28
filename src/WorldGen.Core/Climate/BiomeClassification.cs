namespace WorldGen.Core.Climate
{
    /// <summary>Biome-kategória (§5.2, docs/05-milestones.md) - csak hőmérséklet + víz/szárazföld alapú.</summary>
    public enum Biome
    {
        Ocean,
        SeaIce,
        IceSheet,
        Tundra,
        Temperate,
        Tropical,
    }

    /// <summary>
    /// M5 biome/jég-osztályozás (§5.2) — ND-10 javasolt alapértelmezése: fix
    /// Föld-szerű küszöbök, nem bolygóparaméter-skálázott (v1.0-ban).
    ///
    /// HATÓKÖR: csak HŐMÉRSÉKLET + víz/szárazföld alapján osztályoz — a
    /// csapadék/nedvesség (§31) halasztva van, ezért NEM különböztetünk meg
    /// pl. sivatagot/esőerdőt (ahhoz nedvesség-adat kellene) — csak
    /// hőmérsékleti sávokat + jég/óceánt.
    ///
    /// Nincs transzcendens függvény itt — csak küszöb-összehasonlítás a már
    /// kiszámolt hőmérsékleten, tehát BITPONTOS.
    /// </summary>
    public static class BiomeClassification
    {
        public const double OceanFreezingK = 271.15; // ~ -2°C, sós víz fagyáspontja
        public const double IceSheetThresholdK = 263.15; // -10°C
        public const double TundraThresholdK = 278.15; // 5°C
        public const double TemperateThresholdK = 293.15; // 20°C

        public static Biome Classify(double temperatureK, bool isOceanic)
        {
            if (isOceanic)
                return temperatureK < OceanFreezingK ? Biome.SeaIce : Biome.Ocean;

            if (temperatureK < IceSheetThresholdK) return Biome.IceSheet;
            if (temperatureK < TundraThresholdK) return Biome.Tundra;
            if (temperatureK < TemperateThresholdK) return Biome.Temperate;
            return Biome.Tropical;
        }
    }
}
