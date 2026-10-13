namespace Passenger.App;

/// <summary>What the window started ends with it: programs and builds at once, the debugger, the test runner and the
/// language server as asked, and the process waits a little for those before it goes (<see cref="Ended"/>). Close
/// Solution ends what the folder runs the same way and leaves the window on the Welcome screen.</summary>
public partial class MainWindow
{
    /// <summary>How long the process waits, once the window has closed, for the debugger, the test runner and the
    /// language server to end as asked; each kills its own process when it will not go.</summary>
    public static readonly TimeSpan EndLimit = TimeSpan.FromSeconds(5);

    /// <summary>Finishes when what the window started has ended, or <see cref="EndLimit"/> after it closed; null while it
    /// is open. It needs no UI thread, so the process waits for it once the window's loop has stopped (Program, the MCP
    /// host, a screenshot). A logout does not wait: macOS ends the process, after the programs' and builds' kills.</summary>
    public Task? Ended { get; private set; }

    private void AddShutdown()
    {
        Closed += (_, _) => EndChildren();
        actions!.Add(new IdeAction(AppActions.CloseSolution, "Close Solution", "IDE", _ => CloseSolution())
        {
            WhyNot = _ => folder is null ? "No solution is open" : null,
            Menus = [new MenuPlace("File", 0, 5)],
        });
    }

    private void EndChildren()
    {
        StopPrograms();
        _ = terminal?.CloseAllAsync();
        (string What, Task Ending)[] ending =
        [
            ("the debugger", debug.CloseAsync()),
            ("the test runner", tests.DisposeAsync().AsTask()),
            ("the language server", csharp?.DisposeAsync().AsTask() ?? Task.CompletedTask),
        ];
        // Not held: nothing it does is drawn, and the process waits for it once the window's loop has stopped.
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

    /// <summary>Programs and builds have nothing to finish: their process trees go now, so a server lets go of its port.</summary>
    private void StopPrograms()
    {
        runner.Stop();
        builder.Stop();
        nuget?.StopChange();
        profiling?.Kill();
    }

    /// <summary>Rider's Close Solution: its changed files saved as closing the window saves them (one that cannot be keeps
    /// it open, with why), its editors and layout kept with it and the solution at the top of the recent ones, everything
    /// it runs ended (programs, builds, the debugger, tests, its terminals, the language server), and the Welcome screen
    /// in the editors' place. Null once it is closed, else why not.</summary>
    public string? CloseSolution()
    {
        UiThread.Check();
        if (folder is null)
        {
            return "No solution is open";
        }
        if (!Autosave())
        {
            const string unsaved = "The solution was not closed: a changed file could not be saved";
            StatusText.Text = unsaved;
            return unsaved;
        }
        Remember();
        if (keepsWorkspace)
        {
            KeepDock();
        }
        if (!LeaveFolder())
        {
            const string open = "The solution was not closed: an open file could not be saved";
            StatusText.Text = open;
            return open;
        }
        IdeErrors.Write($"Closed {folder}");
        StopPrograms();
        _ = debug.StopAsync();
        tests.Stop();
        _ = tests.DisposeAsync();
        if (terminal is { Tabs.Count: > 0 })
        {
            Dock.Hide("terminal");
            _ = terminal.CloseAllAsync();
        }
        watcher?.Dispose();
        watcher = null;
        folder = null;
        workspace = null;
        namedSolution = null;
        reads++;
        WorkspaceRead = Task.CompletedTask;
        StartCSharp();
        Explorer.Unload();
        index.Clear();
        _ = vcs.CloseAsync();
        tests.Open(null);
        LoadConfigurations();
        problems.Clear();
        Bottom.Problems.Show(problems);
        if (Dock["problems"] is { } problemsWindow)
        {
            problemsWindow.Title = "Problems";
        }
        agentSocket?.Describe(null);
        ProjectName.Text = "No solution";
        ProjectBadge.IsVisible = false;
        Editors.ShowWelcome(welcome);
        welcome.Say(null);
        ShowRecent();
        UpdateStatus();
        welcome.FocusSearch();
        return null;
    }
}
