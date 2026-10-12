using Microsoft.Diagnostics.NETCore.Client;

namespace Passenger.Debugger;

public static class DiagnosticClientHelper
{
    /// <summary>What holds a program at its start until a diagnostics client lets it go.</summary>
    private const string HoldVariable = "DOTNET_DefaultDiagnosticPortSuspend";

    /// <summary>Lets go a program held at its start by the default diagnostic port. The variable that held it is cleared
    /// in the program first, as the profiler clears its port's, so a .NET program it starts in turn (a build, a test
    /// host, a child it waits for) runs as it would undebugged rather than held for a debugger that never comes.</summary>
    public static async Task DiagnosticClientResumeRuntime(int debuggeeProcessId)
    {
        DiagnosticsClient diagnosticsClient = new DiagnosticsClient(debuggeeProcessId);
        const int maxRetries = 5;
        int delayMs = 50;

        // For applications like godot, which start their own CLR, diagnostics IPC may not be available immediately.
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await Task.Delay(delayMs);
                Release(diagnosticsClient);
                return;
            }
            catch (ServerNotAvailableException) when (attempt < maxRetries)
            {
                delayMs *= 2;
            }
        }
        Release(diagnosticsClient);
    }

    private static void Release(DiagnosticsClient client)
    {
        try
        {
            client.SetEnvironmentVariable(HoldVariable, string.Empty);
        }
        catch (ServerErrorException)
        {
            // A runtime before .NET 6 has no such command: its children are still held, but it runs.
        }
        client.ResumeRuntime();
    }
}
