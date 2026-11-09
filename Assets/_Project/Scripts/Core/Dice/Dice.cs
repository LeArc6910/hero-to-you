using System;

namespace Game.Core
{
    // The arithmetic of checks (Docs/Dice.md): chance limits, the size of an effect for a number of successes, and
    // the odds shown before rolling.
    public static class Dice
    {
        public const int MinChance = 10;
        public const int MaxChance = 95;

        public static int ClampChance(int chance) => Math.Max(MinChance, Math.Min(MaxChance, chance));

        // power × (floor + (100 − floor) × successes / dice) / 100, rounded half up. A check with no dice gives the
        // whole power.
        public static int Value(int power, int floor, int successes, int dice)
        {
            if (power <= 0) return 0;
            if (dice <= 0) return power;
            long numerator = (long)power * (floor * dice + (100 - floor) * successes);
            long denominator = 100L * dice;
            return (int)((numerator * 2 + denominator) / (denominator * 2));
        }

        // A percentage of a value, rounded half up (perfect bonuses, weakness).
        public static int Percent(int value, int percent) => (value * percent * 2 + 100) / 200;

        // The chance of each number of successes, index 0..dice: the sure ones always succeed, each of the rest
        // with `chance` percent. For showing before a roll only; the game's state never depends on these numbers.
        public static double[] Distribution(int dice, int chance, int guaranteed)
        {
            dice = Math.Max(0, dice);
            guaranteed = Math.Max(0, Math.Min(dice, guaranteed));
            var result = new double[dice + 1];
            int rolled = dice - guaranteed;
            double p = chance / 100.0;
            for (int k = 0; k <= rolled; k++)
                result[guaranteed + k] = Binomial(rolled, k) * Math.Pow(p, k) * Math.Pow(1 - p, rolled - k);
            return result;
        }

        static double Binomial(int n, int k)
        {
            double value = 1;
            for (int i = 1; i <= k; i++) value = value * (n - k + i) / i;
            return value;
        }
    }

    // What a check would do, shown before rolling: the odds of each number of successes.
    public sealed class CheckOdds
    {
        public CheckOdds(int dice, int chance, int guaranteed)
        {
            Dice = dice;
            Chance = chance;
            Guaranteed = Math.Max(0, Math.Min(dice, guaranteed));
            BySuccesses = Game.Core.Dice.Distribution(dice, chance, Guaranteed);
        }

        public int Dice { get; }
        public int Chance { get; }
        public int Guaranteed { get; }
        public double[] BySuccesses { get; }

        public double Perfect => Dice > 0 ? BySuccesses[Dice] : 1;
        public double None => Dice > 0 ? BySuccesses[0] : 0;

        public double AtLeast(int successes)
        {
            double total = 0;
            for (int k = Math.Max(0, successes); k <= Dice; k++) total += BySuccesses[k];
            return total;
        }
    }
}
