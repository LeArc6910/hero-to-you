using System.Collections.Generic;

namespace Game.Core
{
    // What the run chose, for the balance sandbox (CoreSandbox --events, 2026-11-11): each event card answered, which
    // choice, whether it had a check, and whether it went well. Rebuilt by replaying a save, never saved itself.
    public sealed partial class Adventure
    {
        readonly List<(string eventId, int choice, bool checkd, bool success)> _eventHistory = new List<(string, int, bool, bool)>();

        public IReadOnlyList<(string eventId, int choice, bool checkd, bool success)> EventHistory => _eventHistory;
    }
}
