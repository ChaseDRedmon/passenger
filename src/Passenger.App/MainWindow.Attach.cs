using Passenger.App.Debugging;
using Passenger.Core;
using Passenger.Editor;
using A = Passenger.App.AppActions;

namespace Passenger.App;

/// <summary>Rider's Attach to Process and Reattach to Process: the .NET programs running on this machine, listed in a
/// dialog, and the debugger attached to the one chosen as a session like any other, which Detach (the debugger's own
/// action) lets go of again with the program running on. On macOS nothing more is asked of the system than of a program
/// the debugger starts: CoreCLR's debugger reaches the runtime through the pipes it opens for a debugger in the user's
/// temporary folder, never through the task port Apple's debugger entitlement and get-task-allow guard.</summary>
public partial class MainWindow
{
    /// <summary>The programs attached to in this window, newest last, as Rider's Recently attached keeps them: by name and
    /// command line too, so one started again under another process id is found again.</summary>
    private readonly List<DotnetProcess> attachedTo = [];

    /// <summary>The last session Attach to Process or Reattach opened, which Rerun reattaches.</summary>
    private Debugging.DebugSession? attachSession;

    /// <summary>The Attach to Process dialog open over the window, or null.</summary>
    public AttachToProcessDialog? AttachToProcessDialog { get; private set; }

    /// <summary>Attach to Process and Reattach to Process, in Rider's Run menu before Edit Configurations.</summary>
    private void AddAttaching()
    {
        Add(A.AttachToProcess, "Attach to Process...", "Debug", _ => ShowAttachToProcess(), menu: ("Run", 0, 3));
        Add(A.ReattachToProcess, "Reattach to Process...", "Debug", context => _ = Reattach(),
            _ => attachedTo.Count == 0 ? "Nothing has been attached to in this window: Attach to Process chooses a program" : null, ("Run", 0, 4));
    }

    /// <summary>The dialog over the window, searched for what is given; the program chosen there is attached to.</summary>
    public AttachToProcessDialog ShowAttachToProcess(string search = "")
    {
        AttachToProcessDialog dialog = new(() => BackgroundWork.Run(DotnetProcesses.List), WasAttached, search);
        AttachToProcessDialog = dialog;
        dialog.Closed += (_, _) =>
        {
            if (AttachToProcessDialog == dialog)
            {
                AttachToProcessDialog = null;
            }
        };
        _ = Chosen();
        return dialog;

        async Task Chosen()
        {
            if (await dialog.ShowDialog<DotnetProcess?>(this) is { } chosen && await AttachAsync(chosen) is { } problem)
            {
                status.Text = problem;
            }
        }
    }

    /// <summary>Attaches the debugger to a .NET program, the Debug window open on its frames: null once the session is open,
    /// else why it could not, as the status bar says. A session the debugger attached to before is let go of first, the
    /// program running on, and one it started is stopped, as Debug stops it.</summary>
    public async Task<string?> AttachAsync(DotnetProcess process)
    {
        UiThread.Check();
        using IDisposable held = BackgroundWork.Scope();
        if (session.Debug.Session is { Attached: true, State: not Debugging.DebugState.Ended })
        {
            await session.Debug.DetachAsync();
        }
        attachedTo.RemoveAll(p => Same(p, process));
        attachedTo.Add(process);
        ShowToolWindow("debug");
        Bottom.Debug.ShowTab(console: false);
        string? refused = await session.Debug.AttachAsync(process.Id, process.Program);
        attachSession = session.Debug.Session;
        return refused;
    }

    /// <summary>Rider's Reattach to Process: the program last attached to, by its process while it runs, else the one
    /// running under its name and command line, or its name; the dialog, searched for it, when several are or none is.</summary>
    private async Task Reattach()
    {
        DotnetProcess last = attachedTo[^1];
        status.Text = $"Looking for {last.Program} to attach to";
        IReadOnlyList<DotnetProcess> running = await BackgroundWork.Run(DotnetProcesses.List);
        List<DotnetProcess> candidates = running.FirstOrDefault(p => p.Id == last.Id && Same(p, last)) is { } same ? [same]
            : running.Where(p => Same(p, last)).ToList() is { Count: > 0 } alike ? alike
            : [.. running.Where(p => p.Program == last.Program)];
        if (candidates is [{ } only])
        {
            if (await AttachAsync(only) is { } problem)
            {
                status.Text = problem;
            }
            return;
        }
        status.Text = candidates.Count == 0
            ? $"Reattach to Process: no .NET process named {last.Program} is running"
            : $"Reattach to Process: {candidates.Count} programs are named {last.Program}; choose one";
        ShowAttachToProcess(last.Program);
    }

    private bool WasAttached(DotnetProcess process) => attachedTo.Any(p => Same(p, process));

    private static bool Same(DotnetProcess one, DotnetProcess other) => one.Name == other.Name && one.CommandLine == other.CommandLine;
}
