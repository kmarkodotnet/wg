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

        /// <summary>
        /// NEM a világmodell része (docs/backlog.md "Csillagos háttér",
        /// docs/04-decisions.md ND-51) - a Naprendszeren kívüli, tisztán
        /// dekoratív renderelési tartalom (pl. háttér-csillagmező) ide
        /// tartozik. Külön van választva a világmodell-domainektől (1-8),
        /// mert ide NEM vonatkoznak ugyanazok a seed-kompatibilitási
        /// garanciák (I1 a VILÁGOT védi, nem a Naprendszeren kívüli
        /// díszletet) - de a felhasználói kérésre MÉGIS determinisztikus
        /// marad (ugyanaz a seed -> ugyanaz a csillagkép).
        /// </summary>
        public const uint Decorative = 32;

        // 33+ : további bővítéseknek

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
        // ND-45 (lemez-eletciklus, M10+M11): 14-16. A 17-19 fenntartva a
        // halasztott HotspotBirth/HotspotDeath-nek.
        public const uint PlateLifecycleRoll = 14;
        public const uint PlateSplitAxisHint = 15;
        public const uint PlateChildId = 16;

        // Events
        public const uint ImpactTrigger = 20;
        public const uint ImpactMagnitude = 21;
        public const uint VolcanicTrigger = 22;
        public const uint ImpactPosition = 23;
        public const uint ImpactVelocity = 24;
        public const uint ImpactAngle = 25;
        public const uint VolcanicMagnitude = 26;
        public const uint VolcanicPosition = 27;

        // Naming
        public const uint SyllableChoice = 30;
        public const uint SuffixChoice = 31;

        // Decorative (ld. RandomDomain.Decorative doksi - NEM a világmodell része)
        public const uint StarPosition = 40;
        public const uint StarBrightness = 41;

        /// <summary>
        /// A tektonikuslemez-overlay (docs/backlog.md, 2026-09-13) lemezenkénti
        /// SZÍNÁRNYALAT-eltolása - tisztán renderelési/UI tulajdonság, nem a
        /// lemez fizikai állapota (azt a <see cref="RandomDomain.Tectonics"/>
        /// domain PlateSeedPoint/EulerPole/PlateVelocity/CrustType tulajdonságai
        /// már meghatározzák) - ezért itt, a Decorative domainben van, ugyanúgy,
        /// mint a csillagmező.
        /// </summary>
        public const uint PlateColorHue = 42;
    }
}
