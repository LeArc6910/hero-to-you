using System.Runtime.CompilerServices;

// The Core tests and the balance sandbox may set up a case directly (a hero's health, a unit's statuses); so may the
// client's playtest remote control (Client/Playtest.cs), to put a solo game into a given state.
[assembly: InternalsVisibleTo("Game.Core.Tests")]
[assembly: InternalsVisibleTo("CoreSandbox")]
[assembly: InternalsVisibleTo("Game.Client")]
[assembly: InternalsVisibleTo("Game.Client.Tests")]
