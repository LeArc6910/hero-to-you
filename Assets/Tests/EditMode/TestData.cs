using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.Core.Tests
{
    // The game's real sheets, read from Assets/_Project/Resources/Data whether the tests run under dotnet or in Unity.
    static class TestData
    {
        static GameData _data;

        public static GameData Data => _data ??= GameData.Load(path => File.ReadAllText(Path.Combine(DataFolder, path)));

        public static string DataFolder
        {
            get
            {
                for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, "Assets", "_Project", "Resources", "Data");
                    if (Directory.Exists(candidate)) return candidate;
                }
                string unity = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "_Project", "Resources", "Data");
                if (Directory.Exists(unity)) return unity;
                throw new DirectoryNotFoundException("Assets/_Project/Resources/Data not found above " + AppContext.BaseDirectory);
            }
        }

        public static Hero Hero(string classId) => new Hero(Data.Classes[classId]);

        public static List<Hero> Party(params string[] classIds) => classIds.Select(Hero).ToList();

        public static List<MonsterDef> Monsters(params string[] ids) => ids.Select(id => Data.Monsters[id]).ToList();

        // A monster that does nothing worth noting: lots of health, slow, and it only howls.
        public static MonsterDef Dummy(int hp = 500, int speed = 1, int armor = 0)
        {
            return new MonsterDef
            {
                Id = "dummy",
                Name = "허수아비",
                Hp = hp,
                Speed = speed,
                Accuracy = 50,
                Armor = armor,
                Pattern = new List<MonsterSkillDef> { Data.MonsterSkills["wolf_howl"] },
                Color = "#FFFFFF",
                Size = 1,
            };
        }
    }
}
