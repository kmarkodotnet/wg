#nullable enable
using System;
using System.IO;

namespace WorldGen.App.Storage
{
    /// <summary>
    /// Atomi fájlcsere (WF-SAVE-002): ideiglenes fájlba ír, lemezre üríti, majd
    /// egy lépésben a cél helyére teszi. Írás közbeni hiba vagy összeomlás
    /// esetén a korábbi fájl érintetlen marad; a régi változat opcionálisan
    /// <c>.bak</c>-ként megmarad.
    /// </summary>
    public sealed class AtomicFileWriter
    {
        public const string TempExtension = ".tmp";
        public const string BackupExtension = ".bak";

        private readonly IFileSystem _fileSystem;

        public bool KeepBackup { get; }

        public AtomicFileWriter(IFileSystem fileSystem, bool keepBackup = true)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            KeepBackup = keepBackup;
        }

        public static string GetBackupPath(string path) => path + BackupExtension;

        public void WriteAllBytes(string path, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Write(path, stream => stream.Write(data, 0, data.Length));
        }

        public void Write(string path, Action<Stream> writeContent)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Üres cél-út.", nameof(path));
            if (writeContent == null) throw new ArgumentNullException(nameof(writeContent));

            string tempPath = path + "." + Guid.NewGuid().ToString("N") + TempExtension;
            try
            {
                using (var stream = _fileSystem.CreateNew(tempPath))
                {
                    writeContent(stream);
                    _fileSystem.FlushToDisk(stream);
                }

                if (_fileSystem.FileExists(path))
                    _fileSystem.ReplaceFile(tempPath, path, KeepBackup ? GetBackupPath(path) : null);
                else
                    _fileSystem.MoveFile(tempPath, path);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        /// <summary>
        /// Ha a cél hiányzik, de van <c>.bak</c> (csere közbeni megszakadás),
        /// visszaállítja. Igaz, ha helyreállítás történt.
        /// </summary>
        public bool TryRecover(string path)
        {
            string backup = GetBackupPath(path);
            if (_fileSystem.FileExists(path) || !_fileSystem.FileExists(backup)) return false;
            _fileSystem.MoveFile(backup, path);
            return true;
        }

        /// <summary>
        /// A könyvtárban maradt, ezen írótól származó ideiglenes fájlok törlése
        /// (név: "cél.32hex.tmp"). A törölt fájlok száma.
        /// </summary>
        public int DeleteOrphanedTempFiles(string directory)
        {
            int deleted = 0;
            foreach (string file in _fileSystem.GetFiles(directory, TempExtension))
            {
                if (!IsOwnTempFileName(Path.GetFileName(file))) continue;
                if (TryDelete(file)) deleted++;
            }
            return deleted;
        }

        public static bool IsOwnTempFileName(string fileName)
        {
            if (!fileName.EndsWith(TempExtension, StringComparison.Ordinal)) return false;
            string stem = fileName.Substring(0, fileName.Length - TempExtension.Length);
            int dot = stem.LastIndexOf('.');
            if (dot <= 0 || stem.Length - dot - 1 != 32) return false;
            for (int i = dot + 1; i < stem.Length; i++)
            {
                char ch = stem[i];
                bool hex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f');
                if (!hex) return false;
            }
            return true;
        }

        private bool TryDelete(string path)
        {
            try
            {
                _fileSystem.DeleteFile(path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
