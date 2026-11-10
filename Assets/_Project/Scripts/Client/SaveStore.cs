using System;
using System.IO;
using UnityEngine;

namespace Game.Client
{
    // The adventures in progress, as files on the device (Docs/Saving.md 저장 슬롯): slots 1 to 3, and slot 0 for the
    // practice run. Slot 1 keeps the old single save's name, so a save from before the slots is slot 1. In the
    // browser, persistentDataPath lives in the site's IndexedDB; the page carries every write there
    // (autoSyncPersistentDataPath in the web template).
    static class SaveStore
    {
        public const int Slots = 3;
        public const int PracticeSlot = 0;
        const string SlotKey = "save_slot";

        // The slot the adventure on screen saves to (and the title's continue opens), kept on the device.
        public static int Slot
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(SlotKey, 1), 0, Slots);
            set
            {
                PlayerPrefs.SetInt(SlotKey, Mathf.Clamp(value, 0, Slots));
                PlayerPrefs.Save();
            }
        }

        static string FileOf(int slot) => slot == PracticeSlot ? "practice.txt" : slot == 1 ? "adventure.txt" : $"adventure{slot}.txt";

        static string PathOf(int slot) => Path.Combine(Application.persistentDataPath, FileOf(slot));

        public static bool Exists => ExistsIn(Slot);

        public static bool ExistsIn(int slot)
        {
            try
            {
                return File.Exists(PathOf(slot));
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Any slot with an adventure in it (the title shows the slots then).
        public static bool Any
        {
            get
            {
                for (int slot = 0; slot <= Slots; slot++)
                    if (ExistsIn(slot)) return true;
                return false;
            }
        }

        public static string Read() => ReadSlot(Slot);

        public static string ReadSlot(int slot)
        {
            try
            {
                string path = PathOf(slot);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not read the save: " + e.Message);
                return null;
            }
        }

        public static void Write(string text)
        {
            try
            {
                File.WriteAllText(PathOf(Slot), text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not save: " + e.Message);
            }
        }

        public static void Delete() => DeleteSlot(Slot);

        public static void DeleteSlot(int slot)
        {
            try
            {
                string path = PathOf(slot);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not delete the save: " + e.Message);
            }
        }

        // The journal that outlives the runs (Core Journal, Docs/Journal.md), next to the save.
        const string JournalName = "journal.txt";

        static string JournalPath => Path.Combine(Application.persistentDataPath, JournalName);

        public static Game.Core.Journal ReadJournal()
        {
            try
            {
                return Game.Core.Journal.Parse(File.Exists(JournalPath) ? File.ReadAllText(JournalPath) : null);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not read the journal: " + e.Message);
                return new Game.Core.Journal();
            }
        }

        public static void WriteJournal(Game.Core.Journal journal)
        {
            try
            {
                File.WriteAllText(JournalPath, journal.ToText());
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not write the journal: " + e.Message);
            }
        }

        // "기사 · 사냥꾼 · 성직자 · 3장 · 진행 120" for a slot, from the save's header and its chapter lines.
        public static string Summary(Game.Core.GameData data, int slot)
        {
            string text = ReadSlot(slot);
            if (string.IsNullOrEmpty(text)) return null;
            string party = "";
            int commands = 0;
            int first = 0;
            foreach (string line in text.Replace("\r", "").Split('\n'))
            {
                if (line.StartsWith("party "))
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (string id in line.Substring(6).Split(',')) names.Add(data.Classes.TryGetValue(id, out var cls) ? cls.Name : id);
                    party = string.Join(" · ", names);
                }
                else if (line.StartsWith("chapters ")) int.TryParse(line.Split(' ')[1], out first);
                else if (line.Length > 0 && !line.StartsWith("seed ") && !line.StartsWith("sheets ") && !line.StartsWith("perks") && !line.StartsWith("level ")
                         && line != "hardcore" && !line.StartsWith(Game.Core.Adventure.SaveHeader)) commands++;
            }
            string from = first > 0 ? " · " + Strings.Format("slots.from", first + 1) : "";
            return $"{party}{from} · {Strings.Format("slots.progress", commands)}";
        }
    }
}
