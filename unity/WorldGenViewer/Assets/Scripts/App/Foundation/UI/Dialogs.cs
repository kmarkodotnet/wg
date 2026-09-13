#nullable enable
using System;
using System.Collections.Generic;
using WorldGen.App.Localization;
using WorldGen.App.Navigation;

namespace WorldGen.App.UI
{
    public enum DialogKind
    {
        Information,
        Confirm,
        Warning,
        Error,
    }

    public enum DialogButtonRole
    {
        Default,
        Primary,
        Cancel,
        Destructive,
    }

    public sealed class DialogButton
    {
        public const string OkId = "ok";
        public const string ConfirmId = "confirm";
        public const string CancelId = "cancel";

        public string Id { get; }
        public string Label { get; }
        public DialogButtonRole Role { get; }

        public DialogButton(string id, string label, DialogButtonRole role = DialogButtonRole.Default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Üres gombazonosító.", nameof(id));
            Id = id;
            Label = label ?? "";
            Role = role;
        }
    }

    /// <summary>Egy modális dialógus leírása (WF-UI-003); a nézet ebből rajzol.</summary>
    public sealed class DialogRequest
    {
        public DialogKind Kind { get; }
        public string Title { get; }
        public string Message { get; }
        public IReadOnlyList<DialogButton> Buttons { get; }

        /// <summary>
        /// Igaz: ha a felhasználó kikapcsolta a megerősítéseket, a dialógus meg sem
        /// jelenik, és a fő gomb választódik. Visszafordíthatatlan műveletnél hamis legyen.
        /// </summary>
        public bool IsSkippable { get; }

        /// <summary>Opcionális azonosító: azonos címkéjű dialógus nem kerül kétszer a sorba.</summary>
        public string? Tag { get; }

        public DialogRequest(DialogKind kind, string title, string message, IReadOnlyList<DialogButton> buttons,
            bool isSkippable = false, string? tag = null)
        {
            if (buttons == null || buttons.Count == 0) throw new ArgumentException("Legalább egy gomb kell.", nameof(buttons));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int cancelCount = 0;
            foreach (var b in buttons)
            {
                if (b == null) throw new ArgumentException("Null gomb.", nameof(buttons));
                if (!ids.Add(b.Id)) throw new ArgumentException("Duplikált gombazonosító: " + b.Id, nameof(buttons));
                if (b.Role == DialogButtonRole.Cancel) cancelCount++;
            }
            if (cancelCount > 1) throw new ArgumentException("Legfeljebb egy Cancel szerepű gomb lehet.", nameof(buttons));
            Kind = kind;
            Title = title ?? "";
            Message = message ?? "";
            Buttons = buttons;
            IsSkippable = isSkippable;
            Tag = tag;
        }

        public DialogButton? CancelButton
        {
            get
            {
                foreach (var b in Buttons)
                    if (b.Role == DialogButtonRole.Cancel) return b;
                return null;
            }
        }

        /// <summary>Az első Primary vagy Destructive gomb; ennek hiányában az első nem-Cancel gomb.</summary>
        public DialogButton PrimaryButton
        {
            get
            {
                foreach (var b in Buttons)
                    if (b.Role == DialogButtonRole.Primary || b.Role == DialogButtonRole.Destructive) return b;
                foreach (var b in Buttons)
                    if (b.Role != DialogButtonRole.Cancel) return b;
                return Buttons[0];
            }
        }

        public bool HasButton(string id)
        {
            foreach (var b in Buttons)
                if (string.Equals(b.Id, id, StringComparison.Ordinal)) return true;
            return false;
        }

        public static DialogRequest Information(string title, string message, string okLabel, string? tag = null)
            => new DialogRequest(DialogKind.Information, title, message,
                new[] { new DialogButton(DialogButton.OkId, okLabel, DialogButtonRole.Primary) }, false, tag);

        public static DialogRequest Error(string title, string message, string okLabel, string? tag = null)
            => new DialogRequest(DialogKind.Error, title, message,
                new[] { new DialogButton(DialogButton.OkId, okLabel, DialogButtonRole.Primary) }, false, tag);

