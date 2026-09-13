#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using WorldGen.App.Serialization;

namespace WorldGen.App.UI
{
    /// <summary>Tooltip-tartalom: cím, rövid leírás, opcionális részletes magyarázat (WF-UI-005).</summary>
    public sealed class TooltipContent
    {
        public string Title { get; }
        public string Summary { get; }
        public string? Details { get; }

        public bool HasDetails => !string.IsNullOrEmpty(Details);

        public TooltipContent(string title, string summary, string? details = null)
        {
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Üres tooltip-cím.", nameof(title));
            Title = title;
            Summary = summary ?? "";
            Details = string.IsNullOrEmpty(details) ? null : details;
        }
    }

    public sealed class TooltipCatalog
    {
        private readonly Dictionary<string, TooltipContent> _entries = new Dictionary<string, TooltipContent>(StringComparer.Ordinal);

        public int Count => _entries.Count;

        public IEnumerable<string> Keys => _entries.Keys;

        public void Add(string key, TooltipContent content)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Üres tooltip-kulcs.", nameof(key));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (_entries.ContainsKey(key)) throw new InvalidOperationException("Duplikált tooltip-kulcs: " + key);
            _entries.Add(key, content);
        }

        public bool TryGet(string key, [NotNullWhen(true)] out TooltipContent? content)
        {
            content = null;
            return key != null && _entries.TryGetValue(key, out content);
        }

        /// <summary>
        /// Betöltés: <c>{ "kulcs": { "title": "...", "summary": "...", "details": "..." } }</c>.
        /// A hibás bejegyzések kimaradnak; kulcsuk a <paramref name="rejectedKeys"/>-be kerül.
        /// </summary>
        public static TooltipCatalog FromJson(JsonValue document, out IReadOnlyList<string> rejectedKeys)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var catalog = new TooltipCatalog();
            var rejected = new List<string>();
            rejectedKeys = rejected;
            if (document.Kind != JsonValueKind.Object)
            {
                rejected.Add("$");
                return catalog;
            }
            foreach (var member in document.Members)
            {
                var node = member.Value;
                if (node.Kind == JsonValueKind.Object
                    && node.GetMember("title") is JsonValue t && t.TryGetString(out string? title) && !string.IsNullOrWhiteSpace(title))
                {
                    string summary = node.GetMember("summary") is JsonValue s && s.TryGetString(out string? sv) ? sv : "";
                    string? details = node.GetMember("details") is JsonValue d && d.TryGetString(out string? dv) ? dv : null;
                    catalog.Add(member.Key, new TooltipContent(title, summary, details));
                }
                else
                {
                    rejected.Add(member.Key);
                }
            }
            return catalog;
        }
    }

    public enum TooltipVisibility
    {
        Hidden,
        Summary,
        Detailed,
    }

    /// <summary>
    /// Hover-időzítés: a késleltetés után rövid tooltip, további ideig tartó
    /// hover (vagy <see cref="ExpandDetails"/>) után részletes. Ha a kurzor
    /// látható tooltipról rövid időn belül másik elemre lép, az új tooltip
    /// késleltetés nélkül jelenik meg („meleg” átadás).
    /// </summary>
    public sealed class TooltipHoverTracker
    {
        public const double WarmHandoffSeconds = 0.3;

        private double _hoverSeconds;
        private double _warmSeconds;
        private double _showDelaySeconds;

        public string? TargetKey { get; private set; }
        public TooltipVisibility Visibility { get; private set; }

        public double DetailDelaySeconds { get; }

        public event Action<TooltipHoverTracker>? Changed;

        public TooltipHoverTracker(double showDelaySeconds, double detailDelaySeconds = 1.5)
        {
            ShowDelaySeconds = showDelaySeconds;
            if (!double.IsFinite(detailDelaySeconds) || detailDelaySeconds < 0) throw new ArgumentOutOfRangeException(nameof(detailDelaySeconds));
            DetailDelaySeconds = detailDelaySeconds;
        }

        /// <summary>A beállításból (<c>InterfaceSettings.TooltipDelaySeconds</c>).</summary>
        public double ShowDelaySeconds
        {
            get => _showDelaySeconds;
            set
            {
                if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                _showDelaySeconds = value;
            }
        }

        public void SetTarget(string? key)
        {
            if (string.Equals(key, TargetKey, StringComparison.Ordinal)) return;
            bool wasVisible = Visibility != TooltipVisibility.Hidden;
            TargetKey = key;
            _hoverSeconds = 0;

            if (key == null)
            {
                if (wasVisible) _warmSeconds = WarmHandoffSeconds;
                SetVisibility(TooltipVisibility.Hidden, force: wasVisible);
                return;
            }

            bool warm = wasVisible || _warmSeconds > 0;
            _warmSeconds = 0;
            SetVisibility(warm || _showDelaySeconds <= 0 ? TooltipVisibility.Summary : TooltipVisibility.Hidden, force: true);
        }

        public void Tick(double unscaledDeltaSeconds)
        {
            if (!double.IsFinite(unscaledDeltaSeconds) || unscaledDeltaSeconds <= 0) return;
            if (TargetKey == null)
            {
                _warmSeconds = Math.Max(0, _warmSeconds - unscaledDeltaSeconds);
                return;
            }
            _hoverSeconds += unscaledDeltaSeconds;
            if (Visibility == TooltipVisibility.Hidden && _hoverSeconds >= _showDelaySeconds)
            {
                _hoverSeconds = 0;
                SetVisibility(TooltipVisibility.Summary, force: false);
            }
            else if (Visibility == TooltipVisibility.Summary && _hoverSeconds >= DetailDelaySeconds)
            {
                SetVisibility(TooltipVisibility.Detailed, force: false);
            }
        }

        /// <summary>Részletes nézet kérése (pl. Shift vagy F1 a látható tooltipon).</summary>
        public void ExpandDetails()
        {
            if (TargetKey == null || Visibility == TooltipVisibility.Hidden) return;
            SetVisibility(TooltipVisibility.Detailed, force: false);
        }

        private void SetVisibility(TooltipVisibility visibility, bool force)
        {
            if (Visibility == visibility && !force) return;
            Visibility = visibility;
            Changed?.Invoke(this);
        }
    }
}
