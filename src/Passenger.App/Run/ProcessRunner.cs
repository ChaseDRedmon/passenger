using System.Diagnostics;
using System.Text;
using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Run;

/// <summary>A child process whose output and errors stream into a console as they arrive, through its sink: read off the
/// UI thread, colour codes taken out, standard error in the error colour. A program run (not a build) reads what the
/// person types into the console, as in Rider; a build's input is closed, so a step that reads ends rather than waits.
/// Stopping kills the whole process tree, as a run's Stop button does. The console is the window's; a runner lets go of it
/// when disposed, its folder left, and another folder's runner writes there.</summary>
public sealed class ProcessRunner : IDisposable
{
    private readonly StringBuilder partialLine = new();
    private readonly OutputView console;
    private readonly bool takesInput;
    private Process? process;
    private bool running;

    /// <summary>The wait of a run whose process has yet to start, which Stop ends.</summary>
    private CancellationTokenSource? waiting;
    private TaskCompletionSource<int> ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ProcessRunner(OutputView console, bool takesInput = false)
    {
        this.console = console;
        this.takesInput = takesInput;
        console.Written += OnWritten;
    }

    private void OnWritten(object? sender, string text)
    {
        if (running && Line is not null)
        {
            Lines(text);
        }
    }

    /// <summary>Lets go of the console, its process stopped first: nothing more it reads is taken as its lines.</summary>
    public void Dispose() => console.Written -= OnWritten;

    /// <summary>A whole line of output, for whoever reads it (the build's error list); UI thread.</summary>
    public event EventHandler<string>? Line;

    /// <summary>The process ended, with its exit code; UI thread.</summary>
    public event EventHandler<int>? Exited;

    public TextDocument Output => console.Document;

    /// <summary>What the console shows of the process now, what is on its way to it taken in first: what an agent reads of
    /// a run, rather than the window's view of it.</summary>
    public TextSnapshot Shown()
    {
        console.Flush();
        return console.Document.Snapshot;
    }

    /// <summary>From the start until <see cref="Exited"/> is raised, so nothing starts again before whoever waits hears the end.</summary>
    public bool IsRunning => running;

    /// <summary>Whether the last run ended because Stop was pressed.</summary>
    public bool WasStopped { get; private set; }

    /// <summary>Why the last run's process could not start once its wait was over; null for one that started.</summary>
    public string? Unstarted { get; private set; }

    /// <summary>The process last started (dotnet run, dotnet watch, a build), for a test to see it gone.</summary>
    public int? ProcessId => process?.Id;

    /// <summary>The exit code of the process last started, once <see cref="Exited"/> has said it ended: its own end,
    /// whatever starts after it.</summary>
    public Task<int> Ended => ended.Task;

    /// <summary>Starts the .NET command line; why it could not, or null once it runs. Given a wait, it runs from now as its
    /// callers see it (<see cref="IsRunning"/>, <see cref="Stop"/>, <see cref="Ended"/>) and its process starts once the
    /// wait is over; stopped meanwhile, or unable to start then (<see cref="Unstarted"/>), it ends with exit code -1.</summary>
    public string? StartDotnet(IReadOnlyList<string> arguments, string folder, IReadOnlyDictionary<string, string>? environment = null,
        Func<CancellationToken, Task>? after = null)
    {
        ProcessStartInfo start;
        try
        {
            start = ChildProcesses.DotnetCommand(arguments, folder);
        }
        catch (FileNotFoundException missing)
        {
            console.Append($"{missing.Message}\n", error: true);
            return missing.Message;
        }
        return Start(start, environment, after);
    }

    /// <summary>Starts a program; why it could not (a working folder gone, a missing program), or null once it runs.</summary>
    public string? Start(string file, IReadOnlyList<string> arguments, string folder, IReadOnlyDictionary<string, string>? environment = null) =>
        Start(ChildProcesses.Command(file, arguments, folder), environment);

