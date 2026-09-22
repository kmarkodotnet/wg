namespace WorldGen.Core.Persistence
{
    /// <summary>
    /// ND-108: a teljes numerikus világmodell kompatibilitási azonosítója.
    /// Minden seed-/világadat-törő módosításkor emelendő, az érintett ND-ben
    /// dokumentálva. Nem alkalmazás- vagy fájlformátum-verzió.
    /// </summary>
    public static class WorldGeneratorVersion
    {
        // Első explicit alapvonal: 2026-09-22, ND-126b/130 utáni modell.
        public const string Current = "1";
    }
}
