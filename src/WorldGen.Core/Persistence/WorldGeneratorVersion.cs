namespace WorldGen.Core.Persistence
{
    /// <summary>
    /// ND-108: a teljes numerikus világmodell kompatibilitási azonosítója.
    /// Minden seed-/világadat-törő módosításkor emelendő, az érintett ND-ben
    /// dokumentálva. Nem alkalmazás- vagy fájlformátum-verzió.
    /// </summary>
    public static class WorldGeneratorVersion
    {
        // ND-137 2. kör: folyóvízi bevágódás (a relief előbb NŐ, majd lekopik)
        // + parti abrázió (a tengerszint körüli sáv a tengerszint felé
        // planálódik). Csak a `t > 0` deep-time kimenet változik; a statikus
        // (t = 0) világok bitre azonosak maradnak.
        // Előzmény — ND-137 (A20): valódi deep-time erózió — a relief-zajtagok
        // hullámhossz-szelektív, hidrológia-vezérelt csillapítása, és a modul
        // átállítása a DeterministicMath Exp/Sin/Asin-jára (korábban nyers
        // System.Math, tehát platformok között nem garantált).
        // Előzmény — ND-136 (A19): a domborzati zaj a lemez saját
        // vonatkoztatási rendszerében értékelődik ki, tehát együtt vándorol
        // a kéreggel.
        // Előzmény — ND-142: a pillanatnyi levegőanomália visszahat a termikus szélre.
        public const string Current = "5";
    }
}
