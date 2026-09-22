using WorldGen.App.Saves;
using WorldGen.Core.Persistence;

namespace WorldGen.App.UnityBinding
{
    /// <summary>
    /// ND-108: a Core-verzió egyetlen app-oldali kötése. A Foundation nem
    /// ismeri a Core-t; régi fejléc olvasásakor nincs automatikus átverziózás.
    /// A teljes világállapot összeállítása a session-host külön feladata.
    /// </summary>
    public static class CoreSavePolicy
    {
        public static SaveHeader CreateHeader() => new SaveHeader
        {
            WorldGeneratorVersion = WorldGeneratorVersion.Current,
        };

        public static SaveCompatibilityResult Evaluate(SaveHeader header) =>
            SaveCompatibility.Evaluate(header, SaveHeaderCodec.CurrentFormatVersion, WorldGeneratorVersion.Current);
    }
}
