using Microsoft.Diagnostics.NETCore.Client;

namespace Passenger.Debugger;

public static class DiagnosticClientHelper
{
    // For applications like godot, which start their own CLR, diagnostics IPC may not be available immediately.
    // Retry until it succeeds.
    public static async Task DiagnosticClientResumeRuntime(int debuggeeProcessId)
    {
        DiagnosticsClient diagnosticsClient = new DiagnosticsClient(debuggeeProcessId);
        const int maxRetries = 5;
        int delayMs = 50;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await Task.Delay(delayMs);
                diagnosticsClient.ResumeRuntime();
                return;
            }
            catch (ServerNotAvailableException) when (attempt < maxRetries)
            {
                delayMs *= 2;
            }
        }
        diagnosticsClient.ResumeRuntime();
    }
}
