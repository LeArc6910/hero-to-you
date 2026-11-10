using UnityEngine;

namespace Game.Client
{
    // The player's settings, kept on the device (PlayerPrefs): text size, vibration, the battle playback, the
    // language. Volumes live with Sounds and Music. The settings screen is GameRoot.Menu.cs.
    static class Settings
    {
        const string TextKey = "text_size";
        const string VibrationKey = "vibration";
        const string LanguageKey = "language";

        // How big everything is drawn, in percent of the usual size (Ui.Begin multiplies the zoom by it).
        public static readonly int[] TextSizes = { 90, 100, 115, 130 };

        public static int TextSize
        {
            get => PlayerPrefs.GetInt(TextKey, 100);
            set
            {
                PlayerPrefs.SetInt(TextKey, value);
                PlayerPrefs.Save();
            }
        }

        public static bool Vibration
        {
            get => PlayerPrefs.GetInt(VibrationKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(VibrationKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        // "ko" or "en" (Strings.cs). Only the menus, settings and tips have English yet.
        public static string Language
        {
            get => PlayerPrefs.GetString(LanguageKey, "ko");
            set
            {
                PlayerPrefs.SetString(LanguageKey, value);
                PlayerPrefs.Save();
            }
        }

        public static int NextTextSize()
        {
            int i = System.Array.IndexOf(TextSizes, TextSize);
            return TextSizes[(i + 1) % TextSizes.Length];
        }
    }
}
