using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Remote.Protocol;
using Avalonia.Remote.Protocol.Designer;
using Avalonia.Remote.Protocol.Viewport;
using RemoteFormat = Avalonia.Remote.Protocol.Viewport.PixelFormat;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Xaml.Projects;

namespace Passenger.App.Xaml;

/// <summary>One XAML file's design view, as Avalonia's own previewer draws it: the project's Avalonia.Designer.HostApp
/// (in its Avalonia package, MIT) run through ChildProcesses on the project's last build, its frames sent over
/// Avalonia.Remote.Protocol to a port of Passenger's on loopback, and the file's text as it stands sent to it as it is
/// typed. The host honours what Avalonia's design-time support says (d:DesignWidth, Design.DataContext,
/// Design.PreviewWith). A UI-thread object: the protocol's messages cross to it through <see cref="UiThread"/>.</summary>
public sealed class XamlPreview : IDisposable
{
    /// <summary>How long an update waits for the host's answer before what it held is let go.</summary>
    private static readonly TimeSpan Answer = TimeSpan.FromSeconds(60);

    private readonly XamlDocument document;
    private readonly Queue<string> said = new();
    private IDisposable? listening;
    private IAvaloniaRemoteTransportConnection? connection;
    private Process? host;
    private IDisposable? waiting;
    private string? output;
    private bool disposed;
    private Stopwatch? starting;
    private bool empty;
    private double drawnAt;
    private double scaling = 1;

    public XamlPreview(XamlDocument document) => this.document = document;

    /// <summary>The frame, its status or its error changed; raised on the UI thread.</summary>
    public event EventHandler? Changed;

    /// <summary>The last frame the host drew, its pixels at 96 DPI whatever the host drew it at: an image control draws
    /// a bitmap's size in its own units from its pixels, so one at the screen's DPI shows a quarter of itself enlarged.</summary>
    public WriteableBitmap? Frame { get; private set; }

    /// <summary>The frame's size in the window's units: its pixels over the scale the host drew it at.</summary>
    public Size FrameSize { get; private set; }

    /// <summary>What the preview is doing, or why it shows nothing.</summary>
    public string Status { get; private set; } = "Starting the previewer…";

    /// <summary>What the host said of the XAML it could not load, with its line; null when the last load went well.</summary>
    public string? Error { get; private set; }

    /// <summary>Starts the host on the project's last build, once the project's XAML has been read.</summary>
    public async Task StartAsync()
    {
        UiThread.Check();
        if (await document.Loading.Builder is not { } builder || await document.Loaded is null || disposed)
        {
            Say("Previews are for the XAML of an Avalonia project");
            return;
        }
        ProjectInputs inputs = builder.Current.Inputs;
        if (Output(inputs) is not { } built)
        {
            Say($"Build {Path.GetFileNameWithoutExtension(inputs.ProjectPath)} to preview its XAML: the previewer draws its last build");
            return;
        }
        if (HostApp(inputs) is not { } designer)
        {
            Say("The project's Avalonia package has no previewer (tools/…/designer/Avalonia.Designer.HostApp.dll)");
            return;
        }
        output = built;
        try
        {
            int port = FreePort();
            listening = new BsonTcpTransport().Listen(IPAddress.Loopback, port, UiThread.Marshal<IAvaloniaRemoteTransportConnection>(Connected));
            string folder = Path.GetDirectoryName(built)!;
            string name = Path.GetFileNameWithoutExtension(built);
            ProcessStartInfo start = ChildProcesses.DotnetCommand(["exec", "--runtimeconfig", Path.Combine(folder, name + ".runtimeconfig.json"),
                "--depsfile", Path.Combine(folder, name + ".deps.json"), designer, "--transport", $"tcp-bson://127.0.0.1:{port}/", "--method", "avalonia-remote",
                "--session-id", Guid.NewGuid().ToString("N"), built], folder);
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            Process started = ChildProcesses.Start(start);
            host = started;
            started.OutputDataReceived += (_, e) => Heard(e.Data);
            started.ErrorDataReceived += (_, e) => Heard(e.Data);
            started.Exited += (_, _) => UiThread.Post(() => Ended(started));
            started.BeginOutputReadLine();
            started.BeginErrorReadLine();
            starting = Stopwatch.StartNew();
            Say($"Starting the previewer on {Path.GetFileName(built)}…");
            Hold();
        }
        catch (Exception error) when (error is SocketException or IOException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            Say($"The previewer could not start: {error.Message}");
        }
    }

