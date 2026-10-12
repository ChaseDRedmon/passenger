namespace Passenger.App;

/// <summary>What the window started ends with it: programs and builds at once, the debugger, the test runner and the
/// language server as asked, and the process waits a little for those before it goes (<see cref="Ended"/>).</summary>
public partial class MainWindow
{
    /// <summary>How long the process waits, once the window has closed, for the debugger, the test runner and the
    /// language server to end as asked; each kills its own process when it will not go.</summary>
    public static readonly TimeSpan EndLimit = TimeSpan.FromSeconds(5);

    /// <summary>Finishes when what the window started has ended, or <see cref="EndLimit"/> after it closed; null while it
    /// is open. It needs no UI thread, so the process waits for it once the window's loop has stopped (Program, the MCP
    /// host, a screenshot). A logout does not wait: macOS ends the process, after the programs' and builds' kills.</summary>
    public Task? Ended { get; private set; }

    private void AddShutdown() => Closed += (_, _) => EndChildren();

    private void EndChildren()
    {
        // Programs and builds have nothing to finish: their process trees go now, so a server lets go of its port.
        runner.Stop();
        builder.Stop();
        nuget?.StopChange();
        profiling?.Kill();
        _ = terminal?.CloseAllAsync();
        (string What, Task Ending)[] ending =
        [
            ("the debugger", debug.CloseAsync()),
            ("the test runner", tests.DisposeAsync().AsTask()),
            ("the language server", csharp?.DisposeAsync().AsTask() ?? Task.CompletedTask),
        ];
        Ended = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(ending.Select(e => e.Ending)).WaitAsync(EndLimit);
            }
            catch (TimeoutException)
            {
                string late = string.Join(" and ", ending.Where(e => !e.Ending.IsCompleted).Select(e => e.What));
                IdeErrors.Write($"Closing: {late} had not ended after {EndLimit.TotalSeconds:0} s");
            }
            catch (Exception error)
            {
                IdeErrors.Write($"Closing: ending what the window started failed: {error}");
            }
        });
    }
}
