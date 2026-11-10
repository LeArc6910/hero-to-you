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
        public const string ChaptersFile = "Story/chapters.csv";
        public const string PerksFile = "Journal/perks.csv";
        public const string StoriesFile = "Story/stories.csv";
        public const string EventsFile = "Story/events.csv";
        public const string EventChoicesFile = "Story/event_choices.csv";
        public const string ItemsFile = "Items/items.csv";
        public const string UpgradesFile = "Classes/upgrades.csv";
        public const string RenownFile = "Story/renown.csv";
        public const string TalksFile = "Story/talks.csv";
        public const string AchievementsFile = "Journal/achievements.csv";
        public const string CombosFile = "Classes/combos.csv";

        public static readonly string[] Files =
        {
            ClassesFile, SkillsFile, MonstersFile, MonsterSkillsFile, EncountersFile, ChaptersFile, StoriesFile, EventsFile,
            EventChoicesFile, ItemsFile, UpgradesFile, PerksFile, RenownFile, TalksFile, AchievementsFile, CombosFile,
        };

        // The sentences Core writes (Common/Lines.cs). Read with the sheets but not part of their fingerprint: wording
        // changes never mark a save as made with other sheets.
        public static readonly string[] TextFiles = { "Text/common.csv", "Text/battle.csv", "Text/adventure.csv", "Text/town.csv" };

        public Lines Lines { get; private set; } = new Lines("ko");

        // A sentence for the player in the loaded language (Lines.Get).
        public string Line(string id, params object[] args) => Lines.Get(id, args);

        // Monsters the classes summon to their side (Docs/Classes.md: necromancer, beastmaster).
        public const string SkeletonId = "skeleton";
        public const string CompanionWolfId = "companion_wolf";
        public const string TurretId = "turret";

        static readonly string[] UpgradeVerbs = { "dice", "power", "floor", "cooldown", "delay", "stat", "hp", "resolve", "armor", "gritcap", "special" };

        static readonly string[] OutcomeVerbs =
        {
            "gold", "heal", "heal_all", "hurt", "hurt_all", "resolve", "resolve_all", "bless_all", "potion", "hope", "xp",
            "weak_next", "flag", "battle", "item", "loot", "reagent", "doom", "renown",
        };

        static readonly string[] SkillEffects =
        {
            "", "burn", "chill", "lifesteal", "guard", "taunt", "mark", "bless", "lucky", "backfire", "hide", "finisher", "weaken",
            "song_courage", "song_rest", "encore", "corrode", "raise", "deathmark", "foretell", "twist", "command", "wolfguard", "call",
            "pierce", "meditate", "turret", "snare", "echo", "bonewall",
        };
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
        // The first chapter's map (tests and tools); every chapter has its own (Chapters).
        public MapDef Map => Chapters.Count > 0 ? Chapters[0].Map : null;
        public IReadOnlyList<ChapterDef> Chapters { get; private set; } = new List<ChapterDef>();
        public IReadOnlyList<PerkDef> Perks { get; private set; } = new List<PerkDef>();
        public IReadOnlyList<RenownDef> Renown { get; private set; } = new List<RenownDef>();
        public IReadOnlyList<TalkDef> Talks { get; private set; } = new List<TalkDef>();
        public IReadOnlyList<AchievementDef> Achievements { get; private set; } = new List<AchievementDef>();
        public IReadOnlyList<ComboDef> Combos { get; private set; } = new List<ComboDef>();
        public IReadOnlyDictionary<string, StoryDef> Stories { get; private set; } = new Dictionary<string, StoryDef>();
        public IReadOnlyList<EventCard> Events { get; private set; } = new List<EventCard>();
        public IReadOnlyDictionary<string, ItemDef> Items { get; private set; } = new Dictionary<string, ItemDef>();
        public IReadOnlyList<ItemDef> ItemList { get; private set; } = new List<ItemDef>();
        public IReadOnlyDictionary<string, UpgradeDef> Upgrades { get; private set; } = new Dictionary<string, UpgradeDef>();

        public IEnumerable<ClassDef> PlayableClasses => ClassList.Where(c => c.Playable);

        // `read` gives a sheet's text for its path under Assets/_Project/Resources/Data (one of Files).
        // A short hash of every sheet's text: a save notes it to tell when the rules changed (Adventure.Save).
        public string Fingerprint { get; private set; } = "";

        // `language` "en" reads the sheets' `*_en` cells where they are filled in (Docs/Localization.md); the rules and
        // the save's sheet hash do not change with it.
        public static GameData Load(Func<string, string> read, string language = "ko")
        {
            string was = CsvTable.Language;
            CsvTable.Language = string.IsNullOrEmpty(language) || language == "ko" ? null : language;
            try
            {
                // The sentences first: texts made while reading the sheets (a weapon's attack) use them.
                var lineErrors = new List<string>();
                var lines = ReadLines(read, string.IsNullOrEmpty(language) ? "ko" : language, lineErrors);
                var data = LoadSheets(read, lines);
                data._errors.AddRange(lineErrors);
                if (data._errors.Count > 0) throw new FormatException("시트 오류:\n" + string.Join("\n", data._errors));
                return data;
            }
            finally
            {
                CsvTable.Language = was;
            }
        }

        static Lines ReadLines(Func<string, string> read, string language, List<string> errors)
        {
            var lines = new Lines(language);
            foreach (string file in TextFiles)
            {
                var table = CsvTable.Read(read(file));
                var missing = table.MissingColumns("id", "ko", "en");
                if (missing.Count > 0)
                {
                    errors.Add($"{file}: 열이 없습니다: {string.Join(", ", missing)}");
                    continue;
                }
                foreach (var row in table.Rows)
                {
                    string id = row["id"];
                    if (id.Length == 0) continue;
                    if (lines.Has(id)) errors.Add($"{file} {row.Line}행: 문장 id '{id}'가 겹칩니다.");
                    string text = language == "en" && row["en"].Length > 0 ? row["en"] : row["ko"];
                    lines.Add(id, text.Replace("\\n", "\n"));
                }
            }
            return lines;
        }

        static GameData LoadSheets(Func<string, string> read, Lines lines)
        {
            var data = new GameData { Lines = lines };
            uint hash = 2166136261;
            var source = read;
            string Read(string file)
            {
                string text = source(file) ?? "";
                foreach (char c in text) hash = unchecked((hash ^ c) * 16777619);
                return text;
            }
            var texts = Files.ToDictionary(f => f, Read);
            read = f => texts.TryGetValue(f, out string t) ? t : Read(f);
            data.Fingerprint = hash.ToString("x8");
            var skills = data.ReadSkills(CsvTable.Read(read(SkillsFile)));
            data.Skills = skills;
            data.ReadItems(CsvTable.Read(read(ItemsFile)));
            data.ReadClasses(CsvTable.Read(read(ClassesFile)), skills);
            data.ReadUpgrades(CsvTable.Read(read(UpgradesFile)));
            var monsterSkills = data.ReadMonsterSkills(CsvTable.Read(read(MonsterSkillsFile)));
            data.MonsterSkills = monsterSkills;
            data.ReadMonsters(CsvTable.Read(read(MonstersFile)), monsterSkills);
            foreach (string summon in new[] { SkeletonId, CompanionWolfId, TurretId })
                if (!data.Monsters.ContainsKey(summon)) data._errors.Add($"{MonstersFile}: 소환 몬스터 '{summon}'가 없습니다.");
            data.ReadEncounters(CsvTable.Read(read(EncountersFile)));
            data.ReadStories(CsvTable.Read(read(StoriesFile)));
            data.ReadEvents(CsvTable.Read(read(EventsFile)), CsvTable.Read(read(EventChoicesFile)));
            data.ReadChapters(CsvTable.Read(read(ChaptersFile)), read);
            data.ReadPerks(CsvTable.Read(read(PerksFile)));
            data.ReadRenown(CsvTable.Read(read(RenownFile)));
            data.ReadTalks(CsvTable.Read(read(TalksFile)));
            data.ReadAchievements(CsvTable.Read(read(AchievementsFile)));
            data.ReadCombos(CsvTable.Read(read(CombosFile)));
            foreach (var perk in data.Perks.Where(p => p.Requires.Length > 0))
                if (!data.Achievements.Any(a => a.Id == perk.Requires)) data._errors.Add($"{PerksFile}: '{perk.Id}'의 requires '{perk.Requires}'가 achievements.csv에 없습니다.");
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
            RequireColumns(file, table, "id", "class", "name", "stat", "dice", "power", "floor", "kind", "target", "delay", "cooldown", "element", "effect", "perfect", "cost", "text");
            var skills = new Dictionary<string, SkillDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                var skill = new SkillDef
                {
                    Data = this,
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
                string[] cost = row["cost"].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (cost.Length == 2 && (cost[0] == "reagent" || cost[0] == "soul" || cost[0] == "ki"))
                {
                    skill.CostKind = cost[0];
                    skill.CostAmount = Signed(file, row, cost[1], "cost");
                }
                else if (cost.Length > 0) Error(file, row, $"cost '{row["cost"]}'는 'reagent 1'이나 'soul 2'처럼 씁니다.");
                Choice(file, row, "element", Elements);
                Choice(file, row, "effect", SkillEffects);
                Choice(file, row, "perfect", PerfectEffects);
                if (skill.Floor > 100) Error(file, row, "floor는 0~100입니다.");
                if (skill.Delay < 20 || skill.Delay > 300) Error(file, row, "delay는 20~300(%)입니다.");
                if (skill.Damages && skill.Target != SkillTarget.Enemy && skill.Target != SkillTarget.AllEnemies)
                    Error(file, row, "공격 기술의 target은 enemy나 all_enemies입니다.");
                if (skill.Kind == SkillKind.Revive && skill.Target != SkillTarget.DownedAlly) Error(file, row, "revive의 target은 downed_ally입니다.");
                if (skill.Kind == SkillKind.Heal && skill.Target != SkillTarget.Ally && skill.Target != SkillTarget.Self && skill.Target != SkillTarget.AllAllies)
                    Error(file, row, "heal의 target은 ally나 self입니다.");
                if (skill.Kind == SkillKind.Support && skill.Effect.Length == 0) Error(file, row, "support 기술에는 effect가 있어야 합니다.");
                if (skill.Id.Length > 0) skills[skill.Id] = skill;
            }
            return skills;
        }

        void ReadClasses(CsvTable table, Dictionary<string, SkillDef> skills)
        {
            const string file = ClassesFile;
            RequireColumns(file, table, "id", "name", "role", "hp", "armor", "resolve", "grit", "str", "agi", "int", "wil", "per", "cha", "weapon", "weapons", "skills", "color", "mechanic", "blurb", "playable", "row");
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
                    BackRow = row["row"] == "back",
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
                cls.WeaponKinds = row["weapons"].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (Items.TryGetValue(row["weapon"], out var weapon) && weapon.Slot == ItemSlot.Weapon)
                {
                    cls.StartWeapon = weapon;
                    if (!cls.WeaponKinds.Contains(weapon.Kind)) Error(file, row, $"처음 무기 '{weapon.Id}'({weapon.Kind})를 이 직업이 들 수 없습니다(weapons).");
                }
                else Error(file, row, $"처음 무기 '{row["weapon"]}'가 items.csv의 무기가 아닙니다.");
                if (cls.Playable && classSkills.Count < 2) Error(file, row, "고를 수 있는 직업은 기술이 둘 이상이어야 합니다.");
                if (cls.GritCap < 1) Error(file, row, "grit은 1 이상입니다.");
                list.Add(cls);
                if (cls.Id.Length > 0) map[cls.Id] = cls;
            }
            foreach (var skill in skills.Values)
                if (!map.ContainsKey(skill.ClassId)) _errors.Add($"{SkillsFile}: 기술 '{skill.Id}'의 직업 '{skill.ClassId}'가 없습니다.");
            ClassList = list;
            Classes = map;
        }

        void ReadItems(CsvTable table)
        {
            const string file = ItemsFile;
            RequireColumns(file, table, "id", "name", "slot", "kind", "tier", "price", "attack", "stat", "dice", "power", "floor", "damage", "delay", "perfect", "armor", "hp", "resolve", "bonus", "use", "text");
            var list = new List<ItemDef>();
            var map = new Dictionary<string, ItemDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                var item = new ItemDef
                {
                    Data = this,
                    Id = Id(file, row, seen),
                    Name = row["name"],
                    Slot = Enum<ItemSlot>(file, row, "slot"),
                    Kind = row["kind"],
                    Tier = Number(file, row, "tier"),
                    Price = Number(file, row, "price"),
                    Armor = Number(file, row, "armor"),
                    Hp = Number(file, row, "hp"),
                    Resolve = Number(file, row, "resolve"),
                    Text = row["text"],
                };
                string[] bonus = row["bonus"].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i + 1 < bonus.Length; i += 2)
                {
                    if (Stats.TryParse(bonus[i], out var stat)) item.StatBonus[(int)stat] += Signed(file, row, bonus[i + 1], "bonus");
                    else Error(file, row, $"bonus의 '{bonus[i]}'는 능력치(str, agi…)가 아닙니다.");
                }
                if (bonus.Length % 2 != 0) Error(file, row, "bonus는 '능력치 수' 짝으로 씁니다(예: str 5).");
                if (item.Slot == ItemSlot.Consumable)
                {
                    var uses = new List<UpgradeEffect>();
                    foreach (string part in row["use"].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] words = part.Trim().Split(' ');
                        if (!new[] { "heal", "resolve", "cleanse", "flee", "bomb", "luck" }.Contains(words[0])) Error(file, row, $"use의 '{words[0]}'는 heal, resolve, cleanse, flee, bomb, luck 가운데 하나입니다.");
                        uses.Add(new UpgradeEffect { Verb = words[0], Target = "", Amount = words.Length > 1 ? Signed(file, row, words[1], "use") : 0 });
                    }
                    if (uses.Count == 0) Error(file, row, "소모품에는 use가 있어야 합니다.");
                    item.Use = uses;
                }
                string[] effect = row["effect"].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (effect.Length > 0 && item.Slot != ItemSlot.Weapon)
                {
                    item.Effect = effect[0];
                    item.EffectAmount = effect.Length > 1 ? Signed(file, row, effect[1], "effect") : 0;
                    if (item.Effect != "thorns" && item.Effect != "regen") Error(file, row, $"갑옷·장신구의 effect '{item.Effect}'는 thorns나 regen입니다.");
                }
                if (item.Slot == ItemSlot.Weapon)
                {
                    var attack = new SkillDef
                    {
                        Data = this,
                        Id = Hero.AttackId,
                        ClassId = "",
                        Name = row["attack"],
                        Dice = Number(file, row, "dice"),
                        Power = Number(file, row, "power"),
                        Floor = Number(file, row, "floor"),
                        Kind = row["damage"] == "magic" ? SkillKind.Magic : SkillKind.Physical,
                        Target = SkillTarget.Enemy,
                        Delay = Number(file, row, "delay"),
                        Element = "",
                        Effect = effect.Length > 0 ? effect[0] : "",
                        Perfect = row["perfect"],
                        Text = this.Line("common.weapon_attack", item.Name),
                    };
                    (attack.Stat, attack.FixedChance) = StatColumn(file, row, row["stat"], true);
                    Choice(file, row, "damage", new[] { "physical", "magic" });
                    Choice(file, row, "perfect", PerfectEffects);
                    if (attack.Name.Length == 0) Error(file, row, "무기에는 attack(공격 이름)이 있어야 합니다.");
                    if (attack.Dice < 1) Error(file, row, "무기의 dice는 1 이상입니다.");
                    if (attack.Delay < 20 || attack.Delay > 300) Error(file, row, "delay는 20~300(%)입니다.");
                    if (attack.Effect.Length > 0 && !new[] { "burn", "chill", "lifesteal", "weaken", "corrode", "pierce" }.Contains(attack.Effect)) Error(file, row, $"무기의 effect '{attack.Effect}'는 burn, chill, lifesteal, weaken, corrode, pierce 가운데 하나입니다.");
                    item.Attack = attack;
                }
                list.Add(item);
                if (item.Id.Length > 0) map[item.Id] = item;
            }
            Items = map;
            ItemList = list;
        }

        void ReadUpgrades(CsvTable table)
        {
            const string file = UpgradesFile;
            RequireColumns(file, table, "id", "class", "name", "effect", "text");
            var map = new Dictionary<string, UpgradeDef>();
            var byClass = new Dictionary<string, List<UpgradeDef>>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                var upgrade = new UpgradeDef { Id = Id(file, row, seen), ClassId = row["class"], Name = row["name"], Text = row["text"] };
                if (!Classes.TryGetValue(upgrade.ClassId, out var cls))
                {
                    Error(file, row, $"직업 '{upgrade.ClassId}'가 없습니다.");
                    continue;
                }
                var effects = new List<UpgradeEffect>();
                foreach (string part in row["effect"].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] words = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length == 0 || !UpgradeVerbs.Contains(words[0]))
                    {
                        Error(file, row, $"'{part.Trim()}'는 강화 효과가 아닙니다({string.Join(", ", UpgradeVerbs)}).");
                        continue;
                    }
                    bool targeted = words[0] != "hp" && words[0] != "resolve" && words[0] != "armor" && words[0] != "gritcap";
                    if (words.Length != (targeted ? 3 : 2))
                    {
                        Error(file, row, $"'{part.Trim()}': '{words[0]}{(targeted ? " 대상" : "")} 수'로 씁니다.");
                        continue;
                    }
                    var effect = new UpgradeEffect { Verb = words[0], Target = targeted ? words[1] : "", Amount = Signed(file, row, words[words.Length - 1], words[0]) };
                    bool skillVerb = effect.Verb == "dice" || effect.Verb == "power" || effect.Verb == "floor" || effect.Verb == "cooldown" || effect.Verb == "delay";
                    if (skillVerb && effect.Target != Hero.AttackId && !cls.Skills.Any(k => k.Id == effect.Target))
                        Error(file, row, $"'{effect.Target}'는 {cls.Id}의 기술이 아닙니다(기본 공격은 attack).");
                    if (effect.Verb == "stat" && !Stats.TryParse(effect.Target, out _)) Error(file, row, $"'{effect.Target}'는 능력치가 아닙니다.");
                    effects.Add(effect);
                }
                upgrade.Effects = effects;
                if (upgrade.Id.Length > 0) map[upgrade.Id] = upgrade;
                if (!byClass.TryGetValue(cls.Id, out var listed)) byClass[cls.Id] = listed = new List<UpgradeDef>();
                listed.Add(upgrade);
            }
            foreach (var cls in ClassList)
            {
                cls.Upgrades = byClass.TryGetValue(cls.Id, out var listed) ? listed : new List<UpgradeDef>();
                if (cls.Playable && cls.Upgrades.Count < 4) _errors.Add($"{file}: 고를 수 있는 직업 '{cls.Id}'의 강화가 넷보다 적습니다.");
            }
            Upgrades = map;
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
                Choice(file, row, "effect", new[] { "", "burn", "chill", "weaken", "howl", "charge", "seal", "erase", "freeze", "scorch", "daze" });
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
            RequireColumns(file, table, "id", "name", "hp", "speed", "accuracy", "armor", "ward", "xp", "gold", "pattern", "pattern2", "phase2", "phase2_line", "phase2_summon", "boss", "color", "size", "text");
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
            foreach (var row in table.Rows)
            {
                if (!map.TryGetValue(row["id"], out var monster)) continue;
                var summons = new List<MonsterDef>();
                foreach (string id in row["phase2_summon"].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (map.TryGetValue(id, out var summon)) summons.Add(summon);
                    else Error(file, row, $"phase2_summon의 '{id}'가 monsters.csv에 없습니다.");
                }
                monster.Phase2Summon = summons;
            }
        }

        void ReadEncounters(CsvTable table)
        {
            const string file = EncountersFile;
            RequireColumns(file, table, "id", "monsters", "terrain", "danger", "chapter", "text");
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
                var encounter = new EncounterDef
                {
                    Id = Id(file, row, seen),
                    Monsters = monsters,
                    Terrains = row["terrain"].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                    Danger = Number(file, row, "danger"),
                    Chapter = row["chapter"].Length > 0 ? Number(file, row, "chapter") : 0,
                    Text = row["text"],
                };
                foreach (string terrain in encounter.Terrains)
                    if (!System.Enum.GetValues(typeof(Terrain)).Cast<Terrain>().Any(t => TerrainInfo.Code(t) == terrain)) Error(file, row, $"terrain '{terrain}'는 땅 종류가 아닙니다(plains, forest, swamp, road, ruins).");
                if (encounter.Danger > 0 && encounter.Terrains.Count == 0) Error(file, row, "지도에 나오는 전투(danger 1 이상)에는 terrain이 있어야 합니다.");
                if (encounter.Id.Length > 0) map[encounter.Id] = encounter;
            }
            Encounters = map;
        }

        void ReadStories(CsvTable table)
        {
            const string file = StoriesFile;
            RequireColumns(file, table, "id", "title", "text", "art");
            var map = new Dictionary<string, StoryDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                var story = new StoryDef { Id = Id(file, row, seen), Title = row["title"], Text = row["text"], Art = row["art"] };
                if (story.Id.Length > 0) map[story.Id] = story;
            }
            Stories = map;
        }

        void ReadEvents(CsvTable events, CsvTable choices)
        {
            RequireColumns(EventsFile, events, "id", "pool", "class", "title", "text", "journal");
            RequireColumns(EventChoicesFile, choices, "event", "label", "class", "requires", "stat", "dice", "need", "success", "fail", "success_text", "fail_text");
            var list = new List<EventCard>();
            var map = new Dictionary<string, EventCard>();
            var seen = new HashSet<string>();
            foreach (var row in events.Rows)
            {
                var card = new EventCard
                {
                    Id = Id(EventsFile, row, seen),
                    Pool = row["pool"],
                    ClassId = row["class"].Length > 0 ? row["class"] : null,
                    Title = row["title"],
                    Text = row["text"],
                    Journal = row["journal"],
                    Art = row["art"],
                    Requires = row["requires"],
                };
                if (card.Pool.Length == 0) Error(EventsFile, row, "pool이 비었습니다.");
                if (card.ClassId != null && !Classes.ContainsKey(card.ClassId)) Error(EventsFile, row, $"직업 '{card.ClassId}'가 없습니다.");
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
                    Requires = row["requires"].Length > 0 ? row["requires"] : null,
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
                if (verb == "flag" || verb == "battle" || verb == "item")
                {
                    outcome.Arg = words.Length > 1 ? words[1] : "";
                    if (outcome.Arg.Length == 0) Error(file, row, $"{verb} 뒤에 이름이 있어야 합니다.");
                    if (verb == "battle" && !Encounters.ContainsKey(outcome.Arg)) Error(file, row, $"전투 '{outcome.Arg}'가 encounters.csv에 없습니다.");
                    if (verb == "item" && !Items.ContainsKey(outcome.Arg)) Error(file, row, $"아이템 '{outcome.Arg}'가 items.csv에 없습니다.");
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

        void ReadChapters(CsvTable table, Func<string, string> read)
        {
            const string file = ChaptersFile;
            string[] columns = { "id", "title", "map", "boss", "midboss", "town", "prologue", "epilogue", "epilogue_kind", "epilogue_grim", "wild", "chest", "herb", "quest", "clue", "shrine", "elder", "weaken_flag", "nosummon_flag", "quest_name", "clue_name", "midboss_name", "boss_name", "rift", "kind_flags", "kind_need", "grim_flags", "grim_need", "goal_quest", "goal_clue", "goal_camp", "goal_boss", "weaken_text", "nosummon_text", "dungeon", "dungeon_name", "music", "kind_renown" };
            RequireColumns(file, table, columns);
            var list = new List<ChapterDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                var chapter = new ChapterDef
                {
                    Id = Id(file, row, seen),
                    Index = list.Count,
                    Title = row["title"],
                    Boss = row["boss"],
                    Midboss = row["midboss"],
                    Town = row["town"],
                    Prologue = row["prologue"],
                    Epilogue = row["epilogue"],
                    EpilogueKind = row["epilogue_kind"],
                    EpilogueGrim = row["epilogue_grim"],
                    WildPool = row["wild"],
                    ChestPool = row["chest"],
                    HerbPool = row["herb"],
                    QuestPool = row["quest"],
                    CluePool = row["clue"],
                    ShrinePool = row["shrine"],
                    ElderPool = row["elder"],
                    InterludePool = row["interlude"],
                    WeakenFlag = row["weaken_flag"],
                    NoSummonFlag = row["nosummon_flag"],
                    QuestName = row["quest_name"],
                    ClueName = row["clue_name"],
                    MidbossName = row["midboss_name"],
                    BossName = row["boss_name"],
                    Rift = row["rift"],
                    KindFlags = List(row["kind_flags"]),
                    KindNeed = Number(file, row, "kind_need"),
                    GrimFlags = List(row["grim_flags"]),
                    GrimNeed = Number(file, row, "grim_need"),
                    GoalQuest = row["goal_quest"],
                    GoalClue = row["goal_clue"],
                    GoalCamp = row["goal_camp"],
                    GoalBoss = row["goal_boss"],
                    WeakenText = row["weaken_text"],
                    NoSummonText = row["nosummon_text"],
                    Dungeon = List(row["dungeon"]),
                    DungeonName = row["dungeon_name"],
                    Music = row["music"],
                    KindRenown = Number(file, row, "kind_renown"),
                };
                var maps = new List<MapDef>();
                foreach (string path in List(row["map"]))
                {
                    try
                    {
                        maps.Add(MapDef.Parse(read(path), path));
                    }
                    catch (FormatException e)
                    {
                        _errors.Add(e.Message);
                    }
                }
                chapter.Maps = maps;
                chapter.Map = maps.FirstOrDefault();
                foreach (string encounter in new[] { chapter.Boss, chapter.Midboss }.Concat(chapter.Dungeon.Where(r => !r.StartsWith(Adventure.EventRoom))))
                    if (!Encounters.ContainsKey(encounter)) Error(file, row, $"전투 '{encounter}'가 encounters.csv에 없습니다.");
                foreach (string room in chapter.Dungeon.Where(r => r.StartsWith(Adventure.EventRoom)))
                    if (!Events.Any(e => e.Pool == room.Substring(Adventure.EventRoom.Length))) Error(file, row, $"던전 사건 방 '{room}'의 묶음이 events.csv에 없습니다.");
                foreach (string story in new[] { chapter.Prologue, chapter.Epilogue, chapter.EpilogueKind, chapter.EpilogueGrim, chapter.Rift })
                    if (!Stories.ContainsKey(story)) Error(file, row, $"이야기 '{story}'가 stories.csv에 없습니다.");
                foreach (string pool in new[] { chapter.WildPool, chapter.ChestPool, chapter.HerbPool, chapter.QuestPool, chapter.CluePool, chapter.ShrinePool, chapter.ElderPool })
                    if (!Events.Any(e => e.Pool == pool)) Error(file, row, $"사건 묶음 '{pool}'가 events.csv에 없습니다.");
                if (chapter.InterludePool.Length > 0 && !Events.Any(e => e.Pool == chapter.InterludePool))
                    Error(file, row, $"막간 묶음 '{chapter.InterludePool}'가 events.csv에 없습니다.");
                int index = list.Count + 1;
                if (!Encounters.Values.Any(e => e.Danger == 1 && (e.Chapter == 0 || e.Chapter == index))) Error(file, row, "이 챕터의 지도에 놓을 전투(danger 1)가 없습니다.");
                list.Add(chapter);
            }
            if (list.Count == 0) _errors.Add($"{file}: 챕터가 없습니다.");
            Chapters = list;
        }

        static List<string> List(string raw) => (raw ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        // What a journal perk or a class pairing can give at the start of a run (Adventure.ApplyGift).
        public static readonly string[] PerkVerbs = { "gold", "potion", "tonic", "hope", "doom", "xp", "hard", "reveal", "bless", "forge", "renown" };

        void ReadCombos(CsvTable table)
        {
            const string file = CombosFile;
            RequireColumns(file, table, "id", "name", "classes", "effect", "text");
            var list = new List<ComboDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                string[] words = row["effect"].Split(' ');
                var combo = new ComboDef
                {
                    Id = Id(file, row, seen),
                    Name = row["name"],
                    Classes = List(row["classes"]),
                    Effect = new UpgradeEffect { Verb = words[0], Target = "", Amount = words.Length > 1 ? Signed(file, row, words[1], "effect") : 0 },
                    Text = row["text"],
                };
                if (!PerkVerbs.Contains(combo.Effect.Verb) || combo.Effect.Verb == "hard") Error(file, row, $"effect '{combo.Effect.Verb}'는 쓸 수 없습니다.");
                foreach (string cls in combo.Classes)
                    if (!Classes.ContainsKey(cls)) Error(file, row, $"직업 '{cls}'가 없습니다.");
                list.Add(combo);
            }
            Combos = list;
        }

        static readonly string[] AchievementRules = { "win", "chapters", "kind_ending", "grim_ending", "kind_all", "renown", "dungeons", "contracts", "talks", "notes", "hard_win", "hardcore_win", "easy_win", "classes", "runs", "no_fall", "short_win", "combos", "old_friends", "monsters", "items", "endings", "fragile_win" };

        void ReadAchievements(CsvTable table)
        {
            const string file = AchievementsFile;
            RequireColumns(file, table, "id", "name", "rule", "text");
            var list = new List<AchievementDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                string[] words = row["rule"].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var achievement = new AchievementDef { Id = Id(file, row, seen), Name = row["name"], Rule = words.Length > 0 ? words[0] : "", Amount = words.Length > 1 ? Signed(file, row, words[1], "rule") : 0, Text = row["text"] };
                if (!AchievementRules.Contains(achievement.Rule)) Error(file, row, $"rule '{achievement.Rule}'는 {string.Join(", ", AchievementRules)} 가운데 하나입니다.");
                list.Add(achievement);
            }
            Achievements = list;
        }

        void ReadTalks(CsvTable table)
        {
            const string file = TalksFile;
            RequireColumns(file, table, "id", "chapter", "a", "b", "requires", "lines");
            var list = new List<TalkDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                var talk = new TalkDef { Id = Id(file, row, seen), Chapter = Number(file, row, "chapter"), A = row["a"], B = row["b"], Requires = row["requires"] };
                var lines = new List<(string, string)>();
                foreach (string part in row["lines"].Split('|'))
                {
                    int colon = part.IndexOf(':');
                    string speaker = colon > 0 ? part.Substring(0, colon).Trim() : "";
                    if (speaker != talk.A && speaker != talk.B) Error(file, row, $"줄 '{part.Trim()}'는 '{talk.A}:' 또는 '{talk.B}:'로 시작해야 합니다.");
                    lines.Add((speaker, colon > 0 ? part.Substring(colon + 1).Trim() : part.Trim()));
                }
                talk.Lines = lines;
                foreach (string cls in new[] { talk.A, talk.B })
                    if (!Classes.ContainsKey(cls)) Error(file, row, $"직업 '{cls}'가 classes.csv에 없습니다.");
                list.Add(talk);
            }
            Talks = list;
        }

        void ReadRenown(CsvTable table)
        {
            const string file = RenownFile;
            RequireColumns(file, table, "need", "title", "text");
            var list = new List<RenownDef>();
            foreach (var row in table.Rows)
            {
                var rank = new RenownDef { Need = Number(file, row, "need"), Title = row["title"], Text = row["text"] };
                if (list.Count > 0 && rank.Need <= list[list.Count - 1].Need) Error(file, row, "need는 위 줄보다 커야 합니다.");
                if (list.Count == 0 && rank.Need != 0) Error(file, row, "첫 줄의 need는 0이어야 합니다.");
                list.Add(rank);
            }
            if (list.Count == 0) _errors.Add($"{file}: 명성 단계가 없습니다.");
            Renown = list;
        }

        void ReadPerks(CsvTable table)
        {
            const string file = PerksFile;
            RequireColumns(file, table, "id", "name", "pages", "effect", "text");
            var list = new List<PerkDef>();
            var seen = new HashSet<string>();
            foreach (var row in table.Rows)
            {
                string[] words = row["effect"].Split(' ');
                var perk = new PerkDef
                {
                    Id = Id(file, row, seen),
                    Name = row["name"],
                    Pages = Number(file, row, "pages"),
                    Effect = new UpgradeEffect { Verb = words[0], Target = "", Amount = words.Length > 1 ? Signed(file, row, words[1], "effect") : 0 },
                    Text = row["text"],
                    Requires = row["requires"],
                };
                if (!PerkVerbs.Contains(perk.Effect.Verb)) Error(file, row, $"effect '{perk.Effect.Verb}'는 {string.Join(", ", PerkVerbs)} 가운데 하나입니다.");
                list.Add(perk);
            }
            Perks = list;
        }
    }
}
