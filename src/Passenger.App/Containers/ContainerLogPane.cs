using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Passenger.App.Run;
using Passenger.App.ToolWindows;

namespace Passenger.App.Containers;

/// <summary>A container's log, or a Compose project's or service's with every service's lines merged: <c>docker logs -f</c>
/// (<c>docker compose logs -f</c>) streamed into a console through a <see cref="ProcessRunner"/>, whose sink takes colour
/// codes out and whose links (stack frames, compiler messages, addresses) are the consoles' own. Timestamps are shown on
/// demand, and a project's services each have a toggle; changing either streams the log again.</summary>
public sealed class ContainerLogPane : DockPanel
{
    /// <summary>How many lines of what came before a stream begins with.</summary>
    private const int Tail = 1000;

    private readonly Func<ContainerEngine> engine;
    private readonly ToggleButton timestamps;
    private readonly StackPanel services = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly HashSet<string> hidden = new(StringComparer.Ordinal);
    private ProcessRunner? runner;
    private ContainerRow? row;
    private string? streaming;

    /// <summary>What the log shown was read from (<see cref="Signature"/>).</summary>
    private string? streamed;

    public ContainerLogPane(Func<ContainerEngine> engine)
    {
        this.engine = engine;
        Console = new OutputView(toolbar: false);
        Console.LinkClicked += (_, link) => LinkClicked?.Invoke(this, link);
        timestamps = new ToggleButton { Classes = { "segment" }, Content = "Timestamps", VerticalAlignment = VerticalAlignment.Center, Name = "containerTimestamps" };
        timestamps.IsCheckedChanged += (_, _) => Restart();
        // The editor's own Find, over the console's text: case, whole words and regular expressions, every match marked.
        Find = new Editors.FindBar { [DockProperty] = Dock.Top };
        Find.Attach(Console.Editor);
        Button finding = ToolButtons.Of("Icon.Search", "Find in the log", OpenFind);
        finding.Name = "containerLogFind";
        finding.VerticalAlignment = VerticalAlignment.Center;
        StackPanel right = new() { Orientation = Orientation.Horizontal, Spacing = 4, [DockProperty] = Dock.Right, Children = { finding, timestamps } };
        DockPanel bar = new() { [DockProperty] = Dock.Top, Height = 32, Margin = new Thickness(8, 0) };
        bar.Children.Add(right);
        bar.Children.Add(services);
        Children.Add(bar);
        Children.Add(Find);
        Children.Add(Console);
    }

    /// <summary>Find over the log, as the editor's: shown by its button and by Find's keys while the window has them.</summary>
    public Editors.FindBar Find { get; }

    /// <summary>Opens Find over the log.</summary>
    public void OpenFind() => Find.Open(replace: false);

    /// <summary>A link in the log clicked.</summary>
    public event EventHandler<LinkTarget>? LinkClicked;

    /// <summary>The console the log streams into.</summary>
    public OutputView Console { get; }

    /// <summary>Whether lines carry the engine's timestamps.</summary>
    public bool Timestamps
    {
        get => timestamps.IsChecked == true;
        set => timestamps.IsChecked = value;
    }

    /// <summary>The services whose lines a project's log shows, each a toggle.</summary>
    public IReadOnlyList<ToggleButton> ServiceToggles => [.. services.Children.OfType<ToggleButton>()];

    /// <summary>What streams now, as a command's words; null while nothing does.</summary>
    public string? Streaming => streaming;

    /// <summary>Streams a row's log, or nothing; the same row again leaves its stream as it is, and a stopped container's
    /// log as it was read (its scroll and Find kept) until one of its containers runs again or another comes.</summary>
    public void Show(ContainerRow? shown)
    {
        UiThread.Check();
        if (shown?.Key == row?.Key && (shown is null || runner is { IsRunning: true } || (Signature(shown) == streamed && !shown.Containers.Any(c => c.IsRunning))))
        {
            row = shown;
            return;
        }
        bool otherProject = shown?.Project?.Name != row?.Project?.Name;
        row = shown;
        if (otherProject)
        {
            hidden.Clear();
        }
        services.Children.Clear();
        if (shown is { Project: { } project, Service: null, Kind: ContainerRowKind.Project })
        {
            foreach ((string service, _) in project.Services)
            {
                ToggleButton toggle = new() { Classes = { "segment" }, Content = service, IsChecked = !hidden.Contains(service), VerticalAlignment = VerticalAlignment.Center };
                toggle.IsCheckedChanged += (_, _) =>
                {
                    if (toggle.IsChecked == true)
                    {
                        hidden.Remove(service);
                    }
                    else
                    {
                        hidden.Add(service);
                    }
                    Restart();
                };
                services.Children.Add(toggle);
            }
        }
        Restart();
    }

    /// <summary>Stops the stream, as the window hides.</summary>
    public void Stop()
    {
        row = null;
        End();
    }

    private void End()
    {
        streaming = null;
        if (runner is not null)
        {
            runner.Stop();
            runner.Dispose();
            runner = null;
        }
    }

    /// <summary>The containers a row's log was read from, and which of them ran.</summary>
    private static string Signature(ContainerRow shown) => string.Join(',', shown.Containers.Select(c => $"{c.Id}:{c.IsRunning}"));

    private void Restart()
    {
        End();
        Console.Clear();
        streamed = row is { } read ? Signature(read) : null;
        if (row is not { } shown || Words(shown) is not { } words)
        {
            return;
        }
        System.Diagnostics.ProcessStartInfo start;
        try
        {
            start = engine().Command(words);
        }
        catch (FileNotFoundException missing)
        {
            Console.Append($"{missing.Message}\n", error: true);
            return;
        }
        runner = new ProcessRunner(Console);
        Dictionary<string, string> environment = start.Environment.Where(v => v.Value is not null).ToDictionary(v => v.Key, v => v.Value!);
        if (runner.Start(start.FileName, words, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), environment) is { } problem)
        {
            Console.Append($"{problem}\n", error: true);
            return;
        }
        streaming = ContainerEngine.Words(words);
    }

    private IReadOnlyList<string>? Words(ContainerRow shown)
    {
        List<string> options = ["-f", "--tail", Tail.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        if (Timestamps)
        {
            options.Add("-t");
        }
        if (shown.Kind == ContainerRowKind.Container || (shown.Kind == ContainerRowKind.Service && shown.Container is not null))
        {
            return ["logs", .. options, shown.Container!.Id];
        }
        if (shown.Project is { } project)
        {
            string[] chosen = shown.Service is { } service ? [service] : [.. project.Services.Select(s => s.Service).Where(s => !hidden.Contains(s))];
            if (chosen.Length == 0)
            {
                return null;
            }
            // The services' names are labels', never read as flags.
            return [.. project.Arguments, "logs", "--no-color", .. options, "--", .. chosen];
        }
        return null;
    }
}
