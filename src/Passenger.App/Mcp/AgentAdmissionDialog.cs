using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Passenger.App.State;

namespace Passenger.App.Mcp;

/// <summary>The question a person's window asks when an agent's call reaches it while Enable MCP Server is off: "NAME
/// wants to work in this window", Allow (the default) or Don't allow, as a question's answers stand, and Always for this
/// folder at the footer's left, which keeps an Allow with the folder (never a no). Escape and closing answer no.</summary>
public sealed class AgentAdmissionDialog : Window
{
    public const string Allow = "Allow";
    public const string DontAllow = "Don't allow";

    private readonly CheckBox always = new() { Content = "Always for this folder" };

    public AgentAdmissionDialog(string agent, string? folder)
    {
        Title = $"{agent} wants to work in this window";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Button allow = new() { Content = Allow, IsDefault = true, Classes = { "primary" } };
        Button dontAllow = new() { Content = DontAllow, IsCancel = true };
        allow.Click += (_, _) => Close(Admission.Allowed);
        dontAllow.Click += (_, _) => Close(Admission.NotAllowed);

        string where = folder is null ? "this window" : Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Content = DialogFooter.Page(new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock
                {
                    Text = $"It asked through Passenger's MCP server. Allowed, it can read and search {where}, see the window, and build, run "
                        + "and debug; Passenger still asks before each kind of change it makes. Enable MCP Server in Settings lets agents in "
                        + "without asking.",
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        }, DialogFooter.Bar(DialogFooter.Answers(allow, dontAllow), always));
    }

    /// <summary>Whether Always for this folder is ticked, which keeps an Allow.</summary>
    public bool Always => always.IsChecked == true;
}

/// <summary>A folder's word on agents: Always for this folder, which lets every agent into its window without the
/// question from then on, whatever Enable MCP Server says.</summary>
public sealed record AgentsState : IStateDocument<AgentsState>
{
    public bool AlwaysAllowed { get; init; }

    public static IReadOnlyList<Action<JsonObject>> Upgrades { get; } = [StateStore.Unversioned];

    public AgentsState Clamped() => this;
}
