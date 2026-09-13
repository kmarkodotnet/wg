using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WorldGen.App.Settings;
using WorldGen.App.Storage;
using Xunit;

namespace WorldGen.App.Foundation.Tests.Settings
{
    public class SettingsStoreTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        private static readonly string Dir = Path.Combine(InMemoryFileSystem.Root, "Settings");
        private static readonly string FilePath = Path.Combine(Dir, "settings.json");

        private static SettingsStore NewStore(InMemoryFileSystem fs) => new SettingsStore(fs, FilePath, new ManualClock(Now));

        [Fact]
        public void FirstRunUsesDefaultsWithoutWriting()
        {
            var fs = new InMemoryFileSystem();
            var result = NewStore(fs).Load();
            Assert.True(result.IsFirstRun);
            Assert.Empty(result.Issues);
            Assert.Equal(SettingsCategory.None, new AppSettings().DiffCategories(result.Settings));
            Assert.Equal(0, fs.FileCount);
        }

        [Fact]
        public void AppliedSettingsSurviveRestart()
        {
            var fs = new InMemoryFileSystem();
            var store = NewStore(fs);
            store.Load();
            var changes = new List<SettingsCategory>();
            store.Changed += (_, diff) => changes.Add(diff);

            var updated = store.Snapshot;
            updated.Audio.Music = 0.2;
            updated.Graphics.FpsLimit = 90;
            Assert.Equal(SettingsCategory.Audio | SettingsCategory.Graphics, store.Apply(updated));
            Assert.Equal(new[] { SettingsCategory.Audio | SettingsCategory.Graphics }, changes);

            var restarted = NewStore(fs);
            var loaded = restarted.Load();
            Assert.False(loaded.IsFirstRun);
            Assert.Empty(loaded.Issues);
            Assert.Equal(0.2, loaded.Settings.Audio.Music);
            Assert.Equal(90, restarted.Snapshot.Graphics.FpsLimit);
            Assert.EndsWith("\n", fs.ReadText(FilePath));
        }

        [Fact]
        public void ApplyingIdenticalOrNormalizesToSameIsNoOp()
        {
            var fs = new InMemoryFileSystem();
            var store = NewStore(fs);
            store.Load();
            int events = 0;
            store.Changed += (_, __) => events++;

            Assert.Equal(SettingsCategory.None, store.Apply(store.Snapshot));
            var same = store.Snapshot;
            same.Audio.Master = 7.0; // 1.0-ra normalizálódik = az alapérték
            Assert.Equal(SettingsCategory.None, store.Apply(same));
            Assert.Equal(0, events);
            Assert.Equal(0, fs.FileCount);
        }

        [Fact]
        public void SnapshotIsDetachedCopy()
        {
            var store = NewStore(new InMemoryFileSystem());
            store.Snapshot.Audio.Master = 0.0;
            Assert.Equal(1.0, store.Snapshot.Audio.Master);
        }

        [Fact]
        public void CorruptFileIsPreservedAndDefaultsAreUsed()
        {
            var fs = new InMemoryFileSystem();
            fs.WriteText(FilePath, "{ \"version\": 1, \"graphics\": ");
            var result = NewStore(fs).Load();

            Assert.Equal(SettingsCategory.None, new AppSettings().DiffCategories(result.Settings));
            Assert.Equal(Path.Combine(Dir, "settings.corrupt-20260913-120000.json"), result.CorruptFileCopyPath);
            Assert.Equal("{ \"version\": 1, \"graphics\": ", fs.ReadText(result.CorruptFileCopyPath!));

            var second = NewStore(fs).Load();
            Assert.Equal(Path.Combine(Dir, "settings.corrupt-20260913-120000 (2).json"), second.CorruptFileCopyPath);
        }

        [Fact]
        public void InvalidUtf8IsTreatedAsCorrupt()
        {
            var fs = new InMemoryFileSystem();
            fs.WriteFile(FilePath, new byte[] { 0x7B, 0xFF, 0xFE, 0x7D });
            var result = NewStore(fs).Load();
            Assert.Contains(result.Issues, i => i.Kind == SettingsIssueKind.Corrupt);
            Assert.NotNull(result.CorruptFileCopyPath);
        }

        [Fact]
        public void Utf8BomIsAccepted()
        {
            var fs = new InMemoryFileSystem();
            byte[] body = Encoding.UTF8.GetBytes(SettingsSerializer.Serialize(SettingsSerializerTests.AllNonDefault()));
            var withBom = new byte[body.Length + 3];
            withBom[0] = 0xEF;
            withBom[1] = 0xBB;
            withBom[2] = 0xBF;
            Array.Copy(body, 0, withBom, 3, body.Length);
            fs.WriteFile(FilePath, withBom);

            var result = NewStore(fs).Load();
            Assert.Empty(result.Issues);
            Assert.True(result.Settings.Audio.Muted);
        }

