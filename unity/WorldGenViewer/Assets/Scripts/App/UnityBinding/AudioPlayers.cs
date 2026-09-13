#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using WorldGen.App.Audio;

namespace WorldGen.App.UnityBinding
{
    /// <summary>Azonosító → AudioClip; a zene- és UI-hangfájlok helye (licencelt assetek, WF-AUDIO-002/004).</summary>
    [CreateAssetMenu(menuName = "WorldGen/Audio Clip Library", fileName = "AudioClipLibrary")]
    public sealed class AudioClipLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string id;
            public AudioClip clip;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public AudioClip? Find(string id)
        {
            foreach (var e in entries)
                if (e.clip != null && string.Equals(e.id, id, StringComparison.Ordinal)) return e.clip;
            return null;
        }
    }

    /// <summary>
    /// A <see cref="MusicDirector"/> két rétegét két AudioSource-szal játssza le.
    /// A lejátszás unscaled idővel halad (pause alatt is szól a zene).
    /// </summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        [SerializeField] private AudioClipLibrary? library = null;
        [SerializeField] private AudioMixerGroup? output = null;

        private readonly Dictionary<MusicLayer, AudioSource> _sources = new Dictionary<MusicLayer, AudioSource>();
        private readonly Stack<AudioSource> _free = new Stack<AudioSource>();
        private MusicDirector? _director;

        public void Initialize(MusicDirector director, AudioClipLibrary? clipLibrary = null)
        {
            if (_director != null) throw new InvalidOperationException("A MusicPlayer már inicializálva van.");
            _director = director ?? throw new ArgumentNullException(nameof(director));
            if (clipLibrary != null) library = clipLibrary;
            for (int i = 0; i < 2; i++) _free.Push(CreateSource());
            _director.TrackStarted += OnTrackStarted;
            _director.TrackStopped += OnTrackStopped;
        }

        private void Update()
        {
            if (_director == null) return;
            _director.Tick(Time.unscaledDeltaTime);
            foreach (var pair in _sources) pair.Value.volume = (float)pair.Key.Gain;

            var current = _director.Current;
            if (current != null && _sources.TryGetValue(current, out var source) && source.clip != null
                && !source.isPlaying && source.time <= 0f)
            {
                _director.NotifyTrackFinished();
                if (_director.Current == current) source.Play(); // egyszámos lista: újrakezdés
            }
        }

        private void OnDestroy()
        {
            if (_director == null) return;
            _director.TrackStarted -= OnTrackStarted;
            _director.TrackStopped -= OnTrackStopped;
        }

        private void OnTrackStarted(MusicLayer layer)
        {
            var clip = library != null ? library.Find(layer.TrackId) : null;
            if (clip == null) return;
            var source = _free.Count > 0 ? _free.Pop() : CreateSource();
            source.clip = clip;
            source.volume = (float)layer.Gain;
            source.Play();
            _sources[layer] = source;
        }

        private void OnTrackStopped(MusicLayer layer)
        {
            if (!_sources.TryGetValue(layer, out var source)) return;
            _sources.Remove(layer);
            source.Stop();
            source.clip = null;
            _free.Push(source);
        }

        private AudioSource CreateSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.outputAudioMixerGroup = output;
            return source;
        }
    }

    /// <summary>UI-hangok ritkítva (WF-AUDIO-004). Klip-azonosító: "ui.hover", "ui.click", "ui.back", "ui.confirm", "ui.error".</summary>
    public sealed class UiSoundPlayer : MonoBehaviour
    {
        [SerializeField] private AudioClipLibrary? library = null;
        [SerializeField] private AudioMixerGroup? output = null;

        private readonly UiSoundThrottle _throttle = new UiSoundThrottle();
        private AudioSource? _source;

        public static string ClipId(UiSoundEvent sound) => "ui." + sound.ToString().ToLowerInvariant();

        public void Play(UiSoundEvent sound)
        {
            if (library == null || !_throttle.TryPlay(sound, Time.unscaledTimeAsDouble)) return;
            var clip = library.Find(ClipId(sound));
            if (clip == null) return;
            if (_source == null)
            {
                _source = gameObject.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.ignoreListenerPause = true;
                _source.outputAudioMixerGroup = output;
            }
            _source.PlayOneShot(clip);
        }
    }
}
