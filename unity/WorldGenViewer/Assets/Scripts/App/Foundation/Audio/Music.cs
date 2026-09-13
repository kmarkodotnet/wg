#nullable enable
using System;
using System.Collections.Generic;

namespace WorldGen.App.Audio
{
    public enum FadeCurve
    {
        Linear,

        /// <summary>sin/cos görbe: két réteg átfedésekor az összteljesítmény állandó.</summary>
        EqualPower,
    }

    /// <summary>Időben lefutó hangerő-átmenet (WF-AUDIO-002).</summary>
    public sealed class FadeEnvelope
    {
        private double _elapsed;

        public double From { get; }
        public double To { get; }
        public double DurationSeconds { get; }
        public FadeCurve Curve { get; }

        public FadeEnvelope(double from, double to, double durationSeconds, FadeCurve curve = FadeCurve.Linear)
        {
            if (!double.IsFinite(from) || !double.IsFinite(to)) throw new ArgumentException("A hangerő nem lehet NaN vagy végtelen.");
            if (!double.IsFinite(durationSeconds) || durationSeconds < 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            From = from;
            To = to;
            DurationSeconds = durationSeconds;
            Curve = curve;
        }

        public double Progress => DurationSeconds <= 0 ? 1.0 : Math.Min(1.0, _elapsed / DurationSeconds);
        public bool IsComplete => Progress >= 1.0;
        public double Value => Evaluate(From, To, Progress, Curve);

        public double Tick(double unscaledDeltaSeconds)
        {
            if (double.IsFinite(unscaledDeltaSeconds) && unscaledDeltaSeconds > 0) _elapsed += unscaledDeltaSeconds;
            return Value;
        }

        public static double Evaluate(double from, double to, double t, FadeCurve curve)
        {
            if (double.IsNaN(t) || t <= 0) return from;
            if (t >= 1) return to;
            if (curve == FadeCurve.Linear) return from + (to - from) * t;
            return to >= from
                ? from + (to - from) * Math.Sin(t * Math.PI / 2)
                : to + (from - to) * Math.Cos(t * Math.PI / 2);
        }
    }

    /// <summary>Zenei állapotok (WF-AUDIO-005); a dinamikus váltást később a Core-kötés kéri.</summary>
    public enum MusicState
    {
        None,
        MainMenu,
        Space,
        Geological,
        Ocean,
        Life,
        ComplexLife,
        Catastrophe,
    }

    public interface IMusicPlaylist
    {
        /// <summary>A következő zeneszám az adott állapothoz; null, ha nincs.</summary>
        string? NextTrack(MusicState state, string? previousTrack);
    }

    /// <summary>Állapotonként rögzített sorrendű lista, körbeforduló.</summary>
    public sealed class SequentialPlaylist : IMusicPlaylist
    {
        private readonly Dictionary<MusicState, string[]> _tracks = new Dictionary<MusicState, string[]>();

        public void Set(MusicState state, params string[] trackIds)
        {
            if (trackIds == null) throw new ArgumentNullException(nameof(trackIds));
            foreach (string t in trackIds)
                if (string.IsNullOrEmpty(t)) throw new ArgumentException("Üres zeneszám-azonosító.", nameof(trackIds));
            _tracks[state] = (string[])trackIds.Clone();
        }

        public string? NextTrack(MusicState state, string? previousTrack)
        {
            if (!_tracks.TryGetValue(state, out var list) || list.Length == 0) return null;
            if (previousTrack == null) return list[0];
            int index = Array.IndexOf(list, previousTrack);
            return list[(index + 1) % list.Length];
        }
    }

    public sealed class MusicLayer
    {
        internal MusicLayer(MusicState state, string trackId, FadeEnvelope envelope)
        {
            State = state;
            TrackId = trackId;
            Envelope = envelope;
        }

        public MusicState State { get; }
        public string TrackId { get; }
        public double Gain => Envelope.Value;
        internal FadeEnvelope Envelope { get; set; }
    }

    /// <summary>
    /// Kétrétegű zenei crossfade (WF-AUDIO-002/005). A Foundation csak a
    /// rétegeket és a hangerőt vezeti; a Unity-kötés az eseményekre indít
    /// és állít le AudioSource-okat, és minden frame-ben a <see cref="MusicLayer.Gain"/>-t alkalmazza.
    /// </summary>
    public sealed class MusicDirector
    {
        private readonly IMusicPlaylist _playlist;

        public double CrossfadeSeconds { get; }
        public MusicState State { get; private set; } = MusicState.None;
        public MusicLayer? Current { get; private set; }
        public MusicLayer? Outgoing { get; private set; }

        public event Action<MusicLayer>? TrackStarted;
        public event Action<MusicLayer>? TrackStopped;

        public MusicDirector(IMusicPlaylist playlist, double crossfadeSeconds = 3.0)
        {
            _playlist = playlist ?? throw new ArgumentNullException(nameof(playlist));
            if (!double.IsFinite(crossfadeSeconds) || crossfadeSeconds < 0) throw new ArgumentOutOfRangeException(nameof(crossfadeSeconds));
            CrossfadeSeconds = crossfadeSeconds;
        }

        public void RequestState(MusicState state, bool immediate = false)
        {
            if (state == State) return;
            State = state;
            string? next = state == MusicState.None ? null : _playlist.NextTrack(state, null);
            SwitchTo(state, next, immediate);
        }

        /// <summary>Az aktuális szám természetes vége: a lista következő száma crossfade-del (egyszámos listánál a kötés loopol).</summary>
        public void NotifyTrackFinished()
        {
            if (Current == null) return;
            string? next = _playlist.NextTrack(Current.State, Current.TrackId);
            if (next == null || string.Equals(next, Current.TrackId, StringComparison.Ordinal)) return;
            SwitchTo(Current.State, next, immediate: false);
        }

        public void Tick(double unscaledDeltaSeconds)
        {
            Current?.Envelope.Tick(unscaledDeltaSeconds);
            if (Outgoing == null) return;
            Outgoing.Envelope.Tick(unscaledDeltaSeconds);
            if (Outgoing.Envelope.IsComplete) StopOutgoing();
        }

        private void SwitchTo(MusicState state, string? trackId, bool immediate)
        {
            double duration = immediate ? 0 : CrossfadeSeconds;
            if (Outgoing != null) StopOutgoing();
            if (Current != null)
            {
                Outgoing = Current;
                Outgoing.Envelope = new FadeEnvelope(Outgoing.Gain, 0, duration, FadeCurve.EqualPower);
                Current = null;
                if (duration <= 0) StopOutgoing();
            }
            if (trackId == null) return;
            Current = new MusicLayer(state, trackId, new FadeEnvelope(0, 1, duration, FadeCurve.EqualPower));
            TrackStarted?.Invoke(Current);
        }

        private void StopOutgoing()
        {
            var layer = Outgoing;
            Outgoing = null;
            if (layer != null) TrackStopped?.Invoke(layer);
        }
    }
}
