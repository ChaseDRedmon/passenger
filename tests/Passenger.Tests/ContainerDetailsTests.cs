using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Containers;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>The Containers window's Details tab and Inspect, as a person reads a container whose health check fails: a
/// Compose service's row shows its container's environment with each secret masked (a secret's variable, a URL's password,
/// a connection string's), its health check's last outputs (so "unhealthy" says why), its port as a link, its volume,
/// network and the service it waits for; and Inspect's JSON masks the secrets too, in its tab and in the copy it writes.
/// Skipped, saying why, where no Linux engine runs; everything the test makes carries the run's label.</summary>
public sealed class ContainerDetailsTests
{
    private const string Secret = "hunter2";

    [AvaloniaFact]
    public void A_services_details_mask_its_secrets_say_why_it_is_unhealthy_and_inspect_never_writes_the_secret()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        string project = $"ptest{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}details";
        string compose = Path.Combine(TestFolders.Create("ctr-details-"), "compose.yaml");
        string label = Docker.RunLabel.Replace("=", ": \"", StringComparison.Ordinal) + "\"";
        File.WriteAllText(compose, $$"""
            name: {{project}}
            services:
              cache:
                image: {{Docker.Image}}
                command: ["sleep", "600"]
                labels: { {{label}} }
                healthcheck: { test: ["CMD", "true"], interval: 1s, retries: 30 }
              app:
                image: {{Docker.Image}}
                command: ["sleep", "600"]
                labels: { {{label}} }
                environment: { DB_PASSWORD: {{Secret}}, GREETING: hello, DATABASE_URL: "postgres://app:{{Secret}}@db/shop", SHOP_DB: "Host=db;Password={{Secret}}" }
                ports: ["8080"]
                volumes: ["data:/data"]
                depends_on: { cache: { condition: service_healthy } }
                healthcheck: { test: ["CMD-SHELL", "echo probe-said-no; exit 1"], interval: 1s, retries: 1 }
            volumes:
              data: { labels: { {{label}} } }
            networks:
              default: { labels: { {{label}} } }

            """);
        // Not --wait: the app's health check fails on purpose.
        Docker.Run("compose", "-f", compose, "up", "-d");
        try
        {
            Read(project, compose);
        }
        finally
        {
            // Gone at once rather than at the run's end, so the windows of the tests after it list no more than they did.
            Docker.Run("compose", "-f", compose, "down", "--volumes");
        }
    }

    private static void Read(string project, string compose)
    {
        MainWindow window = new() { Width = 1200, Height = 900 };
        window.Show();
        window.OpenFolder(TestFolders.Create("ctr-details-window-"));
        Ui.Settle();
        window.ShowToolWindow("containers");
        Ui.Settle();
        ContainersView view = window.ContainersWindow;
        Ui.WaitFor(() => view.Choose(r => r.Kind == ContainerRowKind.Service && r.Project?.Name == project && r.Service == "app"), "the app service's row");
        view.ShowTab("Details");
        Ui.WaitFor(() => view.Details.Details is { Health: "unhealthy", Probes.Count: > 0 },
            "the app's details to say it is unhealthy, with its health check's outputs", TimeSpan.FromMinutes(1));

        ContainerDetails details = view.Details.Details!;
        Assert.Contains(("DB_PASSWORD", ContainerSecrets.Masked), details.Environment);
        Assert.Contains(("GREETING", "hello"), details.Environment);
        Assert.Contains(("DATABASE_URL", $"postgres://app:{ContainerSecrets.Masked}@db/shop"), details.Environment);
        Assert.Contains(("SHOP_DB", $"Host=db;Password={ContainerSecrets.Masked}"), details.Environment);
        Assert.Contains("probe-said-no", details.Probes[0].Output, StringComparison.Ordinal);
        Assert.Equal(new ServiceDependency("cache", "service_healthy", false), Assert.Single(details.DependsOn));
        Assert.Contains(details.Mounts, m => m.Type == "volume" && m.Destination == "/data" && m.Source == $"{project}_data");
        Assert.Contains(details.Networks, n => n.Name == $"{project}_default" && n.Address is not null);
        string[] shown = Words(view.Details);
        Assert.True(!shown.Any(w => w.Contains(Secret, StringComparison.Ordinal)), $"The Details tab shows the password: {string.Join(" | ", shown)}");
        Assert.Contains(ContainerSecrets.Masked, shown);
        Assert.True(shown.Any(w => w.Contains("probe-said-no", StringComparison.Ordinal)), $"The Details tab does not say what the failing health check printed: {string.Join(" | ", shown)}");

        // The published port is a link the browser opens.
        string published = Docker.Run("compose", "-f", compose, "port", "app", "8080").Split('\n')[0].Trim();
        int port = int.Parse(published[(published.LastIndexOf(':') + 1)..], CultureInfo.InvariantCulture);
        Button link = view.Details.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "detailsPort");
        link.BringIntoView();
        Ui.Settle();
        Point at = link.TranslatePoint(new Point(link.Bounds.Width / 2, link.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
        Assert.Contains(new Uri($"http://localhost:{port}/"), window.Browsed);

        Assert.True(window.Perform(AppActions.ContainerInspect), "Inspect did not run");
        string container = view.Chosen!.Container!.Name;
        Ui.WaitFor(() => window.EditorArea.Active?.Path.EndsWith($"{container}.json", StringComparison.Ordinal) == true, "Inspect's JSON open in the editor");
        Passenger.App.Workspace.OpenDocument inspected = window.EditorArea.Active!;
        string text = inspected.Text.Snapshot.ToString();
        Assert.True(inspected.IsReadOnly, "Inspect's JSON opened editable");
        Assert.Contains($"\"DB_PASSWORD={ContainerSecrets.Masked}\"", text, StringComparison.Ordinal);
        Assert.Contains("\"GREETING=hello\"", text, StringComparison.Ordinal);
        Assert.True(!text.Contains(Secret, StringComparison.Ordinal), "Inspect's tab shows the password");
        foreach (string file in Directory.EnumerateFiles(Path.Combine(window.StateFolder, "cache", "containers"), "*", SearchOption.AllDirectories))
        {
            Assert.True(!File.ReadAllText(file).Contains(Secret, StringComparison.Ordinal), $"{file}, which Passenger wrote, holds the password");
        }
        window.Close();
    }

    /// <summary>Every word the pane shows.</summary>
    private static string[] Words(Control pane) =>
        [.. pane.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty)];
}
