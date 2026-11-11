using System.Collections.Generic;
using UnityEngine;

namespace Game.Client
{
    // Sound files the game ships (Resources/Audio, free sounds and music: Docs/Sound.md): a clip by folder and name,
    // or null when there is none (the game then makes the sound in code, Art/SoundArt.cs and Art/MusicArt.cs). A
    // name may come in a few takes (Hit_1, Hit_2, …) so a sound heard often does not repeat itself exactly.
    static class AudioFiles
    {
        static readonly Dictionary<string, AudioClip[]> Found = new Dictionary<string, AudioClip[]>();
        static int _turn;

        public static AudioClip Get(string folder, string name)
        {
            string key = folder + "/" + name;
            if (!Found.TryGetValue(key, out var takes))
            {
                var list = new List<AudioClip>();
                var one = Resources.Load<AudioClip>("Audio/" + key);
                if (one != null) list.Add(one);
                for (int i = 1; i <= 4; i++)
                {
                    var take = Resources.Load<AudioClip>($"Audio/{key}_{i}");
                    if (take == null) break;
                    list.Add(take);
                }
                takes = list.ToArray();
                Found[key] = takes;
            }
            if (takes.Length == 0) return null;
            _turn++;
            return takes[_turn % takes.Length];
        }
    }

    // Plays the sound effects: the sound file when the game has one (AudioFiles), else the synthesized sound
    // (Art/SoundArt.cs). The title's switch turns them off; the choice is kept on the device.
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
            var file = AudioFiles.Get("Sfx", sound.ToString());
            if (file != null)
            {
                _source.PlayOneShot(file, 0.8f);
                return;
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
