using System;
using System.IO;
using UnityEngine;

namespace Game.Client
{
    // The adventure in progress, as a file on the device (Docs/Saving.md). In the browser, persistentDataPath lives
    // in the site's IndexedDB; the page carries every write there (autoSyncPersistentDataPath in the web template).
    static class SaveStore
    {
        const string FileName = "adventure.txt";

        static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static bool Exists
        {
            get
            {
                try
                {
                    return File.Exists(FilePath);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static string Read()
        {
            try
            {
                return File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
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
                File.WriteAllText(FilePath, text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not save: " + e.Message);
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not delete the save: " + e.Message);
            }
        }

        // "기사 · 사냥꾼 · 성직자, 명령 120개" for the title's continue button, from the save's header.
        public static string Summary(Game.Core.GameData data)
        {
            string text = Read();
            if (string.IsNullOrEmpty(text)) return null;
            string party = "";
            int commands = 0;
            foreach (string line in text.Replace("\r", "").Split('\n'))
            {
                if (line.StartsWith("party "))
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (string id in line.Substring(6).Split(',')) names.Add(data.Classes.TryGetValue(id, out var cls) ? cls.Name : id);
                    party = string.Join(" · ", names);
                }
                else if (line.Length > 0 && !line.StartsWith("seed ") && !line.StartsWith("sheets ") && !line.StartsWith(Game.Core.Adventure.SaveHeader)) commands++;
            }
            return $"{party} · 진행 {commands}";
        }
    }
}
