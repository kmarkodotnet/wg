namespace WorldGen.Core.Random
{
    /// <summary>
    /// Domain-azonosítók a determinisztikus mintavételhez.
    ///
    /// FONTOS: az értékek EXPLICITEK és SOHA nem változhatnak. Nem string-hash-ből
    /// származnak, mert az ütközhetne és a hash-algoritmus verziófüggő lenne.
    /// Új domain csak új, még nem használt számot kaphat.
    /// Meglévő szám átszámozása vagy újrafelhasználása seed-törő változás.
    /// </summary>
    public static class RandomDomain
    {
        public const uint Terrain = 1;
        public const uint Tectonics = 2;
        public const uint Climate = 3;
        public const uint Hydrology = 4;
        public const uint Events = 5;
        public const uint Naming = 6;
        public const uint Cyclone = 7;
        public const uint NeuralTexture = 8;

        // 9-31: fenntartva a core szimulációnak
        // 32+ : bővítéseknek

        /// <summary>Tesztekhez fenntartott domain. Produkciós kód nem használhatja.</summary>
        public const uint Test = 999;
    }

    /// <summary>
    /// Tulajdonság-azonosítók domainen belül. Ugyanaz a szabály: explicit, változatlan.
    /// </summary>
    public static class RandomProperty
    {
        public const uint Default = 0;

        // Terrain
        public const uint ContinentSeedPoint = 1;
        public const uint NoiseGradient = 2;
        public const uint RoughnessModifier = 3;

        // Tectonics
        public const uint PlateSeedPoint = 10;
        public const uint EulerPole = 11;
        public const uint PlateVelocity = 12;
        public const uint CrustType = 13;

        // Events
        public const uint ImpactTrigger = 20;
        public const uint ImpactMagnitude = 21;
        public const uint VolcanicTrigger = 22;
        public const uint ImpactPosition = 23;
        public const uint ImpactVelocity = 24;
        public const uint ImpactAngle = 25;

        // Naming
        public const uint SyllableChoice = 30;
        public const uint SuffixChoice = 31;
    }
}
