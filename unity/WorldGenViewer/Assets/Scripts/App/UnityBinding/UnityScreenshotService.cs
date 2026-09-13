#nullable enable
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using WorldGen.App.Screenshots;
using WorldGen.App.Storage;

namespace WorldGen.App.UnityBinding
{
    /// <summary>
    /// A UI elrejtéséhez szükséges kapcsoló. A viewer mostani IMGUI/Canvas UI-ja ezt
    /// még nem valósítja meg; amíg nincs bekötve, a „UI nélküli” kép is tartalmazza a UI-t.
    /// </summary>
    public interface IUiVisibility
    {
        bool IsUiVisible { get; }
        void SetUiVisible(bool visible);
    }

    /// <summary>Képernyőkép a frame végén, atomi PNG-írással (WF-SHOT-001/002).</summary>
    public sealed class UnityScreenshotService : MonoBehaviour
    {
        private string _directory = "";
        private IUiVisibility? _uiVisibility;

        public bool IsBusy { get; private set; }

        public event Action<string>? ScreenshotSaved;
        public event Action<Exception>? ScreenshotFailed;

        public void Configure(string directory, IUiVisibility? uiVisibility)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("Üres screenshot-könyvtár.", nameof(directory));
            _directory = directory;
            _uiVisibility = uiVisibility;
        }

        public bool Capture(ScreenshotRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (IsBusy || _directory.Length == 0 || !isActiveAndEnabled) return false;
            StartCoroutine(CaptureRoutine(request));
            return true;
        }

        private IEnumerator CaptureRoutine(ScreenshotRequest request)
        {
            IsBusy = true;
            bool restoreUi = false;
            if (!request.IncludeUi && _uiVisibility != null && _uiVisibility.IsUiVisible)
            {
                _uiVisibility.SetUiVisible(false);
                restoreUi = true;
                yield return null; // egy frame, hogy a UI ténylegesen eltűnjön
            }
            yield return new WaitForEndOfFrame();

            Texture2D? texture = null;
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture(request.SuperSize);
                byte[] png = texture.EncodeToPNG();
                Directory.CreateDirectory(_directory);
                string path = ScreenshotNaming.CreateUniquePath(_directory, DateTime.Now, !request.IncludeUi, File.Exists);
                new AtomicFileWriter(new PhysicalFileSystem(), keepBackup: false).WriteAllBytes(path, png);
                ScreenshotSaved?.Invoke(path);
            }
            catch (Exception ex)
            {
                ScreenshotFailed?.Invoke(ex);
            }
            finally
            {
                if (texture != null) Destroy(texture);
                if (restoreUi) _uiVisibility!.SetUiVisible(true);
                IsBusy = false;
            }
        }
    }
}
