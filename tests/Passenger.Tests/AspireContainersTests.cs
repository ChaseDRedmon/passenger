using System.Globalization;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Containers;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>An AppHost's containers, which its orchestrator (DCP) makes on the container engine with none of Passenger's
/// labels, as a person sees them: under their AppHost in the Containers window, known while the Aspire window is hidden, and
/// gone with the run's Stop but for a Persistent one, the person's to keep. The fixture's starter is given a busybox container
/// of each lifetime (both carrying the run's label, so the run's end removes whatever a failure left); skipped, saying why,
/// without a Linux engine or busybox in it.</summary>
[Collection(Alone.Name)]
public sealed class AspireContainersTests : IClassFixture<AspireApp>
{
    private readonly AspireApp project;

    public AspireContainersTests(AspireApp project)
    {
        this.project = project;
        if (project.WhyNot is null)
        {
            project.Reset();
        }
    }

    /// <summary>The session container's resource name, the run's own so no other run's AppHost makes one of the same.</summary>
    internal static string Session => $"ps{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}-session";

    /// <summary>The Persistent container's resource name.</summary>
    internal static string Kept => $"ps{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}-kept";

    [AvaloniaFact(Skip = AspireApp.AppHostSkipped, SkipType = typeof(AspireApp), SkipWhen = nameof(AspireApp.IsUnavailable))]
    public void Stop_takes_down_the_runs_session_container_shown_under_its_AppHost_and_keeps_a_persistent_one()
    {
        Assert.SkipWhen(WhyNot is not null, WhyNot ?? string.Empty);
        WithContainers(Path.GetDirectoryName(project.Host)!, project.Write);
        MainWindow window = project.OpenHost();
        try
        {
            Ui.WaitFor(() => window.Configurations.Any(configuration => configuration.Aspire && PathNames.Same(configuration.Project, project.Host)
                && configuration.LaunchProfile == "http"), "the generated HTTP Aspire configuration");
            Assert.True(window.SelectConfiguration($"Aspire: {AspireApp.Name}.AppHost: http"));
            Assert.False(window.DockHost.IsShown(ToolWindowIds.Aspire), "The Aspire window must be hidden: the run alone follows its containers");
            Ui.Run(window, AppActions.RunProgram);
            string session = Running(Session);
            string kept = Running(Kept);

            // Under their AppHost in the Containers window, as a Compose project's are under the project.
            window.ShowToolWindow(ToolWindowIds.Containers);
            ContainersView view = window.ContainersWindow;
            Ui.WaitFor(() =>
            {
                if (view.HasRead && !view.Rows.Any(row => row.Kind == ContainerRowKind.AppHost))
                {
                    _ = view.RefreshAsync();
                }
                return view.Rows.Any(row => row.Kind == ContainerRowKind.AppHost && row.Containers.Count == 2);
            }, $"the AppHost's row with its two containers, not [{string.Join(", ", view.Rows.Select(row => $"{row.Kind} {row.Label}"))}]", TimeSpan.FromMinutes(1));
            ContainerRow host = view.Rows.Single(row => row.Kind == ContainerRowKind.AppHost);
            Assert.Equal($"Aspire: {AspireApp.Name}.AppHost: http", host.Label);
            int at = view.Rows.ToList().IndexOf(host);
            Assert.All(view.Rows.Skip(at + 1).Take(2), row => Assert.True(row.Kind == ContainerRowKind.Container && row.Depth == host.Depth + 1,
                $"{row.Label} is not one of the AppHost's containers under it"));
            Assert.True(view.Choose(row => row.Container is { } container && ContainerRow.SameId(session, container.Id)), "The session container's row");
            Ui.Settle();
            view.ShowTab("Details");
            Ui.Settle();
            Assert.Equal("Details", view.ShownTab);

            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => window.AspireRuntime.Apps.Count == 0, "Stop to end the AppHost", TimeSpan.FromMinutes(1));
            // Stop waits for its containers to go: the session one has gone once it has.
            Assert.Null(Docker.State(session));
            Assert.Equal("running", Docker.State(kept));
        }
        finally
        {
            window.Close();
            RemoveMade();
        }
    }

    /// <summary>An AppHost with no Persistent container has its orchestrator put its containers on a session network of the
    /// run's (DCP's <c>aspire-session-network-…</c>, labelled not Persistent), recorded with the session container as it is
    /// made, and Stop takes it down with the container, as a container run's Stop takes its networks, whatever the
    /// orchestrator did of it itself as its AppHost went.</summary>
    [AvaloniaFact(Skip = AspireApp.AppHostSkipped, SkipType = typeof(AspireApp), SkipWhen = nameof(AspireApp.IsUnavailable))]
    public void Stop_takes_down_the_runs_session_network_with_its_session_container()
    {
        Assert.SkipWhen(WhyNot is not null, WhyNot ?? string.Empty);
        WithContainers(Path.GetDirectoryName(project.Host)!, project.Write, kept: false);
        MainWindow window = project.OpenHost(languageServer: false);
        string? network = null;
        try
        {
            Ui.WaitFor(() => window.Configurations.Any(configuration => configuration.Aspire && PathNames.Same(configuration.Project, project.Host)
                && configuration.LaunchProfile == "http"), "the generated HTTP Aspire configuration");
            Assert.True(window.SelectConfiguration($"Aspire: {AspireApp.Name}.AppHost: http"));
            Ui.Run(window, AppActions.RunProgram);
            string session = Running(Session);
            string[] networks = (Docker.Said("inspect", "-f", "{{range $name, $_ := .NetworkSettings.Networks}}{{$name}} {{end}}", session) ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            network = Assert.Single(networks);
            Assert.StartsWith("aspire-session-network-", network, StringComparison.Ordinal);
            string record = Path.Combine(window.StateFolder, "aspire-containers", "ownership.json");
            Ui.WaitFor(() => File.Exists(record) && Ui.Shared(record).Contains(session, StringComparison.Ordinal) && Ui.Shared(record).Contains(network, StringComparison.Ordinal),
                "the session container recorded with its network as the orchestrator made them");

            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => window.AspireRuntime.Apps.Count == 0, "Stop to end the AppHost", TimeSpan.FromMinutes(1));
            // Stop waits for its containers and network to go.
            Assert.Null(Docker.State(session));
            Assert.Null(Docker.Said("network", "inspect", network));
        }
        finally
        {
            window.Close();
            RemoveMade();
            if (network is not null)
            {
                Docker.Said("network", "rm", network);
            }
        }
    }

    /// <summary>Why no AppHost here can make the tests' containers: no Linux engine, or no busybox in it (none is pulled).</summary>
    internal static string? WhyNot => Docker.WhyNot
        ?? (Docker.Said("image", "inspect", Docker.Image) is null ? $"The engine lacks {Docker.Image}, which these tests do not pull: docker pull {Docker.Image}" : null);

    /// <summary>The starter's AppHost given a busybox container of each lifetime (the session one alone without
    /// <paramref name="kept"/>), carrying the run's label.</summary>
    internal static void WithContainers(string hostFolder, Func<string, string, string> write, bool kept = true)
    {
        string source = Path.Combine(hostFolder, "AppHost.cs");
        string added = $"""
            builder.AddContainer("{Session}", "busybox", "1.37").WithArgs("sleep", "3600").WithContainerRuntimeArgs("--label", "{Docker.RunLabel}");
            {(kept ? $"builder.AddContainer(\"{Kept}\", \"busybox\", \"1.37\").WithArgs(\"sleep\", \"3600\").WithLifetime(ContainerLifetime.Persistent).WithContainerRuntimeArgs(\"--label\", \"{Docker.RunLabel}\");" : string.Empty)}

            """;
        string text = File.ReadAllText(source);
        Assert.Contains("builder.Build().Run();", text, StringComparison.Ordinal);
        write(source, text.Replace("builder.Build().Run();", added + "builder.Build().Run();", StringComparison.Ordinal));
    }

    /// <summary>The whole id of the container DCP made for a resource, once it runs, the window's loop running meanwhile.</summary>
    internal static string Running(string resource)
    {
        string? id = null;
        Ui.WaitFor(() => (id = RunningId(resource)) is not null, $"{resource}'s container running", TimeSpan.FromMinutes(3));
        return id!;
    }

    /// <summary>As <see cref="Running"/>, for a test with no window, off the window's thread.</summary>
    internal static async Task<string> RunningAsync(string resource)
    {
        string? id = null;
        Assert.True(await Waiting.UntilAsync(() => (id = RunningId(resource)) is not null, TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken),
            $"{resource}'s container did not run within 3 minutes");
        return id!;
    }

    private static string? RunningId(string resource) =>
        Docker.Said("ps", "-q", "--no-trunc", "--filter", $"label={Docker.RunLabel}", "--filter", $"name={resource}-") is { Length: 64 } id ? id : null;

    /// <summary>Removes what the tests' AppHosts made: their containers (by the run's label) and the Persistent one's network.</summary>
    internal static void RemoveMade()
    {
        string[] networks = (Docker.Said("ps", "-a", "-q", "--filter", $"label={Docker.RunLabel}", "--filter", $"name={Kept}-") ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(id => (Docker.Said("inspect", "-f", "{{range $name, $_ := .NetworkSettings.Networks}}{{$name}} {{end}}", id) ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray();
        foreach (string id in (Docker.Said("ps", "-aq", "--filter", $"label={Docker.RunLabel}") ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(id => Docker.Said("inspect", "-f", "{{.Name}}", id) is { } name && (name.Contains(Session, StringComparison.Ordinal) || name.Contains(Kept, StringComparison.Ordinal))))
        {
            Docker.Said("rm", "-f", id);
        }
        foreach (string network in networks.Where(name => name.StartsWith("aspire-", StringComparison.Ordinal)))
        {
            Docker.Said("network", "rm", network);
        }
    }
}