        public static DialogRequest Confirm(string title, string message, string confirmLabel, string cancelLabel,
            bool destructive = false, bool isSkippable = false, string? tag = null)
            => new DialogRequest(DialogKind.Confirm, title, message, new[]
            {
                new DialogButton(DialogButton.ConfirmId, confirmLabel, destructive ? DialogButtonRole.Destructive : DialogButtonRole.Primary),
                new DialogButton(DialogButton.CancelId, cancelLabel, DialogButtonRole.Cancel),
            }, isSkippable, tag);

        public static DialogRequest Warning(string title, string message, string confirmLabel, string cancelLabel,
            bool isSkippable = false, string? tag = null)
            => new DialogRequest(DialogKind.Warning, title, message, new[]
            {
                new DialogButton(DialogButton.ConfirmId, confirmLabel, DialogButtonRole.Destructive),
                new DialogButton(DialogButton.CancelId, cancelLabel, DialogButtonRole.Cancel),
            }, isSkippable, tag);
    }

    public readonly struct DialogResult
    {
        public string ButtonId { get; }

        /// <summary>A Cancel szerepű gombbal, ESC-kel vagy <see cref="DialogService.DismissAll"/>-lal zárult.</summary>
        public bool WasCancelled { get; }

        /// <summary>Kikapcsolt megerősítések miatt meg sem jelent.</summary>
        public bool WasAutoConfirmed { get; }

        public DialogResult(string buttonId, bool wasCancelled, bool wasAutoConfirmed)
        {
            ButtonId = buttonId ?? "";
            WasCancelled = wasCancelled;
            WasAutoConfirmed = wasAutoConfirmed;
        }

        public bool IsConfirmed => !WasCancelled && ButtonId != DialogButton.CancelId;
    }

    /// <summary>
    /// Egyetlen, újrahasználható modális dialógusrendszer (WF-UI-003). Egyszerre
    /// egy aktív, a többi sorban áll. ESC-kezelőként is regisztrálható: amíg van
    /// aktív dialógus, a Vissza-eseményt mindig elnyeli.
    /// Csak a főszálról használható.
    /// </summary>
    public sealed class DialogService : IBackHandler
    {
        private readonly Queue<Entry> _pending = new Queue<Entry>();
        private readonly Func<bool> _confirmationsEnabled;
        private Entry? _active;

        public DialogService(Func<bool>? confirmationsEnabled = null)
        {
            _confirmationsEnabled = confirmationsEnabled ?? (() => true);
        }

        public DialogRequest? Active => _active?.Request;

        public int PendingCount => _pending.Count;

        public bool IsOpen => _active != null;

        public event Action<DialogRequest?>? ActiveChanged;

        /// <summary>
        /// Megjelenít (vagy sorba állít) egy dialógust. Hamis, ha azonos
        /// <see cref="DialogRequest.Tag"/>-ű már aktív vagy sorban van.
        /// </summary>
        public bool Show(DialogRequest request, Action<DialogResult>? onClosed = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Tag != null && HasTag(request.Tag)) return false;

            if (request.IsSkippable && !_confirmationsEnabled())
            {
                onClosed?.Invoke(new DialogResult(request.PrimaryButton.Id, false, true));
                return true;
            }

            var entry = new Entry(request, onClosed);
            if (_active == null)
            {
                _active = entry;
                ActiveChanged?.Invoke(request);
            }
            else
            {
                _pending.Enqueue(entry);
            }
            return true;
        }

        /// <summary>Az aktív dialógus lezárása a megadott gombbal. Hamis, ha nincs ilyen gomb vagy dialógus.</summary>
        public bool Resolve(string buttonId)
        {
            if (_active == null || !_active.Request.HasButton(buttonId)) return false;
            var cancel = _active.Request.CancelButton;
            bool cancelled = cancel != null && string.Equals(cancel.Id, buttonId, StringComparison.Ordinal);
            Close(new DialogResult(buttonId, cancelled, false));
            return true;
        }