    /// <summary>The file's text as it stands, sent to the host to draw.</summary>
    public void Update()
    {
        UiThread.Check();
        if (connection is not { } open || output is null || disposed)
        {
            return;
        }
        Hold();
        _ = open.Send(new UpdateXamlMessage { Xaml = document.Document.Text.Snapshot.ToString(), AssemblyPath = output, XamlFileProjectPath = ProjectPath() });
    }

    private void Connected(IAvaloniaRemoteTransportConnection opened)
    {
        if (disposed)
        {
            opened.Dispose();
            return;
        }
        connection = opened;
        opened.OnMessage += (_, message) => UiThread.Post(() => Received(opened, message));
        opened.OnException += (_, error) => UiThread.Post(() => Say($"The previewer's connection failed: {error.Message}"));
        opened.Start();
        double dpi = 96 * Scaling;
        _ = opened.Send(new ClientSupportedPixelFormatsMessage { Formats = [RemoteFormat.Bgra8888, RemoteFormat.Rgba8888] });
        _ = opened.Send(new ClientRenderInfoMessage { DpiX = dpi, DpiY = dpi });
        Update();
    }

    private void Received(IAvaloniaRemoteTransportConnection from, object message)
    {
        if (disposed || from != connection)
        {
            return;
        }
        switch (message)
        {
            case RequestViewportResizeMessage resize:
                // Only said, never answered: an allocation in reply lays a root sized to its content out again, which asks
                // again, frame after frame. A root of no size (hidden, or empty with no design size) is never drawn.
                empty = resize.Width < 1 || resize.Height < 1;
                break;
            case UpdateXamlResultMessage result:
                Error = result.Error is { Length: > 0 } ? Describe(result) : null;
                if (Error is not null)
                {
                    Let();
                }
                else if (Frame is null || Math.Abs(drawnAt - (96 * Scaling)) > 0.5)
                {
                    // The host's window takes its scale from what arrives once it exists; a reload keeps the one it had.
                    double scale = 96 * Scaling;
                    _ = from.Send(new ClientRenderInfoMessage { DpiX = scale, DpiY = scale });
                }
                Status = Error is null ? "Live" : "The XAML did not load";
                if (Error is null && empty)
                {
                    Frame = null;
                    Status = "Nothing to draw: the root is 0 × 0 (hidden, or sized to content with none and no d:DesignWidth and d:DesignHeight)";
                    Let();
                }
                Changed?.Invoke(this, EventArgs.Empty);
                break;
            case FrameMessage frame:
                Draw(frame);
                if (starting is { } clock)
                {
                    starting = null;
                    IdeErrors.Write($"Preview of {Path.GetFileName(document.Document.Path)}: first frame {frame.Width}x{frame.Height} after {clock.ElapsedMilliseconds} ms");
                }
                _ = from.Send(new FrameReceivedMessage { SequenceId = frame.SequenceId });
                Status = "Live";
                Changed?.Invoke(this, EventArgs.Empty);
                Let();
                break;
        }
    }

