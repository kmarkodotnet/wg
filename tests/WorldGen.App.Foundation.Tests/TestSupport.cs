using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WorldGen.App.Storage;

namespace WorldGen.App.Foundation.Tests
{
    public sealed class ManualClock : IClock
    {
        public ManualClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; set; }

        public void Advance(TimeSpan delta) => UtcNow += delta;
    }

    /// <summary>Egyedi ideiglenes könyvtár, Dispose-kor törlődik.</summary>
    public sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "worldgen-app-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Combine(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Memóriabeli fájlrendszer hibainjektálással; a lemezhez nem nyúl.</summary>
    public sealed class InMemoryFileSystem : IFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _times = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly HashSet<string> _directories = new HashSet<string>(StringComparer.Ordinal);

        public static string Root { get; } = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "worldgen-inmemory"));

        public DateTime NowUtc { get; set; } = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

        public bool FailNextReplace { get; set; }

        public bool FailNextMove { get; set; }

        public int FileCount => _files.Count;

        public IEnumerable<string> AllFiles => _files.Keys;

        public bool FileExists(string path) => _files.ContainsKey(path);

        public bool DirectoryExists(string path) => _directories.Contains(path);

        public void CreateDirectory(string path) => _directories.Add(path);

        public byte[] ReadAllBytes(string path)
        {
            if (!_files.TryGetValue(path, out var data)) throw new FileNotFoundException("nincs: " + path);
            return (byte[])data.Clone();
        }

        public Stream OpenRead(string path) => new MemoryStream(ReadAllBytes(path), writable: false);

        public Stream CreateNew(string path)
        {
            if (_files.ContainsKey(path)) throw new IOException("már létezik: " + path);
            _files[path] = Array.Empty<byte>();
            _times[path] = NowUtc;
            return new CommitStream(this, path);
        }

        public void FlushToDisk(Stream stream) => stream.Flush();

        public void DeleteFile(string path)
        {
            _files.Remove(path);
            _times.Remove(path);
        }

        public void MoveFile(string sourcePath, string destinationPath)
        {
            if (FailNextMove)
            {
                FailNextMove = false;
                throw new IOException("injektált move-hiba");
            }
            if (!_files.ContainsKey(sourcePath)) throw new FileNotFoundException("nincs: " + sourcePath);
            if (_files.ContainsKey(destinationPath)) throw new IOException("a cél létezik: " + destinationPath);
            _files[destinationPath] = _files[sourcePath];
            _times[destinationPath] = NowUtc;
            DeleteFile(sourcePath);
        }

        public void ReplaceFile(string sourcePath, string destinationPath, string? backupPath)
        {
            if (FailNextReplace)
            {
                FailNextReplace = false;
                throw new IOException("injektált replace-hiba");
            }
            if (!_files.ContainsKey(sourcePath)) throw new FileNotFoundException("nincs: " + sourcePath);
            if (!_files.ContainsKey(destinationPath)) throw new FileNotFoundException("nincs: " + destinationPath);
            if (backupPath != null)
            {
                _files[backupPath] = _files[destinationPath];
                _times[backupPath] = _times[destinationPath];
            }
            _files[destinationPath] = _files[sourcePath];
            _times[destinationPath] = NowUtc;
            DeleteFile(sourcePath);
        }

        public IReadOnlyList<string> GetFiles(string directory, string suffix)
        {
            var result = new List<string>();
            foreach (string path in _files.Keys)
            {
                if (!string.Equals(System.IO.Path.GetDirectoryName(path), directory, StringComparison.Ordinal)) continue;
                if (System.IO.Path.GetFileName(path).EndsWith(suffix, StringComparison.Ordinal)) result.Add(path);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        public DateTime GetLastWriteTimeUtc(string path) => _times[path];

        public long GetFileLength(string path) => _files[path].Length;

        public void WriteFile(string path, byte[] data)
        {
            _files[path] = (byte[])data.Clone();
            _times[path] = NowUtc;
        }

        public void WriteText(string path, string text) => WriteFile(path, new UTF8Encoding(false).GetBytes(text));

        public string ReadText(string path) => Encoding.UTF8.GetString(ReadAllBytes(path));

        /// <summary>Sérülés szimulálása: egy bájt átbillentése.</summary>
        public void FlipByte(string path, int offset)
        {
            _files[path][offset] ^= 0xFF;
        }

        private sealed class CommitStream : MemoryStream
        {
            private readonly InMemoryFileSystem _owner;
            private readonly string _path;

            public CommitStream(InMemoryFileSystem owner, string path)
            {
                _owner = owner;
                _path = path;
            }

            public override void Flush()
            {
                base.Flush();
                Commit();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) Commit();
                base.Dispose(disposing);
            }

            private void Commit()
            {
                if (_owner._files.ContainsKey(_path)) _owner._files[_path] = ToArray();
            }
        }
    }
}
