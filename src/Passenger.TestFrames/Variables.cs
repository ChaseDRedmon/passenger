namespace Passenger.TestFrames;

/// <summary>What Passenger puts in a test run's environment for the hook, compiled into both ends.</summary>
internal static class Variables
{
    /// <summary>"1" in a test run Passenger starts whose failing tests' windows are to be drawn into their results.</summary>
    public const string Frames = "PASSENGER_TEST_FRAMES";

    /// <summary>The runtime's list of startup hooks, which loads this one before the test program's Main.</summary>
    public const string StartupHooks = "DOTNET_STARTUP_HOOKS";

    /// <summary>The Avalonia the hook is built for; a test project on another major version gets no frames.</summary>
    public const int AvaloniaMajor = 12;
}