        /// <summary>
        /// ESC-szerű lezárás: a Cancel gombbal; egygombos dialógusnál (Information,
        /// Error) azzal az egy gombbal. Hamis, ha a dialógus nem zárható így.
        /// </summary>
        public bool CancelActive()
        {
            if (_active == null) return false;
            var cancel = _active.Request.CancelButton;
            if (cancel != null)
            {
                Close(new DialogResult(cancel.Id, true, false));
                return true;
            }
            if (_active.Request.Buttons.Count == 1)
            {
                Close(new DialogResult(_active.Request.Buttons[0].Id, false, false));
                return true;
            }
            return false;
        }

        public bool TryHandleBack()
        {
            if (_active == null) return false;
            CancelActive();
            return true;
        }

        /// <summary>Minden aktív és sorban álló dialógus elvetése (pl. session-váltáskor); a visszahívások megszakítottként kapják.</summary>
        public void DismissAll()
        {
            var all = new List<Entry>();
            if (_active != null) all.Add(_active);
            all.AddRange(_pending);
            _pending.Clear();
            bool hadActive = _active != null;
            _active = null;
            if (hadActive) ActiveChanged?.Invoke(null);
            foreach (var e in all)
            {
                string id = e.Request.CancelButton?.Id ?? "";
                e.OnClosed?.Invoke(new DialogResult(id, true, false));
            }
        }

        private void Close(DialogResult result)
        {
            var closed = _active!;
            _active = _pending.Count > 0 ? _pending.Dequeue() : null;
            ActiveChanged?.Invoke(_active?.Request);
            closed.OnClosed?.Invoke(result);
        }

        private bool HasTag(string tag)
        {
            if (_active != null && string.Equals(_active.Request.Tag, tag, StringComparison.Ordinal)) return true;
            foreach (var e in _pending)
                if (string.Equals(e.Request.Tag, tag, StringComparison.Ordinal)) return true;
            return false;
        }

        private sealed class Entry
        {
            public Entry(DialogRequest request, Action<DialogResult>? onClosed)
            {
                Request = request;
                OnClosed = onClosed;
            }

            public DialogRequest Request { get; }
            public Action<DialogResult>? OnClosed { get; }
        }
    }

    /// <summary>Az alkalmazás visszatérő dialógusai, lokalizált szöveggel.</summary>
    public static class CommonDialogs
    {
        public const string UnsavedProgressTag = "unsaved-progress";
        public const string QuitTag = "quit-to-desktop";

        public static DialogRequest UnsavedProgress(LocalizationTable text)
            => DialogRequest.Confirm(text.Get("dialog.unsaved.title"), text.Get("dialog.unsaved.message"),
                text.Get("dialog.button.continue"), text.Get("dialog.button.cancel"),
                destructive: true, isSkippable: true, tag: UnsavedProgressTag);

        public static DialogRequest QuitToDesktop(LocalizationTable text, bool hasUnsavedProgress)
            => DialogRequest.Confirm(text.Get("dialog.quit.title"),
                text.Get(hasUnsavedProgress ? "dialog.unsaved.message" : "dialog.quit.message"),
                text.Get("dialog.button.quit"), text.Get("dialog.button.cancel"),
                destructive: hasUnsavedProgress, isSkippable: true, tag: QuitTag);

        /// <summary>Visszafordíthatatlan: sosem hagyható ki.</summary>
        public static DialogRequest DeleteSave(LocalizationTable text, string worldName)
            => DialogRequest.Warning(text.Get("dialog.deleteSave.title"), text.Format("dialog.deleteSave.message", worldName),
                text.Get("dialog.button.delete"), text.Get("dialog.button.cancel"), isSkippable: false);

        public static DialogRequest SaveFailed(LocalizationTable text, string userMessage)
            => DialogRequest.Error(text.Get("dialog.saveFailed.title"), text.Format("dialog.saveFailed.message", userMessage),
                text.Get("dialog.button.ok"));

        public static DialogRequest LoadFailed(LocalizationTable text, string userMessage)
            => DialogRequest.Error(text.Get("dialog.loadFailed.title"), text.Format("dialog.loadFailed.message", userMessage),
                text.Get("dialog.button.ok"));
    }
}
