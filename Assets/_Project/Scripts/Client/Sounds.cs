using System.Collections.Generic;
using UnityEngine;

namespace Game.Client
{
    // Plays the synthesized sound effects (Art/SoundArt.cs). The title's switch turns them off; the choice is kept on
    // the device.
    static class Sounds
    {
        const string MuteKey = "sound_off";
        static readonly Dictionary<Sound, AudioClip> Clips = new Dictionary<Sound, AudioClip>();
        static AudioSource _source;

        public static bool Muted
        {
            get => PlayerPrefs.GetInt(MuteKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(MuteKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static void Play(Sound sound)
        {
            if (Muted) return;
            if (_source == null)
            {
                var go = new GameObject("Sounds");
                Object.DontDestroyOnLoad(go);
                _source = go.AddComponent<AudioSource>();
                _source.playOnAwake = false;
            }
            if (!Clips.TryGetValue(sound, out var clip) || clip == null)
            {
                float[] samples = SoundArt.Make(sound);
                clip = AudioClip.Create(sound.ToString(), samples.Length, 1, SoundArt.Rate, false);
                clip.SetData(samples, 0);
                Clips[sound] = clip;
            }
            _source.PlayOneShot(clip, 0.8f);
        }
    }
}
