using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using Passenger.Text;

namespace Passenger.App.Run;

/// <summary>A child process whose output and errors stream into a console as they arrive: read off
/// the UI thread, appended on it in batches, terminal colour codes taken out, standard error in the
/// error colour. Stopping kills the whole process tree, as a run's Stop button does.</summary>
public sealed partial class ProcessRunner
{
    private readonly object gate = new();
    private readonly List<(StringBuilder Text, bool Error)> pending = [];
    private readonly StringBuilder partialLine = new();
    private readonly ToolWindows.OutputView console;
    private Process? process;
    private bool running;
    private bool flushQueued;

    public ProcessRunner(ToolWindows.OutputView console) => this.console = console;

    /// <summary>A whole line of output, for whoever reads it (the build's error list); UI thread.</summary>
    public event EventHandler<string>? Line;

    /// <summary>The process ended, with its exit code; UI thread.</summary>
    public event EventHandler<int>? Exited;

    public TextDocument Output => console.Document;

    /// <summary>From the start until <see cref="Exited"/> is raised, so nothing starts again before whoever waits hears the end.</summary>
    public bool IsRunning => running;

    /// <summary>Whether the last run ended because Stop was pressed.</summary>
    public bool WasStopped { get; private set; }

    /// <summary>Starts the .NET command line; why it could not, or null once it runs.</summary>
    public string? StartDotnet(IReadOnlyList<string> arguments, string folder, IReadOnlyDictionary<string, string>? environment = null)
    {
        string host;
        try
        {
            host = Lsp.Dotnet.Host();
        }
        catch (FileNotFoundException missing)
        {
            console.Append($"{missing.Message}\n", error: true);
            return missing.Message;
        }
        return Start(host, arguments, folder, environment);
    }

    /// <summary>Starts a program; why it could not (a working folder gone, a missing program), or null once it runs.</summary>
    public string? Start(string file, IReadOnlyList<string> arguments, string folder, IReadOnlyDictionary<string, string>? environment = null)
    {
        UiThread.Check();
        if (IsRunning)
        {
            throw new InvalidOperationException("The process is still running.");
        }
        WasStopped = false;
        ProcessStartInfo start = new(file)
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        foreach ((string key, string value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[key] = value;
        }
        Output.Append($"{file} {string.Join(' ', arguments.Select(Quote))}\n");
        Process started = new() { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            started.Start();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            started.Dispose();
            string problem = Directory.Exists(folder) ? $"Could not start {Path.GetFileName(file)}: {error.Message}" : $"Could not start {Path.GetFileName(file)}: the folder {folder} is gone";
            console.Append($"{problem}\n", error: true);
            return problem;
        }
        process = started;
        running = true;
        Task outputs = Task.WhenAll(Pump(started.StandardOutput, error: false), Pump(started.StandardError, error: true));
        _ = outputs.ContinueWith(_ =>
        {
            started.WaitForExit();
            int code = started.ExitCode;
            UiThread.Post(() =>
            {
                Flush();
                if (partialLine.Length > 0)
                {
                    Line?.Invoke(this, partialLine.ToString());
                    partialLine.Clear();
                }
                Output.Append($"\nProcess finished with exit code {code}.\n");
                running = false;
                Exited?.Invoke(this, code);
            });
        }, TaskScheduler.Default);
        return null;
    }

    public void Stop()
    {
        UiThread.Check();
        if (running && process is { HasExited: false } alive)
        {
            WasStopped = true;
            try
            {
                alive.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It ended on its own as Stop was pressed.
            }
        }
    }

    private async Task Pump(StreamReader reader, bool error)
    {
        char[] buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            lock (gate)
            {
                if (pending.Count == 0 || pending[^1].Error != error)
                {
                    pending.Add((new StringBuilder(), error));
                }
                pending[^1].Text.Append(buffer, 0, read);
                if (flushQueued)
                {
                    continue;
                }
                flushQueued = true;
            }
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
        }
    }

    private void Flush()
    {
        List<(string Text, bool Error)> chunks;
        lock (gate)
        {
            chunks = [.. pending.Select(p => (Ansi().Replace(p.Text.Replace("\r\n", "\n").ToString(), string.Empty), p.Error))];
            pending.Clear();
            flushQueued = false;
        }
        foreach ((string chunk, bool error) in chunks)
        {
            console.Append(chunk, error);
        }
        string text = string.Concat(chunks.Select(c => c.Text));
        if (text.Length == 0)
        {
            return;
        }
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

    private static string Quote(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07")]
    private static partial Regex Ansi();
}
