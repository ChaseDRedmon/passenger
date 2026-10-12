using System.Collections.Concurrent;
using System.Text;
using Avalonia.Threading;
using Porta.Pty;

namespace Passenger.Terminal;

/// <summary>A shell in a pseudo-terminal, as Rider's terminal runs one: the user's login shell in the folder, its
/// output read off the window's thread and fed to the <see cref="TerminalScreen"/> on it, keys and pastes written to
/// it, its size kept to the view's. Porta.Pty makes the pseudo-terminal (forkpty in a native shim on macOS and Linux,
/// ConPTY on Windows).</summary>
public sealed class TerminalSession : IAsyncDisposable
{
    private readonly ConcurrentQueue<byte[]> arrived = new();
    private IPtyConnection? pty;
    private int posted;

    public TerminalSession(int columns = 80, int rows = 24)
    {
        Screen = new TerminalScreen(columns, rows);
        Screen.Reply += Write;
    }

    public TerminalScreen Screen { get; }

    /// <summary>Raised on the window's thread when output has been taken onto the screen.</summary>
    public event EventHandler? Output;

    /// <summary>Raised on the window's thread when the shell has ended, with its exit code.</summary>
    public event EventHandler<int>? Exited;

    public bool IsRunning { get; private set; }

    /// <summary>The shell's process id, once it runs.</summary>
    public int? ProcessId => pty?.Pid;

    /// <summary>Starts the shell in a folder. <paramref name="shell"/> names one other than the user's own.</summary>
    public async Task StartAsync(string folder, string? shell = null, IReadOnlyList<string>? arguments = null)
    {
        (string app, string[] args) = shell is null ? DefaultShell() : (shell, [.. arguments ?? []]);
        Dictionary<string, string> environment = new()
        {
            ["TERM"] = "xterm-256color",
            ["COLORTERM"] = "truecolor",
            ["TERM_PROGRAM"] = "Passenger",
        };
        // An app opened from the Finder has no LANG, and a shell without one misreads every UTF-8 character.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LANG")) && !OperatingSystem.IsWindows())
        {
            environment["LANG"] = "en_US.UTF-8";
        }
        PtyOptions options = new()
        {
            Name = "Passenger",
            Cols = Screen.Columns,
            Rows = Screen.Rows,
            Cwd = Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            App = app,
            CommandLine = args,
            Environment = environment,
        };
        pty = await PtyProvider.SpawnAsync(options, CancellationToken.None);
        IsRunning = true;
        pty.ProcessExited += (_, e) => Dispatcher.UIThread.Post(() =>
        {
            IsRunning = false;
            Exited?.Invoke(this, e.ExitCode);
        });
        Stream reader = pty.ReaderStream;
        _ = Task.Run(() => ReadAsync(reader));
    }

    /// <summary>Text for the shell, as typed.</summary>
    public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

    public void Write(byte[] bytes)
    {
        if (pty is not { } connection || !IsRunning || bytes.Length == 0)
        {
            return;
        }
        try
        {
            connection.WriterStream.Write(bytes, 0, bytes.Length);
            connection.WriterStream.Flush();
        }
        catch (IOException)
        {
            // The shell has gone; its exit says so.
        }
    }

    /// <summary>A paste: line breaks as Enter sends them, bracketed when the program asked (so a shell takes it as
    /// text and runs none of its lines).</summary>
    public void Paste(string text)
    {
        string lines = text.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r');
        Write(Screen.BracketedPaste ? $"\x1b[200~{lines}\x1b[201~" : lines);
    }

    public void Resize(int columns, int rows)
    {
        if (columns == Screen.Columns && rows == Screen.Rows)
        {
            return;
        }
        Screen.Resize(columns, rows);
        try
        {
            pty?.Resize(columns, rows);
        }
        catch (Exception failed) when (failed is IOException or InvalidOperationException)
        {
            // A shell that has just ended has no size to set.
        }
    }

    public void Kill()
    {
        try
        {
            pty?.Kill();
        }
        catch (Exception failed) when (failed is IOException or InvalidOperationException)
        {
            // Already gone.
        }
    }

    public ValueTask DisposeAsync()
    {
        if (IsRunning)
        {
            Kill();
        }
        pty?.Dispose();
        pty = null;
        return ValueTask.CompletedTask;
    }

    /// <summary>The user's shell as a login shell, as Rider starts it: $SHELL (zsh where unset on macOS, bash on
    /// Linux); PowerShell on Windows, the newer one when it is installed.</summary>
    public static (string App, string[] Arguments) DefaultShell()
    {
        if (OperatingSystem.IsWindows())
        {
            string? pwsh = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(folder => Path.Combine(folder, "pwsh.exe"))
                .FirstOrDefault(File.Exists);
            return (pwsh ?? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), ["-NoLogo"]);
        }
        string shell = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } own && File.Exists(own)
            ? own
            : OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/bash";
        return (shell, ["-l"]);
    }

    /// <summary>Reads the shell's output as it comes and hands it to the window's thread in batches, so a flood of
    /// output is one screen update a frame rather than one a read.</summary>
    private async Task ReadAsync(Stream reader)
    {
        byte[] buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                int read = await reader.ReadAsync(buffer);
                if (read <= 0)
                {
                    break;
                }
                arrived.Enqueue(buffer[..read]);
                if (Interlocked.Exchange(ref posted, 1) == 0)
                {
                    Dispatcher.UIThread.Post(TakeOutput, DispatcherPriority.Input);
                }
            }
        }
        catch (Exception failed) when (failed is IOException or ObjectDisposedException)
        {
            // The pseudo-terminal closed with the shell.
        }
    }

    private void TakeOutput()
    {
        Interlocked.Exchange(ref posted, 0);
        bool any = false;
        while (arrived.TryDequeue(out byte[]? chunk))
        {
            Screen.Feed(chunk);
            any = true;
        }
        if (any)
        {
            Output?.Invoke(this, EventArgs.Empty);
        }
    }
}
