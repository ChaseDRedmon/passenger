using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.Core;

namespace Passenger.App;

/// <summary>Links, as Rider has them: the Run, Debug, Build and Unit Tests consoles' stack frames, compiler messages and web
/// addresses (a click opens the file at its line, or the address in the browser) and the terminal's (its file paths on
/// Cmd+click, Ctrl+click elsewhere); and Rider's Open browser after launch, which opens a web project's launch URL once its
/// program listens.</summary>
public partial class MainWindow
{
    private readonly List<Uri> browsed = [];

    /// <summary>Ends the wait for the last launch's program to listen, once it ended or another launched.</summary>
    private CancellationTokenSource? browserWait;

    /// <summary>Every address the window opened in the person's browser, in order: what a test reads, as the headless
    /// platform's launcher opens nothing.</summary>
    public IReadOnlyList<Uri> Browsed => browsed;

    private void AddLinks()
    {
        foreach (OutputView console in (OutputView[])[Bottom.Run, Bottom.Build, Bottom.Debug.Console])
        {
            console.LinkClicked += (_, target) => OpenLink(target);
        }
        Bottom.Tests.LinkClicked += (_, target) => OpenLink(target);
        // The project folder the Run window's program and the Debug window's came from is looked in first.
        Bottom.Run.Links.Folders = () => LinkFolders(buildRun.RunProject);
        Bottom.Debug.Console.Links.Folders = () => LinkFolders(buildRun.DebugProject);
        Bottom.Build.Links.Folders = () => LinkFolders(null);
        Bottom.Tests.Links.Folders = () => LinkFolders(null);
        AddTerminalLinks();
    }

    /// <summary>The terminal's links, Rider's terminal's: web addresses and stack frames as the consoles find them, and any
    /// path to a file, a relative one under the folder the shell said it is in, then the open folder's.</summary>
    private void AddTerminalLinks()
    {
        ConsoleLinks links = new() { FilePaths = true };
        string? shellFolder = null;
        links.Folders = () => LinkFolders(shellFolder);
        terminal!.View.Links = text =>
        {
            string? at = terminal.Active?.Screen.WorkingDirectory;
            if (at != shellFolder)
            {
                shellFolder = at;
                links.Forget();
            }
            return [.. links.In(text).Select(link => (link.Start, link.Length, (object)link.Target, link.Hidden))];
        };
        terminal.View.LinkClicked += (_, target) => OpenLink((LinkTarget)target);
    }

    /// <summary>Where a relative path in a console is looked for: the project its output came from, the open folder, then
    /// the folder's projects.</summary>
    private IReadOnlyList<string> LinkFolders(string? project)
    {
        List<string> folders = [];
        if (project is not null)
        {
            folders.Add(project);
        }
        if (Folder is not null)
        {
            folders.Add(Folder);
        }
        folders.AddRange(Projects.Select(p => p.Folder).Where(f => !folders.Contains(f)));
        return folders;
    }

    /// <summary>Opens what a link names: a file at its line and column in the editor, or an address in the browser.</summary>
    public void OpenLink(LinkTarget target)
    {
        UiThread.Check();
        switch (target)
        {
            case FileLink file:
                NavigateTo(file.Path, file.Position);
                if (Editors.Active is { } active && PathNames.Same(active.Path, file.Path))
                {
                    Editors.CodeEditor.Focus();
                }
                break;
            case WebLink web:
                Browse(web.Address);
                break;
        }
    }

    /// <summary>Opens an address in the person's browser, through the system's launcher as every link the IDE opens is:
    /// the browser is the person's, never ended with the IDE.</summary>
    public void Browse(Uri address)
    {
        UiThread.Check();
        browsed.Add(address);
        _ = Launcher.LaunchUriAsync(address);
    }

    /// <summary>Rider's Open browser after launch: the plan's browser URL opens once its program listens there, as
    /// IntelliJ's browser starter waits for the address's port to take a connection; never once the program ended, nor
    /// for a launch that came after it.</summary>
    private void OpenBrowserOnceListening(LaunchPlan plan, Task ended)
    {
        browserWait?.Cancel();
        browserWait = null;
        if (BrowserLaunch.Address(plan.BrowserUrl) is not { } address)
        {
            return;
        }
        CancellationTokenSource waiting = browserWait = new CancellationTokenSource();
        _ = ended.ContinueWith(_ => waiting.Cancel(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        // Not background work: it lasts as long as the program takes to listen, which no screenshot or settling test waits for.
        _ = Task.Run(async () =>
        {
            bool? listening = await BrowserLaunch.ListeningAsync(address, waiting.Token);
            if (listening is null)
            {
                return;
            }
            UiThread.Post(() =>
            {
                if (waiting.IsCancellationRequested)
                {
                    return;
                }
                if (listening == true)
                {
                    Browse(BrowserLaunch.Shown(address));
                }
                else
                {
                    StatusText.Text = BrowserLaunch.NotResponding(address);
                }
            });
        });
    }
}