    private string? Start(ProcessStartInfo start, IReadOnlyDictionary<string, string>? environment, Func<CancellationToken, Task>? after = null)
    {
        UiThread.Check();
        if (IsRunning)
        {
            throw new InvalidOperationException("The process is still running.");
        }
        WasStopped = false;
        Unstarted = null;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        // Otherwise ChildProcesses closes it as the program starts.
        start.RedirectStandardInput = takesInput;
        foreach ((string key, string value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[key] = value;
        }
        if (after is null)
        {
            return Launch(start, null);
        }
        running = true;
        _ = LaunchAfterAsync(start, after, ended = new(TaskCreationOptions.RunContinuationsAsynchronously));
        return null;
    }

    /// <summary>The process started once its wait is over, unless it was stopped meanwhile.</summary>
    private async Task LaunchAfterAsync(ProcessStartInfo start, Func<CancellationToken, Task> after, TaskCompletionSource<int> ending)
    {
        using (CancellationTokenSource stopping = waiting = new())
        {
            try
            {
                await after(stopping.Token);
            }
            catch (OperationCanceledException)
            {
                WasStopped = true;
            }
            catch (Exception error)
            {
                // What it waited for is a courtesy to others: the process starts anyway.
                IdeErrors.Report(error);
            }
            finally
            {
                waiting = null;
            }
        }
        if (WasStopped || (Unstarted = Launch(start, ending)) is not null)
        {
            running = false;
            ending.TrySetResult(-1);
            Exited?.Invoke(this, -1);
        }
    }

    private string? Launch(ProcessStartInfo start, TaskCompletionSource<int>? waited)
    {
        console.Append($"{ChildProcesses.Display(start)}\n");
        Process started;
        try
        {
            started = ChildProcesses.Start(start);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            string file = Path.GetFileName(start.FileName);
            string problem = Directory.Exists(start.WorkingDirectory) ? $"Could not start {file}: {error.Message}" : $"Could not start {file}: the folder {start.WorkingDirectory} is gone";
            console.Append($"{problem}\n", error: true);
            return problem;
        }
        process = started;
        running = true;
        TaskCompletionSource<int> ending = waited ?? (ended = new(TaskCreationOptions.RunContinuationsAsynchronously));
        if (takesInput)
        {
            console.Input = ProgramInput.Of(started.StandardInput);
        }
        ConsoleSink sink = console.Sink;
        Landing landing = new();
        started.Exited += (_, _) => landing.Ended();
        if (started.HasExited)
        {
            landing.Ended();
        }
        Task outputs = Task.WhenAll(Pump(started.StandardOutput, sink, ConsoleStream.Output), Pump(started.StandardError, sink, ConsoleStream.Error));
        _ = outputs.ContinueWith(_ =>
        {
            started.WaitForExit();
            int code = started.ExitCode;
            UiThread.Post(() =>
            {
                try
                {
                    console.Input = null;
                    console.Flush();
                    if (partialLine.Length > 0)
                    {
                        Line?.Invoke(this, partialLine.ToString());
                        partialLine.Clear();
                    }
                    console.Append($"\nProcess finished with exit code {code}.\n");
                    running = false;
                    ending.TrySetResult(code);
                    Exited?.Invoke(this, code);
                }
                finally
                {
                    landing.Landed();
                }
            });
        }, TaskScheduler.Default);
        return null;
    }

    /// <summary>A program's end until the console has all it wrote and says it ended: background work, so a screenshot or
    /// a test finds the last of its output and its exit code there. A program still running holds nothing, since it may
    /// never end; what it writes meanwhile is the sink's to hold as it crosses.</summary>
    private sealed class Landing
    {
        private readonly object gate = new();
        private IDisposable? held;
        private bool landed;

        /// <summary>The program has ended (its process's Exited, on any thread): held until <see cref="Landed"/>.</summary>
        public void Ended()
        {
            lock (gate)
            {
                if (!landed && held is null)
                {
                    held = BackgroundWork.Scope();
                }
            }
        }

        /// <summary>The console says it ended; an end heard only after this holds nothing.</summary>
        public void Landed()
        {
            lock (gate)
            {
                landed = true;
                held?.Dispose();
                held = null;
            }
        }
    }

    /// <summary>Ends the process and all it started, and on macOS and Linux what it left holding its output after it ended,
    /// which keeps the console running until then.</summary>
    public void Stop()
    {
        UiThread.Check();
        if (running && waiting is { } wait)
        {
            WasStopped = true;
            wait.Cancel();
        }
        else if (running && process is { } started)
        {
            WasStopped = true;
            ChildProcesses.Kill(started);
        }
    }

    /// <summary>Reads a stream into the sink as it comes, a read at a time, and ends the stream's state at its end.</summary>
    private static async Task Pump(StreamReader reader, ConsoleSink sink, ConsoleStream stream)
    {
        char[] buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            sink.Write(buffer.AsSpan(0, read), stream);
        }
        sink.End(stream);
    }

    private void Lines(string text)
    {
        partialLine.Append(text);
        string lines = partialLine.ToString();
        int end = lines.LastIndexOf('\n');
        if (end < 0)
        {
            return;
        }
        foreach (string line in lines[..end].Split('\n'))
        {
            Line?.Invoke(this, line);
        }
        partialLine.Clear();
        partialLine.Append(lines, end + 1, lines.Length - end - 1);
    }
}
