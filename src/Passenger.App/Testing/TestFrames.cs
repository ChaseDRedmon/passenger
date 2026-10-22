using Passenger.Core;
using Passenger.TestFrames;

namespace Passenger.App.Testing;

/// <summary>Passenger's hook into a test run of a project that tests Avalonia headless under xunit v3 (src/Passenger.TestFrames,
/// carried under testframes/): as one of its tests fails, each window the test has open is drawn by the headless platform
/// into the test's result as an attachment, which the Unit Tests window shows as the result's frames. Given only to the
/// runs Passenger starts, as a startup hook in the test program's environment, so nothing of it is in the project as built.</summary>
internal static class TestFrames
{
    /// <summary>The hook beside Passenger, as its build carries it.</summary>
    public static string HookPath => Path.Combine(AppContext.BaseDirectory, "testframes", "Passenger.TestFrames.dll");

    /// <summary>Whether a built test assembly's tests are ones the hook draws: Avalonia's headless platform and xunit v3 built
    /// beside it, and this build of Passenger carries the hook.</summary>
    public static bool Draws(string assembly)
    {
        string folder = Path.GetDirectoryName(assembly) ?? string.Empty;
        return File.Exists(Path.Combine(folder, "Avalonia.Headless.dll")) && File.Exists(Path.Combine(folder, "xunit.v3.core.dll")) && File.Exists(HookPath);
    }

    /// <summary>A run's environment with the hook first among its startup hooks, those the project's launch profile or the
    /// IDE's own environment name after it.</summary>
    public static IReadOnlyDictionary<string, string> With(IReadOnlyDictionary<string, string> environment)
    {
        return new Dictionary<string, string>(environment)
        {
            [Variables.Frames] = "1",
            [RuntimeVariables.StartupHooks] = StartupHooks.First(environment, HookPath),
        };
    }
}
