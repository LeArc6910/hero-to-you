using System.Runtime.CompilerServices;

// Play mode tests (when they come) start games through GameRoot's internal entry points.
[assembly: InternalsVisibleTo("Game.Client.Tests")]
