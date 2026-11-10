using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // What a party of classes lacks (Docs/Classes.md 파티의 모양): nobody who stands in the front row, nobody with
    // a healing skill. A party with neither is allowed and is a hard challenge (user decision, 2026-11-10): the
    // party screen says so before the run, and winning with one earns its own achievement.
    public static class PartyShape
    {
        public static bool HasFront(IEnumerable<ClassDef> classes) => classes.Any(c => !c.BackRow);

        public static bool IsHealer(ClassDef cls) => cls.Skills.Any(s => s.Kind == SkillKind.Heal);

        public static bool HasHealer(IEnumerable<ClassDef> classes) => classes.Any(IsHealer);

        // Neither a front-liner nor a healer.
        public static bool Fragile(IEnumerable<ClassDef> classes)
        {
            var list = classes.ToList();
            return list.Count > 0 && !HasFront(list) && !HasHealer(list);
        }

        public static IEnumerable<ClassDef> Of(GameData data, IEnumerable<string> classIds) =>
            classIds.Where(data.Classes.ContainsKey).Select(id => data.Classes[id]);
    }
}
