using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Passenger.App;

/// <summary>Agents in the window: Rider's Enable MCP Server, which opens the window's own socket to them (and closing it
/// shuts them out), the person's leave before an agent's first use of a tool that changes something, and the status
/// bar's word on who is on the window and what they run there.</summary>
public partial class MainWindow
{
    private readonly List<string> agentWork = [];
    private Mcp.AgentSocket? agentSocket;
    private int agents;

    /// <summary>Whether this window may let agents in at all, when its setting says so; <c>--no-mcp</c> says never.</summary>
    public bool AgentsAllowed { get; init; } = true;

    /// <summary>The socket agents reach this window through while the person lets them, else null.</summary>
    public string? AgentSocket => agentSocket?.Path;

    /// <summary>Agents connected to the window's socket.</summary>
    public int Agents
    {
        get => agents;
        set
        {
            agents = value;
            ShowAgents();
        }
    }

    /// <summary>What the status bar says of agents on the window, or null while it says nothing.</summary>
    public string? AgentStatus => AgentText.IsVisible ? AgentText.Text : null;

    private void AddAgents() => Closed += (_, _) => ListenForAgents(false);

    /// <summary>Opens the window's socket to agents, or closes it and ends their connections.</summary>
    private void ListenForAgents(bool on)
    {
        if (on && AgentsAllowed && agentSocket is null)
        {
            try
            {
                agentSocket = Mcp.AgentSocket.Open(this);
                agentSocket.Describe(folder);
            }
            catch (Exception error) when (error is IOException or SocketException or ArgumentException or UnauthorizedAccessException)
            {
                StatusText.Text = $"Agents cannot reach this window: {error.Message}";
            }
        }
        else if (!(on && AgentsAllowed) && agentSocket is not null)
        {
            agentSocket.Dispose();
            agentSocket = null;
        }
    }

    /// <summary>Asks the person whether an agent may use a tool that changes something, showing what it asked with; an
    /// agent that gives up on the call takes the question away unanswered.</summary>
    internal async Task<bool> AllowAgentAsync(string tool, string title, IReadOnlyList<(string Name, string Value)> arguments, CancellationToken cancel)
    {
        string what = char.ToLowerInvariant(title[0]) + title[1..];
        ConfirmDialog dialog = new($"Allow {tool}?",
            $"An agent connected through Passenger's MCP server wants to {what} (its {tool} tool). Once allowed, it may use that tool again without asking until it disconnects.",
            "Allow", "Deny", arguments.Count == 0 ? null : AgentArguments(arguments));
        using CancellationTokenRegistration gone = cancel.Register(() => UiThread.Post(() => dialog.Close(false)));
        return await dialog.ShowDialog<bool>(this);
    }

    internal void AgentWorkStarted(string title)
    {
        agentWork.Add(title);
        ShowAgents();
    }

    internal void AgentWorkEnded(string title)
    {
        agentWork.Remove(title);
        ShowAgents();
    }

    private void ShowAgents()
    {
        AgentText.IsVisible = agents > 0 || agentWork.Count > 0;
        AgentText.Text = agentWork.Count > 0 ? $"Agent: {agentWork[^1]}" : agents == 1 ? "Agent connected" : $"{agents} agents connected";
    }

    private static Control AgentArguments(IReadOnlyList<(string Name, string Value)> arguments)
    {
        const int Longest = 400;
        TextBlock text = new()
        {
            Text = string.Join('\n', arguments.Select(a => $"{a.Name}: {(a.Value.Length > Longest ? a.Value[..Longest] + "…" : a.Value)}")),
            FontFamily = new FontFamily("avares://Passenger.Editor/Fonts#JetBrains Mono"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 6),
        };
        Border frame = new()
        {
            BorderThickness = new Thickness(1),
            MaxHeight = 200,
            Child = new ScrollViewer { Content = text, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
        };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Passenger.Divider"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Passenger.Editor.Background"));
        return frame;
    }
}
