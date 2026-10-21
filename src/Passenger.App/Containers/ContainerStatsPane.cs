using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.Core;

namespace Passenger.App.Containers;

/// <summary>One reading of <c>docker stats</c> for a container: CPU and memory as percentages, and memory, network and disk
/// in the engine's own words.</summary>
public sealed record ContainerStatsSample(string Name, double Cpu, double MemoryPercent, string Memory, string Network, string Disk, string Processes)
{
    internal static ContainerStatsSample? Read(JsonElement row)
    {
        string? name = ContainerEngine.Text(row, "Name");
        if (name is null)
        {
            return null;
        }
        return new ContainerStatsSample(name, Percent(ContainerEngine.Text(row, "CPUPerc")), Percent(ContainerEngine.Text(row, "MemPerc")),
            ContainerEngine.Text(row, "MemUsage") ?? string.Empty, ContainerEngine.Text(row, "NetIO") ?? string.Empty,
            ContainerEngine.Text(row, "BlockIO") ?? string.Empty, ContainerEngine.Text(row, "PIDs") ?? string.Empty);
    }

    private static double Percent(string? said) =>
        double.TryParse(said?.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
}

/// <summary>A container's or a project's CPU, memory, network and disk over time, from <c>docker stats --format json</c>,
/// streamed only while the tab is shown: a row a container with its latest numbers, the project's sum first, and the CPU
/// and memory of the last minutes drawn under them.</summary>
public sealed class ContainerStatsPane : DockPanel
{
    /// <summary>How many readings the lines keep: about two minutes of the engine's one a second or so.</summary>
    private const int Kept = 120;

    private readonly Func<ContainerEngine> engine;
    private readonly StackPanel table = new() { Margin = new Thickness(8, 6) };
    private readonly Sparkline cpu = new("Passenger.Link");
    private readonly Sparkline memory = new("Passenger.Test.Passed");
    private readonly TextBlock waiting = new() { Classes = { "emptyText" }, Text = "Waiting for the engine's first reading…" };
    private readonly Dictionary<string, ContainerStatsSample> latest = new(StringComparer.Ordinal);
    private Process? streaming;
    private readonly HashSet<string> sinceLast = new(StringComparer.Ordinal);
    private string? key;

    public ContainerStatsPane(Func<ContainerEngine> engine)
    {
        this.engine = engine;
        Grid charts = new() { ColumnDefinitions = new ColumnDefinitions("*,*"), Height = 72, Margin = new Thickness(8, 0, 8, 8), [DockProperty] = Dock.Bottom };
        charts.Children.Add(Chart("CPU %", cpu, 0));
        charts.Children.Add(Chart("Memory %", memory, 1));
        Children.Add(charts);
        Children.Add(new Panel { Children = { new ScrollViewer { Content = table }, waiting } });
    }

    /// <summary>The latest reading of each container shown.</summary>
    public IReadOnlyList<ContainerStatsSample> Latest => [.. latest.Values];

    /// <summary>How many readings the lines hold.</summary>
    public int Readings => cpu.Count;

