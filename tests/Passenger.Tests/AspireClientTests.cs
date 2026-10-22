using System.Text.Json;
using Passenger.App.Aspire;

namespace Passenger.Tests;

[Collection(Alone.Name)]
public sealed class AspireClientTests
{
    [Theory]
    [InlineData("13.6.0", true)]
    [InlineData("13.6.3", true)]
    [InlineData("13.7.0", true)]
    [InlineData("13.5.4", false)]
    [InlineData("14.0.0", false)]
    public async Task The_CLI_probe_accepts_compatible_versions_without_requiring_one_patch(string version, bool supported)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in Aspire CLI is a shell script");
        string folder = StandIn(version);
        string? before = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", folder + Path.PathSeparator + before);
            AspireAvailability availability = await new AspireClient().ProbeAsync(TestContext.Current.CancellationToken);
            Assert.Equal(supported, availability.Available);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", before);
            TestFolders.Delete(folder);
        }
    }

    [Fact]
    public async Task CLI_resource_snapshots_remove_URL_credentials_and_report_the_masked_actual_failure()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in Aspire CLI is a shell script");
        string folder = StandIn("13.6.3");
        string? before = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", folder + Path.PathSeparator + before);
            string host = Path.Combine(folder, "AppHost.csproj");
            File.WriteAllText(host, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(folder, "resources.json"), JsonSerializer.Serialize(new
            {
                resources = new[]
                {
                    new
                    {
                        name = "api",
                        displayName = "api",
                        state = "Running",
                        healthStatus = "Healthy",
                        properties = new { },
                        environment = new { },
                        commands = new { },
                        urls = new[]
                        {
                            new { url = "http://user:password@localhost:8080/?api_key=private&view=1" },
                            new { url = "javascript:alert(1)" },
                        },
                    },
                },
            }));
            AspireResource resource = Assert.Single(await new AspireClient().GetResourcesAsync(host, TestContext.Current.CancellationToken));
            Assert.Equal("http://localhost:8080/?view=1", resource.Data.GetProperty("urls")[0].GetProperty("url").GetString());
            Assert.Equal(JsonValueKind.Null, resource.Data.GetProperty("urls")[1].GetProperty("url").ValueKind);
            Assert.DoesNotContain("password", resource.Data.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("private", resource.Data.GetRawText(), StringComparison.Ordinal);

            File.WriteAllText(Path.Combine(folder, "failure"), "Configuration failed: Password=synthetic-password; Authorization=Bearer synthetic-token");
            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new AspireClient().GetResourcesAsync(host, TestContext.Current.CancellationToken));
            Assert.Contains("Configuration failed", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-password", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-token", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", before);
            TestFolders.Delete(folder);
        }
    }

    private static string StandIn(string version)
    {
        string folder = TestFolders.Create("aspire-cli-");
        string cli = Path.Combine(folder, "aspire");
        File.WriteAllText(cli, $"#!/bin/sh\nhere=$(dirname \"$0\")\nif [ \"$1\" = --version ]; then printf '%s\\n' '{version}'; exit 0; fi\nif [ -f \"$here/failure\" ]; then cat \"$here/failure\" >&2; exit 9; fi\ncat \"$here/resources.json\"\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(cli, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return folder;
    }
}
