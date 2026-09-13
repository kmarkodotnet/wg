using System;
using System.IO;
using System.Linq;
using System.Text;
using WorldGen.App.Storage;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Storage
{
    public class UserDataLayoutTests
    {
        [Fact]
        public void AllFoldersLiveUnderRoot()
        {
            var layout = new UserDataLayout(InMemoryFileSystem.Root);
            Assert.Equal(6, layout.AllDirectories.Count);
            foreach (string dir in layout.AllDirectories)
                Assert.True(UserDataLayout.IsInside(dir, layout.Root), dir);
            Assert.Equal(Path.Combine(layout.Root, "Settings", "settings.json"), layout.SettingsFile);

            var fs = new InMemoryFileSystem();
            layout.EnsureCreated(fs);
            Assert.All(layout.AllDirectories, d => Assert.True(fs.DirectoryExists(d)));
        }

        [Fact]
        public void RejectsRelativeOrEmptyRoot()
        {
            Assert.Throws<ArgumentException>(() => new UserDataLayout("relative/path"));
            Assert.Throws<ArgumentException>(() => new UserDataLayout(" "));
        }

        [Fact]
        public void IsInsideDoesNotMatchSiblingWithSamePrefix()
        {
            string games = Path.Combine(Path.GetTempPath(), "Games");
            string install = Path.Combine(games, "WorldGen");
            Assert.True(UserDataLayout.IsInside(install, install));
            Assert.True(UserDataLayout.IsInside(Path.Combine(install, "Data", "Saves"), install));
            Assert.False(UserDataLayout.IsInside(Path.Combine(games, "WorldGenData"), install));
            Assert.False(UserDataLayout.IsInside("", install));
        }
    }

    public class AtomicFileWriterTests
    {
        private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

        [Fact]
        public void PhysicalWriteReplaceKeepsPreviousVersionAsBackupAndLeavesNoTemp()
        {
            using var dir = new TempDirectory();
            var fs = new PhysicalFileSystem();
            var writer = new AtomicFileWriter(fs);
            string path = dir.Combine("world.wgsave");

            writer.WriteAllBytes(path, Utf8("v1"));
            Assert.False(File.Exists(AtomicFileWriter.GetBackupPath(path)));
            writer.WriteAllBytes(path, Utf8("v2"));
            writer.WriteAllBytes(path, Utf8("v3"));

            Assert.Equal("v3", File.ReadAllText(path));
            Assert.Equal("v2", File.ReadAllText(AtomicFileWriter.GetBackupPath(path)));
            Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
        }

        [Fact]
        public void PhysicalWriteWithoutBackup()
        {
            using var dir = new TempDirectory();
            var writer = new AtomicFileWriter(new PhysicalFileSystem(), keepBackup: false);
            string path = dir.Combine("settings.json");
            writer.WriteAllBytes(path, Utf8("a"));
            writer.WriteAllBytes(path, Utf8("b"));
            Assert.Equal("b", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir.Path));
        }

        [Fact]
        public void ExceptionWhileWritingKeepsOriginalAndRemovesTemp()
        {
            var fs = new InMemoryFileSystem();
            var writer = new AtomicFileWriter(fs);
            string path = Path.Combine(InMemoryFileSystem.Root, "world.wgsave");
            writer.WriteAllBytes(path, Utf8("good"));

            Assert.Throws<InvalidOperationException>(() => writer.Write(path, s =>
            {
                s.Write(Utf8("half"), 0, 4);
                throw new InvalidOperationException("serializer crashed");
            }));

            Assert.Equal("good", fs.ReadText(path));
            Assert.Equal(1, fs.FileCount);
        }

        [Fact]
        public void FailedReplaceKeepsOriginalAndRemovesTemp()
        {
            var fs = new InMemoryFileSystem();
            var writer = new AtomicFileWriter(fs);
            string path = Path.Combine(InMemoryFileSystem.Root, "world.wgsave");
            writer.WriteAllBytes(path, Utf8("good"));

            fs.FailNextReplace = true;
            Assert.Throws<IOException>(() => writer.WriteAllBytes(path, Utf8("new")));

            Assert.Equal("good", fs.ReadText(path));
            Assert.Equal(1, fs.FileCount);
        }

        [Fact]
        public void RecoversFromBackupWhenTargetIsMissing()
        {
            var fs = new InMemoryFileSystem();
            var writer = new AtomicFileWriter(fs);
            string path = Path.Combine(InMemoryFileSystem.Root, "settings.json");

            Assert.False(writer.TryRecover(path));
            fs.WriteText(AtomicFileWriter.GetBackupPath(path), "previous");
            Assert.True(writer.TryRecover(path));
            Assert.Equal("previous", fs.ReadText(path));
            Assert.False(fs.FileExists(AtomicFileWriter.GetBackupPath(path)));
            Assert.False(writer.TryRecover(path));
        }

        [Fact]
        public void DeletesOnlyOwnOrphanedTempFiles()
        {
            var fs = new InMemoryFileSystem();
            var writer = new AtomicFileWriter(fs);
            string root = InMemoryFileSystem.Root;
            string own = Path.Combine(root, "world.wgsave." + Guid.NewGuid().ToString("N") + ".tmp");
            fs.WriteText(own, "x");
            fs.WriteText(Path.Combine(root, "user-notes.tmp"), "keep");
            fs.WriteText(Path.Combine(root, "world.wgsave"), "keep");

            Assert.Equal(1, writer.DeleteOrphanedTempFiles(root));
            Assert.False(fs.FileExists(own));
            Assert.Equal(2, fs.FileCount);
        }

        [Theory]
        [InlineData("world.wgsave.0123456789abcdef0123456789abcdef.tmp", true)]
        [InlineData("world.wgsave.0123456789ABCDEF0123456789abcdef.tmp", false)]
        [InlineData("world.wgsave.0123456789abcdef.tmp", false)]
        [InlineData(".0123456789abcdef0123456789abcdef.tmp", false)]
        [InlineData("notes.tmp", false)]
        [InlineData("world.wgsave.0123456789abcdef0123456789abcdef.bak", false)]
        public void RecognizesOwnTempFileNames(string name, bool expected)
        {
            Assert.Equal(expected, AtomicFileWriter.IsOwnTempFileName(name));
        }

        [Fact]
        public void PhysicalGetFilesMatchesSuffixExactly()
        {
            using var dir = new TempDirectory();
            File.WriteAllText(dir.Combine("a.log"), "");
            File.WriteAllText(dir.Combine("b.logx"), "");
            File.WriteAllText(dir.Combine("c.LOG"), "");
            var files = new PhysicalFileSystem().GetFiles(dir.Path, ".log");
            Assert.Equal(new[] { "a.log" }, files.Select(Path.GetFileName));
            Assert.Empty(new PhysicalFileSystem().GetFiles(dir.Combine("missing"), ".log"));
        }
    }

    public class FileNameSanitizerTests
    {
        [Theory]
        [InlineData("Gaia-8214", "Gaia-8214")]
        [InlineData("  Gaia   8214 .", "Gaia 8214")]
        [InlineData("a<b>c:d\"e/f\\g|h?i*j", "a_b_c_d_e_f_g_h_i_j")]
        [InlineData("tab\there", "tab_here")]
        [InlineData("...", "World")]
        [InlineData("", "World")]
        [InlineData("   ", "World")]
        [InlineData("CON", "_CON")]
        [InlineData("con.txt", "_con.txt")]
        [InlineData("lpt9", "_lpt9")]
        [InlineData("COM10", "COM10")]
        [InlineData("Földközi Világ", "Földközi Világ")]
        public void SanitizesNames(string input, string expected)
        {
            Assert.Equal(expected, FileNameSanitizer.Sanitize(input));
        }

        [Fact]
        public void NullUsesFallback()
        {
            Assert.Equal("Save", FileNameSanitizer.Sanitize(null, "Save"));
        }

        [Fact]
        public void TruncatesWithoutSplittingSurrogatePair()
        {
            string name = new string('a', 9) + "\U0001F30D" + "rest";
            string result = FileNameSanitizer.Sanitize(name, maxLength: 10);
            Assert.Equal(new string('a', 9), result);
            Assert.Equal(64, FileNameSanitizer.Sanitize(new string('x', 200)).Length);
        }

        [Fact]
        public void MakeUniqueAppendsCounter()
        {
            var existing = new[] { "Gaia.wgsave", "Gaia (2).wgsave" };
            Assert.Equal("Gaia (3).wgsave", FileNameSanitizer.MakeUnique("Gaia", ".wgsave", existing.Contains));
            Assert.Equal("Terra.wgsave", FileNameSanitizer.MakeUnique("Terra", ".wgsave", existing.Contains));
        }
    }
}
