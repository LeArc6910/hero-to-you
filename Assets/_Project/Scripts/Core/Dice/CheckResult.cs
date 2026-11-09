namespace Game.Core
{
    // One check (Docs/Dice.md): how many dice, the chance of each, which were sure successes and what the rest rolled.
    public sealed class CheckResult
    {
        internal CheckResult(int dice, int chance, int guaranteed, int[] faces)
        {
            Dice = dice;
            Chance = chance;
            Guaranteed = guaranteed;
            Faces = faces;
            Hits = new bool[dice];
            for (int i = 0; i < dice; i++)
            {
                Hits[i] = faces[i] < 0 || faces[i] < chance;
                if (Hits[i]) Successes++;
                else RolledFailures++;
            }
        }

        public int Dice { get; }
        public int Chance { get; }
        public int Guaranteed { get; }

        // Per die: -1 for a sure success, else the value 0..99 the fate gave it (a success when below the chance).
        // Sure successes come first.
        public int[] Faces { get; }
        public bool[] Hits { get; }
        public int Successes { get; }

        // Rolled dice that failed: what grit counts (sure successes are never failures).
        public int RolledFailures { get; }

        public bool Perfect => Dice > 0 && Successes == Dice;

        public override string ToString() => $"{Successes}/{Dice}";
    }
}
