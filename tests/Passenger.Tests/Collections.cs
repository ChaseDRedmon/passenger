// Window tests run one at a time on Avalonia's one session thread whatever xunit does; as the assembly's one collection
// they also run one class after another, so no class's restores and projects are made beside another class's test.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Passenger.Tests;

/// <summary>What runs beside nothing: a class with a test that holds what it tests to a time (a key's, a frame's, a diff's,
/// how soon the window answered, a profile's samples, how long the debugger gives an evaluation) or that debugs a program
/// through the window, its debuggee waiting on timings a busy machine stretches. These run after the rest, one class at a
/// time. Beside the window tests runs only the debug adapter's protocol tests (DebugSession.Collection).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Alone
{
    public const string Name = "Alone";
}
