using System.Collections.Generic;
using UnityEngine;

namespace Game.Client
{
    // First-time tips (Docs/Design.md 튜토리얼): the first time a screen comes up, a short card says what to do on it.
    // Each is shown once on this device (PlayerPrefs); the settings' [도움말 다시 보기] brings them back, and the help
    // spots on the screens (the time, the rift, renown) open theirs again. The words are in the strings sheet
    // (tip.<id>.title, tip.<id>.text), so they can be translated like the menus.
    static class Tips
    {
        const string SeenKey = "tips_seen";

        public static readonly string[] Ids = { "map", "join", "battle", "event", "town", "levelup", "time", "rift", "renown", "journal", "dungeon", "wounds", "talk" };

        public static bool Exists(string id) => System.Array.IndexOf(Ids, id) >= 0;

        public static (string title, string text) Of(string id) => (Strings.Get($"tip.{id}.title"), Strings.Get($"tip.{id}.text"));

        static HashSet<string> _seen;

        static HashSet<string> Seen
        {
            get
            {
                if (_seen != null) return _seen;
                _seen = new HashSet<string>(PlayerPrefs.GetString(SeenKey, "").Split(','));
                return _seen;
            }
        }

        // Whether the tip still has to be shown once; marks it seen.
        public static bool FirstTime(string id)
        {
            if (!Exists(id) || Seen.Contains(id)) return false;
            Seen.Add(id);
            PlayerPrefs.SetString(SeenKey, string.Join(",", Seen));
            PlayerPrefs.Save();
            return true;
        }

        public static void Reset()
        {
            Seen.Clear();
            PlayerPrefs.DeleteKey(SeenKey);
            PlayerPrefs.Save();
        }

        // Playtests start with every tip seen, so the cards do not cover what they press.
        public static void MarkAllSeen()
        {
            foreach (string id in Ids) Seen.Add(id);
            PlayerPrefs.SetString(SeenKey, string.Join(",", Seen));
            PlayerPrefs.Save();
        }
    }
}
