using System;
using System.Collections.Generic;

namespace Game.Core
{
    // PCG32 (pcg-random.org). The same seed and stream give the same numbers everywhere: .NET tests, the
    // sandbox, and Unity on Mono, IL2CPP and Web. System.Random does not promise that across runtimes, and the
    // host and every client have to agree on world generation and loot rolls.
    // The same seed with different streams gives independent sequences, e.g. one stream per world chunk.
    public sealed class GameRandom
    {
        const ulong Multiplier = 6364136223846793005UL;

        readonly ulong _increment;
        ulong _state;

        public GameRandom(ulong seed, ulong stream = 0)
        {
            _increment = (stream << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + _increment);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rotation = (int)(old >> 59);
            return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
        }

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)Bounded((uint)maxExclusive);
        }

        public int Next(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            // The width of any int range fits in a uint, even int.MinValue to int.MaxValue.
            return unchecked(minInclusive + (int)Bounded((uint)(maxExclusive - minInclusive)));
        }

        // 24 random bits, so every result is exactly representable and 1 is never returned.
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        public bool Chance(float probability) => NextFloat() < probability;

        // Each item is picked in proportion to its weight. Zero or negative weights are never picked.
        public T PickWeighted<T>(IReadOnlyList<T> items, Func<T, int> weight)
        {
            int total = 0;
            foreach (var item in items) total += Math.Max(0, weight(item));
            if (total <= 0) throw new ArgumentException("Nothing to pick: every weight is zero.", nameof(items));

            int roll = Next(total);
            foreach (var item in items)
            {
                roll -= Math.Max(0, weight(item));
                if (roll < 0) return item;
            }
            throw new InvalidOperationException("Weights changed while picking.");
        }

        // Rejects the few values that would make some results more likely than others with a plain modulo.
        uint Bounded(uint bound)
        {
            uint threshold = unchecked(0u - bound) % bound;
            while (true)
            {
                uint value = NextUInt();
                if (value >= threshold) return value % bound;
            }
        }
    }
}