    /// <summary>Streams a row's containers' stats, or nothing; the same row again leaves its stream as it is.</summary>
    public void Show(ContainerRow? row)
    {
        UiThread.Check();
        string[] running = row is null ? [] : [.. row.Containers.Where(c => c.IsRunning).Select(c => c.Id)];
        string? next = running.Length == 0 ? null : string.Join(',', running);
        if (next == key && (next is null || streaming is { HasExited: false }))
        {
            return;
        }
        Stop();
        key = next;
        waiting.Text = row is null ? string.Empty : running.Length == 0 ? $"{row.Name} is not running" : "Waiting for the engine's first reading…";
        waiting.IsVisible = true;
        if (next is null)
        {
            return;
        }
        Process process;
        try
        {
            ProcessStartInfo start = engine().Command(["stats", "--format", "json", .. running]);
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            process = ChildProcesses.Start(start);
        }
        catch (Exception failed) when (failed is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            waiting.Text = failed.Message;
            return;
        }
        streaming = process;
        // Not background work: it streams for as long as the tab is shown, which no screenshot or settling test waits for.
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    ContainerStatsSample[] read = [.. ContainerEngine.JsonLines(line).Select(ContainerStatsSample.Read).OfType<ContainerStatsSample>()];
                    if (read.Length > 0)
                    {
                        UiThread.Post(() => Take(process, read));
                    }
                }
            }
            catch (Exception ended) when (ended is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Stopped under its read.
            }
        });
    }

    /// <summary>Stops the stream, the readings let go.</summary>
    public void Stop()
    {
        if (streaming is { } process)
        {
            streaming = null;
            ChildProcesses.Kill(process);
            process.Dispose();
        }
        key = null;
        latest.Clear();
        sinceLast.Clear();
        cpu.Clear();
        memory.Clear();
        table.Children.Clear();
    }

    private void Take(Process from, ContainerStatsSample[] read)
    {
        if (streaming != from)
        {
            return;
        }
        foreach (ContainerStatsSample sample in read)
        {
            // docker stats writes each container a line a reading: a name read again begins the next, and the lines take
            // the sum of the one before.
            if (!sinceLast.Add(sample.Name))
            {
                cpu.Add(latest.Values.Sum(s => s.Cpu));
                memory.Add(latest.Values.Sum(s => s.MemoryPercent));
                sinceLast.Clear();
                sinceLast.Add(sample.Name);
            }
            latest[sample.Name] = sample;
        }
        waiting.IsVisible = false;
        table.Children.Clear();
        table.Children.Add(Line("Container", "CPU", "Memory", "Network I/O", "Disk I/O", "PIDs", heading: true));
        if (latest.Count > 1)
        {
            table.Children.Add(Line("All", Percent(latest.Values.Sum(s => s.Cpu)), Percent(latest.Values.Sum(s => s.MemoryPercent)), string.Empty, string.Empty,
                latest.Values.Sum(s => int.TryParse(s.Processes, CultureInfo.InvariantCulture, out int n) ? n : 0).ToString(CultureInfo.InvariantCulture), heading: true));
        }
        foreach (ContainerStatsSample sample in latest.Values.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            table.Children.Add(Line(sample.Name, Percent(sample.Cpu), $"{sample.Memory} ({Percent(sample.MemoryPercent)})", sample.Network, sample.Disk, sample.Processes, heading: false));
        }
    }

    private static string Percent(double value) => value.ToString("0.00", CultureInfo.InvariantCulture) + "%";

    private static Grid Line(string name, string cpu, string memory, string network, string disk, string processes, bool heading)
    {
        Grid line = new() { ColumnDefinitions = new ColumnDefinitions("2*,*,2*,2*,2*,*"), Height = ListRow.Height };
        string[] cells = [name, cpu, memory, network, disk, processes];
        for (int i = 0; i < cells.Length; i++)
        {
            TextBlock cell = new() { Text = cells[i], VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, [Grid.ColumnProperty] = i, Margin = new Thickness(0, 0, 8, 0) };
            if (heading)
            {
                cell.FontWeight = FontWeight.SemiBold;
            }
            line.Children.Add(cell);
        }
        return line;
    }

    private static Control Chart(string title, Sparkline line, int column)
    {
        TextBlock name = new() { Text = title, FontSize = 11, [DockProperty] = Dock.Top };
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("Passenger.TextMuted"));
        Border frame = new() { Classes = { "framed" }, Child = line };
        return new DockPanel { [Grid.ColumnProperty] = column, Margin = new Thickness(column == 0 ? 0 : 4, 0, column == 0 ? 4 : 0, 0), Children = { name, frame } };
    }

    /// <summary>A line of the last readings, scaled to the highest of them (at least 1), in a token's colour.</summary>
    private sealed class Sparkline(string brush) : Control
    {
        private readonly List<double> values = [];

        public int Count => values.Count;

        public void Add(double value)
        {
            values.Add(value);
            if (values.Count > Kept)
            {
                values.RemoveAt(0);
            }
            InvalidateVisual();
        }

        public void Clear()
        {
            values.Clear();
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            if (values.Count < 2 || !this.TryFindResource(brush, ActualThemeVariant, out object? found) || found is not IBrush colour)
            {
                return;
            }
            double top = Math.Max(1, values.Max());
            Rect area = new Rect(Bounds.Size).Deflate(4);
            StreamGeometry path = new();
            using (StreamGeometryContext draw = path.Open())
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Point at = new(area.Left + (area.Width * i / (Kept - 1)), area.Bottom - (area.Height * values[i] / top));
                    if (i == 0)
                    {
                        draw.BeginFigure(at, isFilled: false);
                    }
                    else
                    {
                        draw.LineTo(at);
                    }
                }
                draw.EndFigure(isClosed: false);
            }
            context.DrawGeometry(null, new Pen(colour, 1.5), path);
        }
    }
}
