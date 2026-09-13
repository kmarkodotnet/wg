#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace WorldGen.App.Storage
{
    /// <summary>
    /// Vékony fájlrendszer-absztrakció, hogy a mentés, a beállítások és a
    /// napló-megőrzés logikája memóriában is tesztelhető legyen.
    /// </summary>
    public interface IFileSystem
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        void CreateDirectory(string path);

        byte[] ReadAllBytes(string path);
        Stream OpenRead(string path);

        /// <summary>Új fájl létrehozása; ha már létezik, <see cref="IOException"/>.</summary>
        Stream CreateNew(string path);

        /// <summary>A stream tartalmát lemezre üríti (fizikai fájlnál az OS-pufferből is).</summary>
        void FlushToDisk(Stream stream);

        /// <summary>Törlés; hiányzó fájlnál nem hiba.</summary>
        void DeleteFile(string path);

        /// <summary>Áthelyezés; a cél nem létezhet.</summary>
        void MoveFile(string sourcePath, string destinationPath);

        /// <summary>A cél cseréje a forrásra; ha <paramref name="backupPath"/> nem null, a régi cél oda kerül.</summary>
        void ReplaceFile(string sourcePath, string destinationPath, string? backupPath);

        /// <summary>A könyvtár közvetlen fájljai, amelyek neve pontosan <paramref name="suffix"/>-re végződik; ordinális sorrendben, teljes úttal.</summary>
        IReadOnlyList<string> GetFiles(string directory, string suffix);

        DateTime GetLastWriteTimeUtc(string path);
        long GetFileLength(string path);
    }
}
