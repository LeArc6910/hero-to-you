namespace Game.Core
{
    // Korean particles that depend on whether a name ends in a final consonant: 기사가 / 사냥꾼이, 기사를 / 사냥꾼을.
    // Names ending in a Latin letter or a digit (고블린 A) go by how it is read aloud.
    public static class Korean
    {
        public static bool HasFinalConsonant(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            char last = word[word.Length - 1];
            if (last >= 0xAC00 && last <= 0xD7A3) return (last - 0xAC00) % 28 != 0;
            switch (char.ToUpperInvariant(last))
            {
                case 'L':
                case 'M':
                case 'N':
                case 'R':
                case '0':
                case '1':
                case '3':
                case '6':
                case '7':
                case '8':
                    return true;
                default:
                    return false;
            }
        }

        // 이/가
        public static string Subject(string word) => word + (HasFinalConsonant(word) ? "이" : "가");

        // 을/를
        public static string Object(string word) => word + (HasFinalConsonant(word) ? "을" : "를");

        // 은/는
        public static string Topic(string word) => word + (HasFinalConsonant(word) ? "은" : "는");
    }
}
