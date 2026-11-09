using System.Collections.Generic;

namespace Game.Core
{
    // The stream every die of a battle (or of an adventure's events) comes from: values 0..99 in order, fixed by the
    // seed, so the same seed and the same commands give the same game everywhere (Docs/Design.md 7.2). A die succeeds
    // when its value is below the chance. The seer's foresight reads values ahead with Peek without using them.
    public sealed class Fate
    {
        readonly GameRandom _random;
        readonly List<int> _ahead = new List<int>();

        public Fate(ulong seed, ulong stream = 1)
        {
            _random = new GameRandom(seed, stream);
        }

        // How many values have been used.
        public int Drawn { get; private set; }

        public int Next()
        {
            Drawn++;
            if (_ahead.Count == 0) return _random.Next(100);
            int value = _ahead[0];
            _ahead.RemoveAt(0);
            return value;
        }

        // The value Next would give after `index` more draws, without using any.
        public int Peek(int index)
        {
            while (_ahead.Count <= index) _ahead.Add(_random.Next(100));
            return _ahead[index];
        }

        // Rolls `dice` dice at `chance` percent, the first `guaranteed` of them sure successes that draw nothing.
        public CheckResult Roll(int dice, int chance, int guaranteed)
        {
            if (dice < 0) dice = 0;
            if (guaranteed > dice) guaranteed = dice;
            if (guaranteed < 0) guaranteed = 0;
            var faces = new int[dice];
            for (int i = 0; i < dice; i++) faces[i] = i < guaranteed ? -1 : Next();
            return new CheckResult(dice, chance, guaranteed, faces);
        }
    }
}
