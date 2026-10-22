using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Aspire;
using Passenger.Core;

namespace Passenger.Tests;

[Collection(Alone.Name)]
public sealed class AspireExternalDataTests(AspireApp project) : IClassFixture<AspireApp>
{
    [AvaloniaFact(Skip = AspireApp.CliSkipped, SkipType = typeof(AspireApp), SkipWhen = nameof(AspireApp.IsUnavailable))]
    public void An_external_AppHosts_environment_values_are_masked_in_the_native_window()
    {
        project.Reset();
        const string Variable = "PASSENGER_EXTERNAL_SECRET";
        const string Secret = "private-fixture-value-243";
        string host = Path.Combine(Path.GetDirectoryName(project.Host)!, "AppHost.cs");
        string source = File.ReadAllText(host);
        Assert.Contains(".WithHttpHealthCheck(\"/health\")", source, StringComparison.Ordinal);
        project.Write(Path.GetRelativePath(project.Folder, host), source.Replace(".WithHttpHealthCheck(\"/health\")",
            $".WithEnvironment(\"{Variable}\", \"{Secret}\").WithHttpHealthCheck(\"/health\")", StringComparison.Ordinal));
        string cli = Assert.IsType<string>(Programs.OnPath("aspire"));
        MainWindow? window = null;
        try
        {
            ChildResult started = Ui.Await(ChildProcesses.RunAsync(ChildProcesses.Command(cli,
                ["start", "--apphost", project.Host, "--format", "json", "--non-interactive", "--nologo"], project.Folder),
                limit: TimeSpan.FromMinutes(3)), "the external starter", TimeSpan.FromMinutes(3));
            Assert.True(started.Succeeded, $"The external starter did not start (exit {started.ExitCode}).");
            window = project.OpenHost();
            window.ShowToolWindow("aspire");
            Assert.Empty(window.AspireRuntime.Apps);
            Ui.WaitFor(() => window.Aspire.IsEffectivelyVisible, "the external AppHost pane");
            Button discover = window.Aspire.GetVisualDescendants().OfType<Button>()
                .Single(button => ActionButtons.GetAction(button) == AppActions.AspireDiscover);
            Ui.WaitFor(() => discover.IsEffectivelyVisible && discover.IsEnabled && !ActionButtons.IsWorking(discover),
                "the native AppHost discovery action to be ready");
            Ui.Run(window, AppActions.FindAction);
            window.KeyTextInput("Discover Aspire AppHosts");
            Ui.Settle();
            Passenger.App.Navigation.ActionPopup popup = Assert.IsType<Passenger.App.Navigation.ActionPopup>(window.Popup);
            Assert.Equal("Discover Aspire AppHosts", popup.Rows[0].Title);
            Ui.Press(window, "ENTER");
            try
            {
                Ui.WaitFor(() => !ActionButtons.IsWorking(discover)
                    && window.Aspire.AppSelector.Items.OfType<Passenger.App.Aspire.AspireApp>()
                        .Any(app => PathNames.Same(project.Host, app.AppHostPath)), "native discovery of the external AppHost");
            }
            catch (TimeoutException error)
            {
                throw new TimeoutException($"{error.Message}\nView: {window.Aspire.Status}\nAppHosts: {string.Join(", ", window.Aspire.AppSelector.Items.OfType<Passenger.App.Aspire.AspireApp>().Select(app => app.AppHostPath))}", error);
            }
            window.Aspire.SelectApp(project.Host);
            Assert.True(PathNames.Same(project.Host, Assert.IsType<Passenger.App.Aspire.AspireApp>(window.Aspire.SelectedApp).AppHostPath));
            Task refresh = window.Aspire.RefreshAsync();
            Ui.PumpUntil(() =>
            {
                if (!refresh.IsCompleted)
                {
                    return false;
                }
                refresh.GetAwaiter().GetResult();
                if (window.Aspire.ResourceSnapshots.Any(resource => resource.DisplayName == "apiservice"
                    && resource.Data.TryGetProperty("environment", out JsonElement environment) && environment.TryGetProperty(Variable, out _)))
                {
                    return true;
                }
                refresh = window.Aspire.RefreshAsync();
                return false;
            }, "the external resource's masked environment", TimeSpan.FromMinutes(1));
            AspireResource api = Assert.Single(window.Aspire.ResourceSnapshots, resource => resource.DisplayName == "apiservice");
            Assert.Equal(JsonValueKind.Null, api.Data.GetProperty("environment").GetProperty(Variable).ValueKind);
            Assert.DoesNotContain(Secret, api.Data.GetRawText(), StringComparison.Ordinal);
            string projectPath = Assert.IsType<string>(api.Data.GetProperty("properties").GetProperty("project.path").GetString());
            Assert.True(PathNames.Same(Path.Combine(Path.GetDirectoryName(project.ApiProgram)!, $"{AspireApp.Name}.ApiService.csproj"), projectPath),
                $"The CLI structural project path was {projectPath}");
        }
        finally
        {
            window?.Close();
            ChildResult stopped = Ui.Await(ChildProcesses.RunAsync(ChildProcesses.Command(cli,
                ["stop", "--apphost", project.Host, "--non-interactive", "--nologo"], project.Folder), limit: TimeSpan.FromSeconds(30)),
                "stopping the external test AppHost");
            Assert.True(stopped.Succeeded, $"The external test AppHost did not stop (exit {stopped.ExitCode}).");
        }
    }
}