        [Fact]
        public void RecoversFromBackupWhenFileIsMissing()
        {
            var fs = new InMemoryFileSystem();
            var s = new AppSettings();
            s.Controls.InvertOrbitY = true;
            fs.WriteText(AtomicFileWriter.GetBackupPath(FilePath), SettingsSerializer.Serialize(s));

            var result = NewStore(fs).Load();
            Assert.True(result.RecoveredFromBackup);
            Assert.False(result.IsFirstRun);
            Assert.True(result.Settings.Controls.InvertOrbitY);
        }

        [Fact]
        public void NewerVersionFileIsCopiedBeforeFirstOverwrite()
        {
            var fs = new InMemoryFileSystem();
            const string newer = "{\"version\": 7, \"audio\": {\"master\": 0.5}, \"future\": {\"x\": 1}}";
            fs.WriteText(FilePath, newer);
            var store = NewStore(fs);
            Assert.True(store.Load().IsFromNewerVersion);

            var updated = store.Snapshot;
            updated.Audio.Muted = true;
            store.Apply(updated);
            updated.Audio.Muted = false;
            store.Apply(updated);

            string copy = Path.Combine(Dir, "settings.v7.json");
            Assert.Equal(newer, fs.ReadText(copy));
            Assert.Contains("\"version\": 1", fs.ReadText(FilePath));
        }

        [Fact]
        public void FailedWriteKeepsCurrentStateAndRaisesNoEvent()
        {
            var fs = new InMemoryFileSystem();
            var store = NewStore(fs);
            store.Load();
            store.Save();
            int events = 0;
            store.Changed += (_, __) => events++;

            var updated = store.Snapshot;
            updated.Audio.Muted = true;
            fs.FailNextReplace = true;
            Assert.Throws<IOException>(() => store.Apply(updated));

            Assert.False(store.Snapshot.Audio.Muted);
            Assert.Equal(0, events);
            Assert.False(NewStoreLoaded(fs).Audio.Muted);
        }

        [Fact]
        public void RestoreDefaultsResetsOnlyRequestedCategory()
        {
            var fs = new InMemoryFileSystem();
            var store = NewStore(fs);
            store.Load();
            store.Apply(SettingsSerializerTests.AllNonDefault());

            Assert.Equal(SettingsCategory.Audio, store.RestoreDefaults(SettingsCategory.Audio));
            var loaded = NewStoreLoaded(fs);
            Assert.Equal(1.0, loaded.Audio.Master);
            Assert.Equal(GraphicsQuality.Ultra, loaded.Graphics.Quality);
        }

        [Fact]
        public void PhysicalFileRoundTripWritesUtf8WithoutBom()
        {
            using var dir = new TempDirectory();
            var layout = new UserDataLayout(dir.Path);
            var fs = new PhysicalFileSystem();
            var store = new SettingsStore(fs, layout.SettingsFile, new ManualClock(Now));
            Assert.True(store.Load().IsFirstRun);
            store.Apply(SettingsSerializerTests.AllNonDefault());

            byte[] bytes = File.ReadAllBytes(layout.SettingsFile);
            Assert.NotEqual(0xEF, bytes[0]);
            var reloaded = new SettingsStore(fs, layout.SettingsFile, new ManualClock(Now)).Load();
            Assert.Empty(reloaded.Issues);
            Assert.Equal(SettingsCategory.None, SettingsSerializerTests.AllNonDefault().DiffCategories(reloaded.Settings));
        }

        private static AppSettings NewStoreLoaded(InMemoryFileSystem fs) => NewStore(fs).Load().Settings;
    }

    public class SettingsEditSessionTests
    {
        [Fact]
        public void ApplyCancelAndRestoreDefaults()
        {
            var fs = new InMemoryFileSystem();
            var store = new SettingsStore(fs, Path.Combine(InMemoryFileSystem.Root, "settings.json"), new ManualClock(DateTime.UtcNow));
            store.Load();
            var edit = new SettingsEditSession(store);
            Assert.False(edit.IsDirty);

            edit.Working.Audio.Ui = 0.1;
            edit.Working.Controls.CameraSpeed = 2;
            Assert.Equal(SettingsCategory.Audio | SettingsCategory.Controls, edit.DirtyCategories);

            edit.Cancel();
            Assert.False(edit.IsDirty);
            Assert.Equal(0.6, edit.Working.Audio.Ui);

            edit.Working.Audio.Ui = 0.1;
            Assert.Equal(SettingsCategory.Audio, edit.Apply());
            Assert.False(edit.IsDirty);
            Assert.Equal(0.1, store.Snapshot.Audio.Ui);

            edit.RestoreDefaults(SettingsCategory.Audio);
            Assert.True(edit.IsDirty);
            Assert.Equal(0.1, store.Snapshot.Audio.Ui);
            edit.Apply();
            Assert.Equal(0.6, store.Snapshot.Audio.Ui);

            edit.Working.Graphics.FpsLimit = -3; // 0-ra normalizálódik = változatlan
            Assert.False(edit.IsDirty);
        }
    }
}
