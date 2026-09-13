#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace WorldGen.App.Storage
{
    public sealed class PhysicalFileSystem : IFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

        public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        public Stream CreateNew(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        public void FlushToDisk(Stream stream)
        {
            if (stream is FileStream fs) fs.Flush(true);
            else stream.Flush();
        }

        public void DeleteFile(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        public void MoveFile(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath);

        public void ReplaceFile(string sourcePath, string destinationPath, string? backupPath)
        {
            if (backupPath != null && File.Exists(backupPath)) File.Delete(backupPath);
            File.Replace(sourcePath, destinationPath, backupPath);
        }

        public IReadOnlyList<string> GetFiles(string directory, string suffix)
        {
            if (!Directory.Exists(directory)) return Array.Empty<string>();
            // Nem "*.ext" keresőmintát adunk át: Windows alatt a 3 betűs
            // kiterjesztésminta a hosszabbakra is illeszkedne (8.3-örökség).
            var result = new List<string>();
            foreach (string path in Directory.GetFiles(directory))
            {
                if (Path.GetFileName(path).EndsWith(suffix, StringComparison.Ordinal)) result.Add(path);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

        public long GetFileLength(string path) => new FileInfo(path).Length;
    }
}
