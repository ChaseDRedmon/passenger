using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using Passenger.Text;

namespace Passenger.App.Run;

/// <summary>A child process whose output and errors stream into a document as they arrive: read off
/// the UI thread, appended on it in batches, terminal colour codes taken out. Stopping kills the
/// whole process tree, as a run's Stop button does.</summary>
public sealed partial class ProcessRunner
{
    private readonly object gate = new();
    private readonly StringBuilder pending = new();
    private readonly StringBuilder partialLine = new();
    private Process? process;
    private bool flushQueued;

    public ProcessRunner(TextDocument output) => Output = output;

    /// <summary>A whole line of output, for whoever reads it (the build's error list); UI thread.</summary>
    public event EventHandler<string>? Line;

    /// <summary>The process ended, with its exit code; UI thread.</summary>
    public event EventHandler<int>? Exited;

    public TextDocument Output { get; }

    public bool IsRunning => process is { HasExited: false };

    public void Start(string file, IReadOnlyList<string> arguments, string folder, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("The process is still running.");
        }
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
        started.Start();
        process = started;
        Task outputs = Task.WhenAll(Pump(started.StandardOutput), Pump(started.StandardError));
        _ = outputs.ContinueWith(_ =>
        {
            started.WaitForExit();
            int code = started.ExitCode;
            Dispatcher.UIThread.Post(() =>
            {
                Flush();
                if (partialLine.Length > 0)
                {
                    Line?.Invoke(this, partialLine.ToString());
                    partialLine.Clear();
                }
                Output.Append($"\nProcess finished with exit code {code}.\n");
                Exited?.Invoke(this, code);
            });
        }, TaskScheduler.Default);
    }

    public void Stop()
    {
        if (process is { HasExited: false } running)
        {
            running.Kill(entireProcessTree: true);
        }
    }

    private async Task Pump(StreamReader reader)
    {
        char[] buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            lock (gate)
            {
                pending.Append(buffer, 0, read);
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
        string text;
        lock (gate)
        {
            text = pending.ToString();
            pending.Clear();
            flushQueued = false;
        }
        if (text.Length == 0)
        {
            return;
        }
        text = Ansi().Replace(text.Replace("\r\n", "\n"), string.Empty);
        Output.Append(text);
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
