using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Passenger.App;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>Two official, container-free starters, restored together and shared by the window tests.</summary>
public sealed class AspireApp : SharedProject
{
    private static readonly Lazy<string> Hive = new(Install, LazyThreadSafetyMode.ExecutionAndPublication);
    public static string TemplatesVersion => AvaloniaApp.Metadata("AspireTemplatesVersion");
    public const string Name = "PassengerAspire";
    public const string CompanionName = "CompanionAspire";
    public const string RequestLog = "Passenger request {RequestValue}";
    public const string MissingCliReason = "Aspire CLI was not found on PATH. Install Aspire 13.6.2.";

    public AspireApp() : base("aspire-")
    {
        WhyNot = Unavailable;
        if (WhyNot is not null)
        {
            return;
        }
        AvaloniaApp.UseFeed(Folder);
        Create(PathOf("First"), Name);
        Create(PathOf("Second"), CompanionName);
        File.WriteAllText(PathOf("AspireTests.slnx"), "<Solution>\n" + string.Concat(
            new[] { ("First", Name), ("Second", CompanionName) }.SelectMany(app =>
                new[] { "AppHost", "ApiService", "Web", "ServiceDefaults" }.Select(project =>
                    $"  <Project Path=\"{app.Item1}/{app.Item2}.{project}/{app.Item2}.{project}.csproj\" />\n"))) + "</Solution>\n");
        Prepare();
        using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Host)!, "obj", "project.assets.json")));
        Assert.True(assets.RootElement.GetProperty("libraries").TryGetProperty("MessagePack/2.5.307", out _),
            "The 13.6.2 AppHost must restore the fixed MessagePack dependency.");
    }

    public string? WhyNot { get; }

    public static bool IsUnavailable => Unavailable is not null;

    public static string? Unavailable => Programs.OnPath(OperatingSystem.IsWindows() ? "aspire.exe" : "aspire") is null
        ? MissingCliReason : null;

    public string Host => PathOf($"First/{Name}.AppHost/{Name}.AppHost.csproj");
    public string CompanionHost => PathOf($"Second/{CompanionName}.AppHost/{CompanionName}.AppHost.csproj");
    public string ApiProgram => PathOf($"First/{Name}.ApiService/Program.cs");
    public string ApiProject => PathOf($"First/{Name}.ApiService/{Name}.ApiService.csproj");
    public string WebProgram => PathOf($"First/{Name}.Web/Program.cs");

    public MainWindow OpenHost() => Open(PathOf($"First/{Name}.AppHost/AppHost.cs"));

    public static string ConnectionInfoPath(string appHost) => appHost + ".dcp.json";

    /// <summary>Reads the connection inherited by the real fixture AppHost, never the IDE's internal server.</summary>
    public static (int Port, string Token) ReadConnectionInfo(string appHost)
    {
        using JsonDocument connection = JsonDocument.Parse(File.ReadAllText(ConnectionInfoPath(appHost)));
        string? endpoint = connection.RootElement.GetProperty("endpoint").GetString();
        Assert.True(endpoint?.StartsWith("localhost:", StringComparison.Ordinal) == true, "The fixture AppHost did not inherit a localhost DCP endpoint");
        Assert.True(int.TryParse(endpoint!["localhost:".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            && port is > 0 and <= 65535, "The fixture AppHost inherited an invalid DCP port");
        string? token = connection.RootElement.GetProperty("token").GetString();
        Assert.True(!string.IsNullOrWhiteSpace(token), "The fixture AppHost did not inherit a DCP bearer token");
        return (port, token!);
    }

    public static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Generates the pinned upstream starter, without restore or changes to the person's template hive.</summary>
    public static void Create(string folder, string name)
    {
        ChildResult made = Run(["new", "aspire-starter", "--output", folder, "--name", name, "--framework", "net10.0",
            "--use-redis-cache", "false", "--skipRestore", "--no-update-check", "--debug:custom-hive", Hive.Value], Path.GetDirectoryName(Hive.Value));
        Assert.True(made.Succeeded, $"The official Aspire starter did not generate:\n{made.Output}{made.Error}");
        AvaloniaApp.UseFeed(folder);
        string hostFolder = Path.Combine(folder, name + ".AppHost");
        string project = Path.Combine(hostFolder, name + ".AppHost.csproj");
        string projectText = File.ReadAllText(project);
        Assert.Contains($"Sdk=\"Aspire.AppHost.Sdk/{TemplatesVersion}\"", projectText, StringComparison.Ordinal);
        Assert.Contains("<AspireUseCliBundle>true</AspireUseCliBundle>", projectText, StringComparison.Ordinal);
        File.WriteAllText(project, projectText.Replace("<AspireUseCliBundle>true</AspireUseCliBundle>",
            "<AspireUseCliBundle>false</AspireUseCliBundle>", StringComparison.Ordinal));
        string hostSource = Path.Combine(hostFolder, "AppHost.cs");
        string hostText = File.ReadAllText(hostSource);
        string connectionPath = JsonSerializer.Serialize(ConnectionInfoPath(project));
        string recordConnection = $$"""
            var passengerDcpFile = {{connectionPath}};
            var passengerDcpOptions = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                passengerDcpOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            using (var passengerDcpStream = new FileStream(passengerDcpFile + ".tmp", passengerDcpOptions))
            {
                System.Text.Json.JsonSerializer.Serialize(passengerDcpStream, new
                {
                    endpoint = Environment.GetEnvironmentVariable("DEBUG_SESSION_PORT"),
                    token = Environment.GetEnvironmentVariable("DEBUG_SESSION_TOKEN"),
                });
            }
            File.Move(passengerDcpFile + ".tmp", passengerDcpFile, overwrite: true);

            """;
        File.WriteAllText(hostSource, recordConnection + hostText.Replace("(\"apiservice\")", "(\"apiservice\", launchProfileName: \"http\")", StringComparison.Ordinal)
            .Replace("(\"webfrontend\")", "(\"webfrontend\", launchProfileName: \"http\")", StringComparison.Ordinal));
        string settings = Path.Combine(hostFolder, "Properties", "launchSettings.json");
        JsonObject launch = JsonNode.Parse(File.ReadAllText(settings))!.AsObject();
        JsonObject http = launch["profiles"]!["http"]!.AsObject();
        http["launchBrowser"] = false;
        http["applicationUrl"] = $"http://localhost:{FreePort()}";
        http["environmentVariables"]!["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"] = $"http://localhost:{FreePort()}";
        http["environmentVariables"]!["ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL"] = $"http://localhost:{FreePort()}";
        http["environmentVariables"]!["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";
        http["environmentVariables"]!["ASPIRE_VERSION_CHECK_DISABLED"] = "true";
        string dashboardData = Path.Combine(hostFolder, ".passenger-dashboard");
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(dashboardData);
        }
        else
        {
            Directory.CreateDirectory(dashboardData, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        http["environmentVariables"]!["ASPIRE_DASHBOARD_DATA_DIRECTORY"] = dashboardData;
        launch["profiles"]!.AsObject().Remove("https");
        File.WriteAllText(settings, launch.ToJsonString());

        string api = Path.Combine(folder, name + ".ApiService", "Program.cs");
        string apiText = File.ReadAllText(api);
        Assert.Contains("app.MapGet(\"/weatherforecast\", () =>", apiText, StringComparison.Ordinal);
        File.WriteAllText(api, apiText.Replace("app.MapGet(\"/weatherforecast\", () =>\n{", "app.MapGet(\"/weatherforecast\", (ILogger<Program> logger) =>\n{", StringComparison.Ordinal)
            .Replace("app.MapGet(\"/weatherforecast\", () =>\r\n{", "app.MapGet(\"/weatherforecast\", (ILogger<Program> logger) =>\r\n{", StringComparison.Ordinal)
            .Replace("    var forecast =", $"    int requestValue = 42;\n    logger.LogInformation(\"{RequestLog}\", requestValue);\n    var forecast =", StringComparison.Ordinal)
            .Replace("app.Run();", """
                app.MapGet("/passenger-flood", () =>
                {
                    for (int line = 0; line < 4096; line++) Console.WriteLine($"Passenger flood {line} {new string('x', 1024)}");
                    return "flood completed";
                });
                app.Run();
                """, StringComparison.Ordinal));
        string web = Path.Combine(folder, name + ".Web", "Program.cs");
        string webText = File.ReadAllText(web);
        File.WriteAllText(web, webText.Replace("app.Run();", $$"""
            app.MapGet("/passenger-test", async (WeatherApiClient weather, ILogger<Program> logger) =>
            {
                int requestValue = 42;
                logger.LogInformation("{{RequestLog}}", requestValue);
                var forecasts = await weather.GetWeatherAsync();
                return Results.Ok(forecasts.Length);
            });
            app.Run();
            """, StringComparison.Ordinal));
    }

    private static string Install()
    {
        string folder = Directory.CreateDirectory(Path.Combine(TestRun.Root, "aspire-templates")).FullName;
        string hive = Path.Combine(folder, "hive");
        string version = TemplatesVersion.ToLowerInvariant();
        string package = Path.Combine(AvaloniaApp.Metadata("NuGetPackageRoot"), "aspire.projecttemplates", version,
            $"aspire.projecttemplates.{version}.nupkg");
        Assert.True(File.Exists(package), $"Aspire.ProjectTemplates {TemplatesVersion} is missing at {package}; restore the test project first");
        string localPackage = Path.Combine(folder, Path.GetFileName(package));
        File.Copy(package, localPackage, overwrite: true);
        string sdk = Path.Combine(AvaloniaApp.Metadata("NuGetPackageRoot"), "aspire.apphost.sdk", version, $"aspire.apphost.sdk.{version}.nupkg");
        Assert.True(File.Exists(sdk), $"Aspire.AppHost.Sdk {TemplatesVersion} is missing at {sdk}; restore the test project first");
        File.Copy(sdk, Path.Combine(AvaloniaApp.Feed, Path.GetFileName(sdk)), overwrite: true);
        AvaloniaApp.UseFeed(folder);
        ChildResult installed = Run(["new", "install", localPackage, "--debug:custom-hive", hive], folder);
        Assert.True(installed.Succeeded, $"Aspire templates did not install into the private hive:\n{installed.Output}{installed.Error}");
        return hive;
    }

    private static ChildResult Run(IReadOnlyList<string> arguments, string? folder) => Task.Run(() =>
        ChildProcesses.RunAsync(ChildProcesses.DotnetCommand(arguments, folder), limit: TimeSpan.FromMinutes(3).Scaled())).GetAwaiter().GetResult();
}