    /// <summary>A frame's pixels in a bitmap the pane shows, row by row as its stride lays them out.</summary>
    private void Draw(FrameMessage frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Data is null)
        {
            return;
        }
        Avalonia.Platform.PixelFormat format = frame.Format == RemoteFormat.Rgba8888 ? Avalonia.Platform.PixelFormat.Rgba8888 : Avalonia.Platform.PixelFormat.Bgra8888;
        WriteableBitmap bitmap = new(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), format, AlphaFormat.Premul);
        using (ILockedFramebuffer target = bitmap.Lock())
        {
            int row = frame.Width * 4;
            for (int y = 0; y < frame.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(frame.Data, y * frame.Stride, target.Address + (y * target.RowBytes), row);
            }
        }
        Frame = bitmap;
        drawnAt = frame.DpiX;
        FrameSize = new Size(frame.Width * 96 / frame.DpiX, frame.Height * 96 / frame.DpiY);
    }

    /// <summary>What the host said of a XAML it could not load, with the line it names.</summary>
    private static string Describe(UpdateXamlResultMessage result)
    {
        if (result.Exception is { } details)
        {
            string where = details.LineNumber is { } line ? $" (line {line}{(details.LinePosition is { } column ? $", column {column}" : string.Empty)})" : string.Empty;
            return $"{details.ExceptionType}: {details.Message}{where}";
        }
        string first = result.Error.Split('\n')[0].Trim();
        return first;
    }

    /// <summary>A line the host printed, kept for what is said if it ends.</summary>
    private void Heard(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }
        lock (said)
        {
            said.Enqueue(line);
            while (said.Count > 5)
            {
                said.Dequeue();
            }
        }
    }

    private void Ended(Process ended)
    {
        if (disposed || ended != host)
        {
            return;
        }
        string last;
        lock (said)
        {
            last = said.LastOrDefault() ?? "it said nothing";
        }
        Let();
        Say($"The previewer ended: {last}");
    }

    private void Say(string status)
    {
        Status = status;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Holds screenshots and tests for the host's answer to what was just sent, a while at most.</summary>
    private void Hold()
    {
        if (waiting is not null)
        {
            return;
        }
        IDisposable held = BackgroundWork.Scope();
        waiting = held;
        _ = Task.Delay(Answer).ContinueWith(_ => UiThread.Post(() =>
        {
            if (waiting == held)
            {
                Let();
            }
        }), TaskScheduler.Default);
    }

    private void Let()
    {
        waiting?.Dispose();
        waiting = null;
    }

    /// <summary>The scale the pane's window draws at, which the host is asked to draw at too.</summary>
    public double Scaling
    {
        get => scaling;
        set
        {
            if (Math.Abs(value - scaling) < 0.01)
            {
                return;
            }
            scaling = value;
            if (connection is { } open && !disposed)
            {
                _ = open.Send(new ClientRenderInfoMessage { DpiX = 96 * value, DpiY = 96 * value });
            }
        }
    }

    /// <summary>The file's path in its project, as the host reads it (<c>/Views/MainWindow.axaml</c>).</summary>
    private string ProjectPath()
    {
        string folder = document.Loading.Folder;
        return "/" + Path.GetRelativePath(folder, document.Document.Path).Replace('\\', '/');
    }

    /// <summary>The project's newest build output that can run: its assembly with its runtime configuration and
    /// dependencies beside it, under its own bin/ or an artifacts folder above it.</summary>
    public static string? Output(ProjectInputs inputs)
    {
        string name = inputs.AssemblyName + ".dll";
        List<string> found = [];
        string bin = Path.Combine(inputs.Folder, "bin");
        if (Directory.Exists(bin))
        {
            found.AddRange(Directory.EnumerateFiles(bin, name, SearchOption.AllDirectories));
        }
        string project = Path.GetFileNameWithoutExtension(inputs.ProjectPath);
        for (DirectoryInfo? at = new(inputs.Folder); at is not null; at = at.Parent)
        {
            string artifacts = Path.Combine(at.FullName, "artifacts", "bin", project);
            if (Directory.Exists(artifacts))
            {
                found.AddRange(Directory.EnumerateFiles(artifacts, name, SearchOption.AllDirectories));
                break;
            }
        }
        return found.Where(f => File.Exists(Path.ChangeExtension(f, ".runtimeconfig.json")) && File.Exists(Path.ChangeExtension(f, ".deps.json"))
                && !f.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    /// <summary>Avalonia's previewer host in the project's Avalonia package: the newest of its tools/ frameworks that is not
    /// newer than the project's.</summary>
    private static string? HostApp(ProjectInputs inputs)
    {
        if (!inputs.PackageFolders.TryGetValue("Avalonia", out string? package) || !Directory.Exists(Path.Combine(package, "tools")))
        {
            return null;
        }
        Version target = Framework(inputs.TargetFramework) ?? new Version(99, 0);
        return Directory.EnumerateDirectories(Path.Combine(package, "tools"))
            .Select(d => (Folder: d, Version: Framework(Path.GetFileName(d))))
            .Where(d => d.Version is not null && d.Version <= target)
            .OrderByDescending(d => d.Version)
            .Select(d => Path.Combine(d.Folder, "designer", "Avalonia.Designer.HostApp.dll"))
            .FirstOrDefault(File.Exists);
    }

    private static Version? Framework(string? moniker) =>
        moniker is not null && moniker.StartsWith("net", StringComparison.Ordinal) && Version.TryParse(moniker[3..].Split('-')[0], out Version? version) && version.Major >= 5
            ? version
            : null;

    private static int FreePort()
    {
        TcpListener free = new(IPAddress.Loopback, 0);
        free.Start();
        int port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        return port;
    }

    /// <summary>The host started again on the project's newest build, the last frame shown until the new one comes.</summary>
    public Task RestartAsync()
    {
        UiThread.Check();
        if (disposed)
        {
            return Task.CompletedTask;
        }
        Stop();
        return StartAsync();
    }

    private void Stop()
    {
        Let();
        connection?.Dispose();
        connection = null;
        listening?.Dispose();
        listening = null;
        output = null;
        if (host is { } running)
        {
            host = null;
            ChildProcesses.Kill(running);
            running.Dispose();
        }
    }

    public void Dispose()
    {
        disposed = true;
        Stop();
    }
}
