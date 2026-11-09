using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // Every sheet of Assets/_Project/Resources/Data, read and checked together. The client hands the sheets' text in from its
    // TextAssets (Client/GameSheets.cs), the tests and the sandbox from the files. A sheet with a mistake throws a
    // FormatException naming every bad row, so a broken sheet never starts a game.
    public sealed class GameData
    {
        public const string ClassesFile = "Classes/classes.csv";
        public const string SkillsFile = "Classes/skills.csv";
        public const string MonstersFile = "Monsters/monsters.csv";
        public const string MonsterSkillsFile = "Monsters/monster_skills.csv";
        public const string EncountersFile = "Story/encounters.csv";
        public const string PathFile = "Story/path.csv";
        public const string StoriesFile = "Story/stories.csv";
        public const string EventsFile = "Story/events.csv";
        public const string EventChoicesFile = "Story/event_choices.csv";

        public static readonly string[] Files =
        {
            ClassesFile, SkillsFile, MonstersFile, MonsterSkillsFile, EncountersFile, PathFile, StoriesFile, EventsFile,
            EventChoicesFile,
        };

        static readonly string[] OutcomeVerbs =
        {
            "gold", "heal", "heal_all", "hurt", "hurt_all", "resolve", "resolve_all", "bless_all", "potion", "hope", "xp",
            "weak_next", "flag", "battle",
        };

        static readonly string[] SkillEffects = { "", "burn", "chill", "lifesteal", "guard", "taunt", "mark", "bless", "lucky", "backfire" };
        static readonly string[] PerfectEffects = { "", "crit150", "crit200", "crit300", "stun", "armor1" };
        static readonly string[] Elements = { "", "fire", "frost", "lightning" };

        readonly List<string> _errors = new List<string>();

        GameData()
        {
        }

        public IReadOnlyList<ClassDef> ClassList { get; private set; } = new List<ClassDef>();
        public IReadOnlyDictionary<string, ClassDef> Classes { get; private set; } = new Dictionary<string, ClassDef>();
        public IReadOnlyDictionary<string, SkillDef> Skills { get; private set; } = new Dictionary<string, SkillDef>();
        public IReadOnlyDictionary<string, MonsterDef> Monsters { get; private set; } = new Dictionary<string, MonsterDef>();
        public IReadOnlyDictionary<string, MonsterSkillDef> MonsterSkills { get; private set; } = new Dictionary<string, MonsterSkillDef>();
        public IReadOnlyDictionary<string, EncounterDef> Encounters { get; private set; } = new Dictionary<string, EncounterDef>();
        public IReadOnlyList<PathNode> Path { get; private set; } = new List<PathNode>();
        public IReadOnlyDictionary<string, StoryDef> Stories { get; private set; } = new Dictionary<string, StoryDef>();
        public IReadOnlyList<EventCard> Events { get; private set; } = new List<EventCard>();

        public IEnumerable<ClassDef> PlayableClasses => ClassList.Where(c => c.Playable);

        // `read` gives a sheet's text for its path under Assets/_Project/Resources/Data (one of Files).
        public static GameData Load(Func<string, string> read)
        {
            var data = new GameData();
            var skills = data.ReadSkills(CsvTable.Read(read(SkillsFile)));
            data.Skills = skills;
            data.ReadClasses(CsvTable.Read(read(ClassesFile)), skills);
            var monsterSkills = data.ReadMonsterSkills(CsvTable.Read(read(MonsterSkillsFile)));
            data.MonsterSkills = monsterSkills;
            data.ReadMonsters(CsvTable.Read(read(MonstersFile)), monsterSkills);
            data.ReadEncounters(CsvTable.Read(read(EncountersFile)));
            data.ReadStories(CsvTable.Read(read(StoriesFile)));
            data.ReadEvents(CsvTable.Read(read(EventsFile)), CsvTable.Read(read(EventChoicesFile)));
            data.ReadPath(CsvTable.Read(read(PathFile)));
            if (data._errors.Count > 0) throw new FormatException("시트 오류:\n" + string.Join("\n", data._errors));
            return data;
        }

        void Error(string file, CsvRow row, string message) => _errors.Add($"{file} {row.Line}행: {message}");

        void RequireColumns(string file, CsvTable table, params string[] columns)
        {
            var missing = table.MissingColumns(columns);
            if (missing.Count > 0) _errors.Add($"{file}: 열이 없습니다: {string.Join(", ", missing)}");
        }

        int Number(string file, CsvRow row, string column)
        {
            try
            {
                return SheetValues.ParseNumber(row[column], column);
            }
            catch (FormatException e)
            {
                Error(file, row, e.Message);
                return 0;
            }
        }

        int Signed(string file, CsvRow row, string raw, string label)
        {
            try
            {
                return SheetValues.ParseSigned(raw, label);
            }
            catch (FormatException e)
            {
                Error(file, row, e.Message);
                return 0;
            }
        }

        bool Bool(string file, CsvRow row, string column)
        {
            try
            {
                return SheetValues.ParseBool(row[column], column);
            }
            catch (FormatException e)
            {
                Error(file, row, e.Message);
                return false;
            }
        }

        T Enum<T>(string file, CsvRow row, string column) where T : struct, Enum
        {
            string raw = row[column].Replace("_", "");
            try
            {
                return SheetValues.ParseEnum<T>(raw, column);
            }
            catch (FormatException e)
            {
                Error(file, row, e.Message);
                return default;
            }
        }

        string Id(string file, CsvRow row, ICollection<string> seen)
        {
            string id = row["id"];
            if (!SheetValues.IsValidId(id)) Error(file, row, $"id '{id}'는 영문·숫자·_로 씁니다.");
            else if (seen.Contains(id)) Error(file, row, $"id '{id}'가 두 번 나옵니다.");
            seen.Add(id);
            return id;
        }

        void Choice(string file, CsvRow row, string column, string[] allowed)
        {
            if (!allowed.Contains(row[column])) Error(file, row, $"{column} '{row[column]}'는 {string.Join(", ", allowed.Where(a => a != ""))} 가운데 하나여야 합니다.");
        }

        void Color(string file, CsvRow row)
        {
            if (!SheetValues.IsColor(row["color"])) Error(file, row, $"color '{row["color"]}'는 #RRGGBB로 씁니다.");
        }

        // stat column: an ability code, "fixed50" style, or empty for no check.
        (Stat? stat, int fixedChance) StatColumn(string file, CsvRow row, string raw, bool needed)
        {
            if (raw.Length == 0)
            {
                if (needed) Error(file, row, "주사위를 굴리는 줄에는 stat이 있어야 합니다.");
                return (null, 0);
            }
            if (raw.StartsWith("fixed"))
            {
                int chance = Signed(file, row, raw.Substring(5), "fixed 확률");
                if (chance < Dice.MinChance || chance > Dice.MaxChance) Error(file, row, $"고정 확률 {chance}는 {Dice.MinChance}~{Dice.MaxChance}여야 합니다.");
                return (null, chance);
            }
            if (Stats.TryParse(raw, out var stat)) return (stat, 0);
            Error(file, row, $"stat '{raw}'는 str, agi, int, wil, per, cha나 fixed50 같은 고정 확률입니다.");
            return (null, 0);
        }

        Dictionary<string, SkillDef> ReadSkills(CsvTable table)
        {
            const string file = SkillsFile;
            RequireColumns(file, table, "id", "class", "name", "stat", "dice", "power", "floor", "kind", "target", "delay", "cooldown", "element", "effect", "perfect", "text");
            var skills = new Dictionary<string, SkillDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                var skill = new SkillDef
                {
                    Id = Id(file, row, seen),
                    ClassId = row["class"],
                    Name = row["name"],
                    Dice = Number(file, row, "dice"),
                    Power = Number(file, row, "power"),
                    Floor = Number(file, row, "floor"),
                    Kind = Enum<SkillKind>(file, row, "kind"),
                    Target = Enum<SkillTarget>(file, row, "target"),
                    Delay = Number(file, row, "delay"),
                    Cooldown = Number(file, row, "cooldown"),
                    Element = row["element"],
                    Effect = row["effect"],
                    Perfect = row["perfect"],
                    Text = row["text"],
                };
                (skill.Stat, skill.FixedChance) = StatColumn(file, row, row["stat"], skill.Dice > 0);
                Choice(file, row, "element", Elements);
                Choice(file, row, "effect", SkillEffects);
                Choice(file, row, "perfect", PerfectEffects);
                if (skill.Floor > 100) Error(file, row, "floor는 0~100입니다.");
                if (skill.Delay < 20 || skill.Delay > 300) Error(file, row, "delay는 20~300(%)입니다.");
                if (skill.Damages && skill.Target != SkillTarget.Enemy && skill.Target != SkillTarget.AllEnemies)
                    Error(file, row, "공격 기술의 target은 enemy나 all_enemies입니다.");
                if (skill.Kind == SkillKind.Revive && skill.Target != SkillTarget.DownedAlly) Error(file, row, "revive의 target은 downed_ally입니다.");
                if (skill.Kind == SkillKind.Heal && skill.Target != SkillTarget.Ally && skill.Target != SkillTarget.Self)
                    Error(file, row, "heal의 target은 ally나 self입니다.");
                if (skill.Kind == SkillKind.Support && skill.Effect.Length == 0) Error(file, row, "support 기술에는 effect가 있어야 합니다.");
                if (skill.Id.Length > 0) skills[skill.Id] = skill;
            }
            return skills;
        }

        void ReadClasses(CsvTable table, Dictionary<string, SkillDef> skills)
        {
            const string file = ClassesFile;
            RequireColumns(file, table, "id", "name", "role", "hp", "armor", "resolve", "grit", "str", "agi", "int", "wil", "per", "cha", "skills", "color", "mechanic", "blurb", "playable");
            var list = new List<ClassDef>();
            var map = new Dictionary<string, ClassDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                var cls = new ClassDef
                {
                    Id = Id(file, row, seen),
                    Name = row["name"],
                    Role = row["role"],
                    Hp = Number(file, row, "hp"),
                    Armor = Number(file, row, "armor"),
                    Resolve = Number(file, row, "resolve"),
                    GritCap = Number(file, row, "grit"),
                    Mechanic = row["mechanic"],
                    Blurb = row["blurb"],
                    Color = row["color"],
                    Playable = Bool(file, row, "playable"),
                };
                for (int i = 0; i < Stats.Count; i++) cls.Stats[i] = Number(file, row, Stats.Code((Stat)i));
                Color(file, row);
                var classSkills = new List<SkillDef>();
                foreach (string id in row["skills"].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!skills.TryGetValue(id, out var skill)) Error(file, row, $"기술 '{id}'가 skills.csv에 없습니다.");
                    else if (skill.ClassId != cls.Id) Error(file, row, $"기술 '{id}'는 {skill.ClassId}의 기술입니다.");
                    else classSkills.Add(skill);
                }
                cls.Skills = classSkills;
                if (cls.Playable && (classSkills.Count == 0 || !classSkills[0].Damages || !classSkills[0].Rolls || classSkills[0].Cooldown > 0))
                    Error(file, row, "고를 수 있는 직업의 첫 기술은 대기 없는 기본 공격이어야 합니다.");
                if (cls.GritCap < 1) Error(file, row, "grit은 1 이상입니다.");
                list.Add(cls);
                if (cls.Id.Length > 0) map[cls.Id] = cls;
            }
            foreach (var skill in skills.Values)
                if (!map.ContainsKey(skill.ClassId)) _errors.Add($"{SkillsFile}: 기술 '{skill.Id}'의 직업 '{skill.ClassId}'가 없습니다.");
            ClassList = list;
            Classes = map;
        }

        Dictionary<string, MonsterSkillDef> ReadMonsterSkills(CsvTable table)
        {
            const string file = MonsterSkillsFile;
            RequireColumns(file, table, "id", "name", "dice", "power", "floor", "kind", "target", "aim", "delay", "effect", "text");
            var map = new Dictionary<string, MonsterSkillDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                var skill = new MonsterSkillDef
                {
                    Id = Id(file, row, seen),
                    Name = row["name"],
                    Dice = Number(file, row, "dice"),
                    Power = Number(file, row, "power"),
                    Floor = Number(file, row, "floor"),
                    Kind = row["kind"],
                    Target = Enum<MonsterTarget>(file, row, "target"),
                    Aim = Enum<MonsterAim>(file, row, "aim"),
                    Delay = Number(file, row, "delay"),
                    Effect = row["effect"],
                    Text = row["text"],
                };
                Choice(file, row, "kind", new[] { "physical", "magic", "buff" });
                Choice(file, row, "effect", new[] { "", "burn", "howl" });
                if (skill.Damages && skill.Dice == 0) Error(file, row, "공격에는 주사위가 있어야 합니다.");
                if (skill.Delay < 20 || skill.Delay > 300) Error(file, row, "delay는 20~300(%)입니다.");
                if (skill.Id.Length > 0) map[skill.Id] = skill;
            }
            return map;
        }

        List<MonsterSkillDef> Pattern(string file, CsvRow row, string column, Dictionary<string, MonsterSkillDef> skills)
        {
            var pattern = new List<MonsterSkillDef>();
            foreach (string id in row[column].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (skills.TryGetValue(id, out var skill)) pattern.Add(skill);
                else Error(file, row, $"{column}의 기술 '{id}'가 monster_skills.csv에 없습니다.");
            }
            return pattern;
        }

        void ReadMonsters(CsvTable table, Dictionary<string, MonsterSkillDef> skills)
        {
            const string file = MonstersFile;
            RequireColumns(file, table, "id", "name", "hp", "speed", "accuracy", "armor", "ward", "xp", "gold", "pattern", "pattern2", "phase2", "phase2_line", "boss", "color", "size", "text");
            var map = new Dictionary<string, MonsterDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                var monster = new MonsterDef
                {
                    Id = Id(file, row, seen),
                    Name = row["name"],
                    Hp = Number(file, row, "hp"),
                    Speed = Number(file, row, "speed"),
                    Accuracy = Number(file, row, "accuracy"),
                    Armor = Number(file, row, "armor"),
                    Ward = Number(file, row, "ward"),
                    Xp = Number(file, row, "xp"),
                    Gold = Number(file, row, "gold"),
                    Pattern = Pattern(file, row, "pattern", skills),
                    Pattern2 = Pattern(file, row, "pattern2", skills),
                    Phase2 = Number(file, row, "phase2"),
                    Phase2Line = row["phase2_line"],
                    Boss = Bool(file, row, "boss"),
                    Color = row["color"],
                    Size = Number(file, row, "size"),
                    Text = row["text"],
                };
                Color(file, row);
                if (monster.Pattern.Count == 0) Error(file, row, "pattern이 비었습니다.");
                if (monster.Phase2 > 0 && monster.Pattern2.Count == 0) Error(file, row, "phase2가 있으면 pattern2도 있어야 합니다.");
                if (monster.Speed < 1) Error(file, row, "speed는 1 이상입니다.");
                if (monster.Hp < 1) Error(file, row, "hp는 1 이상입니다.");
                if (monster.Size < 1 || monster.Size > 2) Error(file, row, "size는 1이나 2입니다.");
                if (monster.Id.Length > 0) map[monster.Id] = monster;
            }
            Monsters = map;
        }

        void ReadEncounters(CsvTable table)
        {
            const string file = EncountersFile;
            RequireColumns(file, table, "id", "monsters", "text");
            var map = new Dictionary<string, EncounterDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                var monsters = new List<MonsterDef>();
                foreach (string id in row["monsters"].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Monsters.TryGetValue(id, out var monster)) monsters.Add(monster);
                    else Error(file, row, $"몬스터 '{id}'가 monsters.csv에 없습니다.");
                }
                if (monsters.Count < 1 || monsters.Count > 5) Error(file, row, "몬스터는 1~5마리입니다.");
                var encounter = new EncounterDef { Id = Id(file, row, seen), Monsters = monsters, Text = row["text"] };
                if (encounter.Id.Length > 0) map[encounter.Id] = encounter;
            }
            Encounters = map;
        }

        void ReadStories(CsvTable table)
        {
            const string file = StoriesFile;
            RequireColumns(file, table, "id", "title", "text");
            var map = new Dictionary<string, StoryDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                var story = new StoryDef { Id = Id(file, row, seen), Title = row["title"], Text = row["text"] };
                if (story.Id.Length > 0) map[story.Id] = story;
            }
            Stories = map;
        }

        void ReadEvents(CsvTable events, CsvTable choices)
        {
            RequireColumns(EventsFile, events, "id", "pool", "title", "text", "journal");
            RequireColumns(EventChoicesFile, choices, "event", "label", "class", "stat", "dice", "need", "success", "fail", "success_text", "fail_text");
            var list = new List<EventCard>();
            var map = new Dictionary<string, EventCard>();
            var seen = new HashSet<string>();
            foreach (var row in events.Rows)
            {
                var card = new EventCard
                {
                    Id = Id(EventsFile, row, seen),
                    Pool = row["pool"],
                    Title = row["title"],
                    Text = row["text"],
                    Journal = row["journal"],
                };
                if (card.Pool.Length == 0) Error(EventsFile, row, "pool이 비었습니다.");
                list.Add(card);
                if (card.Id.Length > 0) map[card.Id] = card;
            }

            const string file = EventChoicesFile;
            foreach (var row in choices.Rows)
            {
                if (!map.TryGetValue(row["event"], out var card))
                {
                    Error(file, row, $"사건 '{row["event"]}'가 events.csv에 없습니다.");
                    continue;
                }
                var choice = new EventChoice
                {
                    Index = card.Choices.Count,
                    Label = row["label"],
                    ClassId = row["class"].Length > 0 ? row["class"] : null,
                    Dice = row["dice"].Length > 0 ? Number(file, row, "dice") : 0,
                    Need = row["need"].Length > 0 ? Number(file, row, "need") : 1,
                    Success = Outcomes(file, row, "success"),
                    Fail = Outcomes(file, row, "fail"),
                    SuccessText = row["success_text"],
                    FailText = row["fail_text"],
                };
                (choice.Stat, choice.FixedChance) = StatColumn(file, row, row["stat"], choice.Dice > 0);
                if (choice.ClassId != null && !Classes.ContainsKey(choice.ClassId)) Error(file, row, $"직업 '{choice.ClassId}'가 없습니다.");
                if (choice.HasCheck && (choice.Need < 1 || choice.Need > choice.Dice)) Error(file, row, "need는 1~dice입니다.");
                if (!choice.HasCheck && choice.Fail.Count > 0) Error(file, row, "판정이 없는 선택지에는 fail이 없습니다.");
                if (choice.Label.Length == 0) Error(file, row, "label이 비었습니다.");
                card.Choices.Add(choice);
            }
            foreach (var card in list)
                if (card.Choices.Count == 0) _errors.Add($"{EventsFile}: 사건 '{card.Id}'에 선택지가 없습니다.");
            Events = list;
        }

        List<Outcome> Outcomes(string file, CsvRow row, string column)
        {
            var outcomes = new List<Outcome>();
            foreach (string part in row[column].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] words = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) continue;
                string verb = words[0];
                if (!OutcomeVerbs.Contains(verb))
                {
                    Error(file, row, $"{column}의 '{verb}'는 결과 명령이 아닙니다({string.Join(", ", OutcomeVerbs)}).");
                    continue;
                }
                var outcome = new Outcome { Verb = verb };
                bool noArgument = verb == "bless_all" || verb == "weak_next";
                if (verb == "flag" || verb == "battle")
                {
                    outcome.Arg = words.Length > 1 ? words[1] : "";
                    if (outcome.Arg.Length == 0) Error(file, row, $"{verb} 뒤에 이름이 있어야 합니다.");
                    if (verb == "battle" && !Encounters.ContainsKey(outcome.Arg)) Error(file, row, $"전투 '{outcome.Arg}'가 encounters.csv에 없습니다.");
                }
                else if (!noArgument)
                {
                    if (words.Length < 2) Error(file, row, $"{verb} 뒤에 수가 있어야 합니다.");
                    else outcome.Amount = Signed(file, row, words[1], verb);
                }
                outcomes.Add(outcome);
            }
            return outcomes;
        }

        void ReadPath(CsvTable table)
        {
            const string file = PathFile;
            RequireColumns(file, table, "order", "kind", "arg");
            var path = new List<PathNode>();
            foreach (var row in table.Rows)
            {
                var node = new PathNode { Order = Number(file, row, "order"), Kind = Enum<PathKind>(file, row, "kind"), Arg = row["arg"] };
                switch (node.Kind)
                {
                    case PathKind.Story:
                        if (!Stories.ContainsKey(node.Arg)) Error(file, row, $"이야기 '{node.Arg}'가 stories.csv에 없습니다.");
                        break;
                    case PathKind.Battle:
                    case PathKind.Boss:
                        if (!Encounters.ContainsKey(node.Arg)) Error(file, row, $"전투 '{node.Arg}'가 encounters.csv에 없습니다.");
                        break;
                    case PathKind.Event:
                        if (!Events.Any(e => e.Pool == node.Arg)) Error(file, row, $"사건 묶음 '{node.Arg}'가 events.csv에 없습니다.");
                        break;
                }
                path.Add(node);
            }
            Path = path.OrderBy(n => n.Order).ToList();
            if (Path.Count == 0) _errors.Add($"{file}: 길이 비었습니다.");
        }
    }
}
