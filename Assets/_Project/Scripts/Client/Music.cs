using System.Collections.Generic;
using UnityEngine;

namespace Game.Client
{
    // Plays the background music (Art/MusicArt.cs): one tune at a time, looping, fading over when the screen calls
    // for another. Tunes are made the first time they are needed. The title sets the volume (kept on the device).
    static class Music
    {
        const string VolumeKey = "music_volume";
        const float FadeSeconds = 0.8f;
        static readonly Dictionary<Tune, AudioClip> Clips = new Dictionary<Tune, AudioClip>();
        static AudioSource _source;
        static AudioSource _ambientSource;
        static readonly Dictionary<Ambience, AudioClip> Ambiences = new Dictionary<Ambience, AudioClip>();
        static Ambience _ambient = Ambience.None;
        static Ambience _nextAmbient = Ambience.None;
        static Tune _playing = Tune.None;
        static Tune _next = Tune.None;
        static float _fade = 1;

        // 0 to 100, in the title's steps.
        public static int Volume
        {
            get => PlayerPrefs.GetInt(VolumeKey, 50);
            set
            {
                PlayerPrefs.SetInt(VolumeKey, Mathf.Clamp(value, 0, 100));
                PlayerPrefs.Save();
            }
        }

        // Asks for a tune; the change happens over the next frames.
        public static void Play(Tune tune) => _next = tune;

        // Asks for the place's sound under the music (None for none).
        public static void Ambient(Ambience ambience) => _nextAmbient = ambience;

        public static void Tick(float dt)
        {
            if (Volume <= 0 || Sounds.Muted)
            {
                if (_source != null && _source.isPlaying) _source.Stop();
                if (_ambientSource != null && _ambientSource.isPlaying) _ambientSource.Stop();
                _playing = Tune.None;
                _ambient = Ambience.None;
                return;
            }
            TickAmbient();
            if (_source == null)
            {
                var go = new GameObject("Music");
                Object.DontDestroyOnLoad(go);
                _source = go.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.loop = true;
            }
            if (_next != _playing)
            {
                // Fade the old tune out, then start the new one.
                _fade -= dt / FadeSeconds;
                if (_fade <= 0 || !_source.isPlaying)
                {
                    _fade = 0;
                    _playing = _next;
                    if (_playing == Tune.None) _source.Stop();
                    else
                    {
                        _source.clip = Clip(_playing);
                        _source.Play();
                    }
                }
            }
            else if (_fade < 1) _fade = Mathf.Min(1, _fade + dt / FadeSeconds);
            _source.volume = Volume / 100f * 0.6f * _fade;
        }

        static void TickAmbient()
        {
            if (_ambientSource == null)
            {
                var go = new GameObject("Ambience");
                Object.DontDestroyOnLoad(go);
                _ambientSource = go.AddComponent<AudioSource>();
                _ambientSource.playOnAwake = false;
                _ambientSource.loop = true;
            }
            if (_nextAmbient != _ambient)
            {
                _ambient = _nextAmbient;
                if (_ambient == Ambience.None) _ambientSource.Stop();
                else
                {
                    if (!Ambiences.TryGetValue(_ambient, out var clip) || clip == null)
                    {
                        float[] samples = MusicArt.MakeAmbience(_ambient);
                        clip = AudioClip.Create("ambience_" + _ambient, samples.Length, 1, MusicArt.Rate, false);
                        clip.SetData(samples, 0);
                        Ambiences[_ambient] = clip;
                    }
                    _ambientSource.clip = clip;
                    _ambientSource.Play();
                }
            }
            _ambientSource.volume = Volume / 100f * 0.35f;
        }

        static AudioClip Clip(Tune tune)
        {
            if (Clips.TryGetValue(tune, out var clip) && clip != null) return clip;
            float[] samples = MusicArt.Make(tune);
            clip = AudioClip.Create("music_" + tune, samples.Length, 1, MusicArt.Rate, false);
            clip.SetData(samples, 0);
            Clips[tune] = clip;
            return clip;
        }
    }
}
