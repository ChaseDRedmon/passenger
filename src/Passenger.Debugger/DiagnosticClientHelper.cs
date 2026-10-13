using System.Diagnostics;
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

    /// <summary>Lets go a process held at its start by its diagnostic port, as an attach does once the debugger is registered
    /// for its runtime; one already running is let be, its runtime's answer (<paramref name="answered"/>) ending the wait
    /// for a port. Fails when the process ends first, or 5 s pass with neither: no .NET in it, or none the debugger reaches.</summary>
    public static async Task LetGoIfHeld(Process process, Task answered)
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!answered.IsCompleted)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException("it ended before the debugger could take hold of it");
            }
            // Looked for first: on Windows a client waits 30 s on a pipe that does not exist before it gives up.
            if (DiagnosticsClient.GetPublishedProcesses().Contains(process.Id))
            {
                try
                {
                    new DiagnosticsClient(process.Id).ResumeRuntime();
                    return;
                }
                catch (ServerNotAvailableException)
                {
                    // Its port was closing, or not yet listening.
                }
            }
            if (DateTime.UtcNow >= until)
            {
                throw new InvalidOperationException("it is not a .NET program the debugger can reach: no runtime in it answered, and it opened no diagnostic port within 5 s");
            }
            // A program only just started opens its port a moment after it starts.
            await Task.WhenAny(answered, Task.Delay(50));
        }
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
