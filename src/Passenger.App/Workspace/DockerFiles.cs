using System.Text;
using System.Text.Json;
using Passenger.App.Containers;
using Passenger.Core;
using Passenger.Host;

namespace Passenger.App.Workspace;

/// <summary>A project as Add > Docker Support and Add > Docker Compose write for it: its file, the assembly its image runs,
/// its newest .NET target, whether it is an ASP.NET Core app (the aspnet image, ports), the HTTP ports its launch profiles
/// listen on, and the project files a restore of it needs (its own and those it references, through them all).</summary>
public sealed record DockerProject(string Path, string Name, string Assembly, string? Framework, bool Web, IReadOnlyList<int> HttpPorts, IReadOnlyList<string> Restores)
{
    public string Folder => System.IO.Path.GetDirectoryName(Path)!;

    /// <summary>Its Dockerfile, where Add > Docker Support writes it: beside the project.</summary>
    public string Dockerfile => System.IO.Path.Combine(Folder, "Dockerfile");

    /// <summary>The Dockerfile it has (Dockerfile or Containerfile beside it), or null.</summary>
    public string? ExistingDockerfile => new[] { "Dockerfile", "Containerfile" }.Select(n => System.IO.Path.Combine(Folder, n)).FirstOrDefault(File.Exists);

    /// <summary>Its name as a compose service and an image's repository: lowercase letters, digits and dashes.</summary>
    public string Service => DockerFiles.ServiceName(Name);
}

/// <summary>A database, cache or broker a project's packages say it talks to, which Add > Docker Compose runs beside it: its
/// compose service, image, the packages that point to it, a healthcheck its dependents wait for, and how a connection
/// string reaches it.</summary>
public sealed partial record BackingService(string Service, string Image, string[] Packages, string[] Words, string Test, string? Note)
{
    public static readonly IReadOnlyList<BackingService> Known =
    [
        new("postgres", "postgres:17", ["Npgsql", "Npgsql.EntityFrameworkCore.PostgreSQL", "Aspire.Npgsql", "Aspire.Npgsql.EntityFrameworkCore.PostgreSQL"],
            ["postgres", "npgsql", "pg"], "[\"CMD-SHELL\", \"pg_isready -U postgres\"]", null),
        new("sqlserver", "mcr.microsoft.com/mssql/server:2022-latest",
            ["Microsoft.Data.SqlClient", "System.Data.SqlClient", "Microsoft.EntityFrameworkCore.SqlServer", "Aspire.Microsoft.Data.SqlClient", "Aspire.Microsoft.EntityFrameworkCore.SqlServer"],
            ["sql", "mssql"], "[\"CMD-SHELL\", \"/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \\\"$$MSSQL_SA_PASSWORD\\\" -C -Q 'SELECT 1' || exit 1\"]",
            "SQL Server's image has no arm64 build: on an Arm machine (Apple silicon) it runs as amd64 under emulation"),
        new("redis", "redis:7", ["StackExchange.Redis", "Microsoft.Extensions.Caching.StackExchangeRedis", "Aspire.StackExchange.Redis"],
            ["redis", "cache"], "[\"CMD\", \"redis-cli\", \"ping\"]", null),
        new("rabbitmq", "rabbitmq:4", ["RabbitMQ.Client", "Aspire.RabbitMQ.Client", "MassTransit.RabbitMQ"],
            ["rabbit", "amqp", "bus", "messag", "queue"], "[\"CMD\", \"rabbitmq-diagnostics\", \"-q\", \"ping\"]", null),
        new("mongo", "mongo:8", ["MongoDB.Driver", "MongoDB.EntityFrameworkCore", "Aspire.MongoDB.Driver"],
            ["mongo"], "[\"CMD\", \"mongosh\", \"--quiet\", \"--eval\", \"db.adminCommand('ping')\"]", null),
    ];

    /// <summary>The services a project's packages point to, in <see cref="Known"/>'s order.</summary>
    public static IReadOnlyList<BackingService> For(IEnumerable<string> packages)
    {
        HashSet<string> named = new(packages, StringComparer.OrdinalIgnoreCase);
        return [.. Known.Where(k => k.Packages.Any(named.Contains))];
    }

    /// <summary>The backing service an app's connection string (its name, and its value in appsettings.json) reaches, by
    /// the value's own shape among those the project's packages call for: Host= is Npgsql's (postgres), Server= or Data
    /// Source= with SqlClient SQL Server's (but a file's, SQLite's), mongodb:// Mongo's, amqp:// RabbitMQ's, host:port
    /// with options Redis's; an empty one by the words of its name (Cache: redis). Null for any other, left as the app has it.</summary>
    public static BackingService? Reached(string name, string value, IReadOnlyList<BackingService> backing)
    {
        BackingService? Named(string service) => backing.FirstOrDefault(b => b.Service == service);
        string text = value.Trim();
        if (text.Length == 0)
        {
            return backing.FirstOrDefault(b => b.Words.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)));
        }
        if (text.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
        {
            return Named("mongo");
        }
        if (text.StartsWith("amqp://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("amqps://", StringComparison.OrdinalIgnoreCase))
        {
            return Named("rabbitmq");
        }
        if (text.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        {
            return Named("redis");
        }
        if (RedisOptions().IsMatch(text))
        {
            // StackExchange.Redis's host:port and its options (localhost:6379,abortConnect=false).
            return Named("redis");
        }
        if (Keywords(text) is not { } keys)
        {
            return null;
        }
        if (keys.ContainsKey("host"))
        {
            return Named("postgres");
        }
        string? source = keys.GetValueOrDefault("server") ?? keys.GetValueOrDefault("data source") ?? keys.GetValueOrDefault("address") ?? keys.GetValueOrDefault("addr");
        if (source is null || IsFile(source))
        {
            return null;
        }
        bool npgsql = keys.ContainsKey("username");
        return npgsql ? Named("postgres") : Named("sqlserver");
    }

    /// <summary>A connection string's keywords, lowercased, with their values; null for a string of another shape.</summary>
    private static IReadOnlyDictionary<string, string>? Keywords(string value)
    {
        if (!value.Contains('=', StringComparison.Ordinal))
        {
            return null;
        }
        try
        {
            System.Data.Common.DbConnectionStringBuilder read = new() { ConnectionString = value };
            return read.Keys.Cast<string>().ToDictionary(k => k.ToLowerInvariant(), k => read[k]?.ToString() ?? string.Empty, StringComparer.Ordinal);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Whether a data source is a file (SQLite's app.db, a path) or memory, no server's (localhost, .\SQLEXPRESS).</summary>
    private static bool IsFile(string source) =>
        source.Contains(":memory:", StringComparison.OrdinalIgnoreCase) || source.Contains('/', StringComparison.Ordinal)
        || (source.Length > 2 && char.IsAsciiLetter(source[0]) && source[1] == ':' && source[2] is '\\' or '/')
        || System.IO.Path.GetExtension(source).ToLowerInvariant() is ".db" or ".db3" or ".sqlite" or ".sqlite3" or ".sdf" or ".mdf";

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z0-9.\-_\[\]]+(?::\d+)?(?:,[^,=]+(?:=[^,]*)?)*$")]
    private static partial System.Text.RegularExpressions.Regex RedisOptions();

    /// <summary>The database a connection string names (a vhost, Mongo's database), else one named for the string, for
    /// a service that has databases; null for one that has none.</summary>
    public string? Database(string name, string value)
    {
        string text = value.Trim();
        switch (Service)
        {
            case "postgres":
                return Keywords(text)?.GetValueOrDefault("database") is { Length: > 0 } postgres ? postgres : name.ToLowerInvariant();
            case "sqlserver":
                return Keywords(text) is { } keys && (keys.GetValueOrDefault("database") ?? keys.GetValueOrDefault("initial catalog")) is { Length: > 0 } catalog ? catalog : name;
            case "mongo" or "rabbitmq":
                // A URL's path: Mongo's database, RabbitMQ's virtual host.
                return Uri.TryCreate(text.Replace("mongodb+srv://", "mongodb://", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out Uri? url)
                    && url.AbsolutePath.Trim('/') is { Length: > 0 } path ? Uri.UnescapeDataString(path) : null;
            default:
                return null;
        }
    }

    /// <summary>Where a project in the same compose project reaches it, for a connection string named so with the app's
    /// own value: the database the value names is kept.</summary>
    public string ConnectionString(string name, string value) => (Service, Database(name, value)) switch
    {
        ("postgres", var database) => $"Host=postgres;Port=5432;Database={database};Username=postgres;Password=${{POSTGRES_PASSWORD:-postgres}}",
        ("sqlserver", var database) => $"Server=sqlserver,1433;Database={database};User Id=sa;Password=${{MSSQL_SA_PASSWORD:-Passenger_dev1}};TrustServerCertificate=True",
        ("redis", _) => "redis:6379",
        ("rabbitmq", { } vhost) => $"amqp://app:${{RABBITMQ_PASSWORD:-app}}@rabbitmq:5672/{Uri.EscapeDataString(vhost)}",
        ("rabbitmq", null) => "amqp://app:${RABBITMQ_PASSWORD:-app}@rabbitmq:5672",
        (_, { } database) => $"mongodb://mongo:27017/{database}",
        _ => "mongodb://mongo:27017",
    };

    /// <summary>What compose.yaml says over it of the databases the apps' connection strings name: SQL Server's image
    /// makes none, nor Postgres' more than POSTGRES_DB's, so the rest are the app's to make.</summary>
    internal IReadOnlyList<string> Comments(IReadOnlyList<string> databases) => Service switch
    {
        "sqlserver" when databases.Count > 0 =>
            [$"SQL Server's image makes no database: {string.Join(", ", databases)} {(databases.Count == 1 ? "is" : "are")} the app's to make (EF Core's Migrate or EnsureCreated) or yours, with sqlcmd"],
        "postgres" when databases.Count > 1 =>
            [$"Postgres makes POSTGRES_DB's database alone: {string.Join(", ", databases.Skip(1))} {(databases.Count == 2 ? "is" : "are")} the app's to make (EF Core's Migrate or EnsureCreated)"],
        _ => [],
    };

    /// <summary>Its settings and where it keeps its data, as compose.yaml writes them under it: Postgres made with the
    /// first database the apps name, RabbitMQ with the first virtual host.</summary>
    internal IReadOnlyList<string> Body(IReadOnlyList<string> databases) => Service switch
    {
        "postgres" => ["environment:", "  POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-postgres}", .. databases.Take(1).Select(d => $"  POSTGRES_DB: {d}"),
            "volumes:", "  - postgres-data:/var/lib/postgresql/data"],
        "sqlserver" => ["platform: linux/amd64", "environment:", "  ACCEPT_EULA: \"Y\"", "  MSSQL_SA_PASSWORD: ${MSSQL_SA_PASSWORD:-Passenger_dev1}", "volumes:", "  - sqlserver-data:/var/opt/mssql"],
        "rabbitmq" => ["environment:", "  RABBITMQ_DEFAULT_USER: app", "  RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD:-app}", .. databases.Take(1).Select(v => $"  RABBITMQ_DEFAULT_VHOST: {v}")],
        "mongo" => ["volumes:", "  - mongo-data:/data/db"],
        _ => [],
    };

    /// <summary>The named volume it keeps its data in, declared at the file's foot; null for one that keeps none.</summary>
    internal string? Volume => Service is "postgres" or "sqlserver" or "mongo" ? $"{Service}-data" : null;
}

/// <summary>The files Add > Docker Support and Add > Docker Compose write: a project's Dockerfile (its stages base, build,
/// publish and final, as .NET's own images are layered), the .dockerignore at the build's context, and a solution's
/// compose.yaml. Text alone: the window writes them as one command (MainWindow.DockerSupport.cs).</summary>
public static class DockerFiles
{
    /// <summary>A project as its Dockerfile needs it, from its evaluation and the solution's other projects.</summary>
    public static DockerProject Describe(ProjectModel project, IReadOnlyList<ProjectModel> all)
    {
        EvaluatedProject? evaluated = project.Evaluated;
        string bare = System.IO.Path.GetFileNameWithoutExtension(project.Path);
        string assembly = evaluated?.AssemblyName is { Length: > 0 } named ? named : bare;
        List<string> restores = [project.Path];
        for (int i = 0; i < restores.Count; i++)
        {
            ProjectModel? at = all.FirstOrDefault(p => PathNames.Same(p.Path, restores[i]));
            foreach (string referenced in at?.ProjectReferences ?? [])
            {
                if (!restores.Any(r => PathNames.Same(r, referenced)))
                {
                    restores.Add(referenced);
                }
            }
        }
        bool web = AspNetCore(restores, all);
        return new DockerProject(project.Path, bare, assembly, Newest(project.TargetFrameworks), web, web ? HttpPorts(project.Folder) : [], restores);
    }

    private const string AspNetCoreApp = "Microsoft.AspNetCore.App";

    /// <summary>Whether a project runs on ASP.NET Core's shared framework, so its image is aspnet's: it or a project it
    /// references (through them all) references the framework as evaluated (the Web SDK's own reference among them), or
    /// its restore says a package or a reference brings it (<c>project.assets.json</c>, as the build's runtimeconfig.json
    /// will name it).</summary>
    private static bool AspNetCore(IReadOnlyList<string> restores, IReadOnlyList<ProjectModel> all)
    {
        if (restores.Select(r => all.FirstOrDefault(p => PathNames.Same(p.Path, r))).Any(p =>
            p?.Evaluated?.Dependencies.Any(d => d.FrameworkReferences.Contains(AspNetCoreApp, StringComparer.OrdinalIgnoreCase)) == true))
        {
            return true;
        }
        if (Passenger.Xaml.Projects.ProjectInputs.FindAssets(restores[0]) is not { } assets)
        {
            return false;
        }
        try
        {
            using FileStream stream = File.OpenRead(assets);
            using JsonDocument read = JsonDocument.Parse(stream);
            JsonElement root = read.RootElement;
            bool own = root.TryGetProperty("project", out JsonElement project) && project.TryGetProperty("frameworks", out JsonElement frameworks)
                && frameworks.ValueKind == JsonValueKind.Object
                && frameworks.EnumerateObject().Any(f => f.Value.TryGetProperty("frameworkReferences", out JsonElement named)
                    && named.ValueKind == JsonValueKind.Object && named.EnumerateObject().Any(n => n.Name.Equals(AspNetCoreApp, StringComparison.OrdinalIgnoreCase)));
            // What a package or a project it references brings: each library's frameworkReferences in the restore's targets.
            bool brought = root.TryGetProperty("targets", out JsonElement targets) && targets.ValueKind == JsonValueKind.Object
                && targets.EnumerateObject().Any(t => t.Value.ValueKind == JsonValueKind.Object && t.Value.EnumerateObject().Any(l =>
                    l.Value.ValueKind == JsonValueKind.Object && l.Value.TryGetProperty("frameworkReferences", out JsonElement references)
                    && references.ValueKind == JsonValueKind.Array && references.EnumerateArray().Any(r => r.GetString()?.Equals(AspNetCoreApp, StringComparison.OrdinalIgnoreCase) == true)));
            return own || brought;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A restore being written, or one that does not read: the evaluation's word stands.
            return false;
        }
    }

    /// <summary>The newest .NET a project targets (net10.0 of net8.0;net10.0), its platform left off; null for none.</summary>
    private static string? Newest(IReadOnlyList<string> frameworks) =>
        frameworks.Select(f => f.Split('-')[0])
            .Where(f => f.StartsWith("net", StringComparison.OrdinalIgnoreCase) && Version.TryParse(f[3..], out Version? v) && v.Major >= 5)
            .OrderByDescending(f => Version.Parse(f[3..]))
            .FirstOrDefault();

    /// <summary>The ports a project's launch profiles listen on over HTTP (applicationUrl), in the order they first come.</summary>
    public static IReadOnlyList<int> HttpPorts(string folder)
    {
        string settings = System.IO.Path.Combine(folder, "Properties", "launchSettings.json");
        List<int> ports = [];
        try
        {
            if (FileText.Load(settings) is not { } file)
            {
                return [];
            }
            using JsonDocument read = JsonDocument.Parse(file.Text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!read.RootElement.TryGetProperty("profiles", out JsonElement profiles) || profiles.ValueKind != JsonValueKind.Object)
            {
                return [];
            }
            foreach (JsonProperty profile in profiles.EnumerateObject())
            {
                if (profile.Value.ValueKind == JsonValueKind.Object && profile.Value.TryGetProperty("applicationUrl", out JsonElement urls) && urls.ValueKind == JsonValueKind.String)
                {
                    foreach (string url in urls.GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (Uri.TryCreate(url.Replace("*", "localhost", StringComparison.Ordinal).Replace("+", "localhost", StringComparison.Ordinal), UriKind.Absolute, out Uri? address)
                            && address.Scheme == Uri.UriSchemeHttp && !ports.Contains(address.Port))
                        {
                            ports.Add(address.Port);
                        }
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // Profiles that do not read name no ports; the image's own (8080) stands.
        }
        return ports;
    }

    /// <summary>The image tag of a target framework: 10.0 of net10.0; null for one that is not .NET 5 or later.</summary>
    private static string? Tag(string? framework) => framework is { Length: > 3 } && Version.TryParse(framework[3..], out Version? version)
        ? $"{version.Major}.{Math.Max(0, version.Minor)}" : null;

    /// <summary>A project's Dockerfile, its build's context the folder given (the solution's): the runtime stage it runs in
    /// (aspnet for an ASP.NET Core app, else runtime, at its framework's version, as its own unprivileged user, listening
    /// on its launch profiles' HTTP ports), the SDK stage that restores from the project files alone first (so a change of
    /// code reuses the restore's layer) and builds, a publish stage, and the final one.</summary>
    public static string Dockerfile(DockerProject project, string context)
    {
        string tag = Tag(project.Framework) ?? "10.0";
        int major = int.Parse(tag.Split('.')[0], System.Globalization.CultureInfo.InvariantCulture);
        string relative = Relative(context, project.Path);
        string folder = Relative(context, project.Folder);
        string file = System.IO.Path.GetFileName(project.Path);
        StringBuilder text = new();
        text.Append("# Built from the solution's folder: docker build -f ").Append(Relative(context, project.Dockerfile)).Append(" .\n");
        text.Append("FROM mcr.microsoft.com/dotnet/").Append(project.Web ? "aspnet" : "runtime").Append(':').Append(tag).Append(" AS base\n");
        if (major >= 8)
        {
            // .NET 8's images have an unprivileged user, which the app runs as.
            text.Append("USER $APP_UID\n");
        }
        text.Append("WORKDIR /app\n");
        if (project.Web && project.HttpPorts.Count > 0)
        {
            text.Append("ENV ASPNETCORE_HTTP_PORTS=").Append(string.Join(';', project.HttpPorts)).Append('\n');
            foreach (int port in project.HttpPorts)
            {
                text.Append("EXPOSE ").Append(port).Append('\n');
            }
        }
        else if (project.Web)
        {
            text.Append("EXPOSE 8080\n");
        }
        text.Append("\nFROM mcr.microsoft.com/dotnet/sdk:").Append(tag).Append(" AS build\n");
        text.Append("ARG BUILD_CONFIGURATION=Release\n");
        text.Append("WORKDIR /src\n");
        foreach (string copied in Shared(context, project).Concat(project.Restores))
        {
            text.Append("COPY [\"").Append(Relative(context, copied)).Append("\", \"").Append(Into(Relative(context, System.IO.Path.GetDirectoryName(copied)!))).Append("\"]\n");
        }
        text.Append("RUN dotnet restore \"./").Append(relative).Append("\"\n");
        text.Append("COPY . .\n");
        text.Append("WORKDIR \"/src/").Append(folder).Append("\"\n");
        text.Append("RUN dotnet build \"./").Append(file).Append("\" -c $BUILD_CONFIGURATION -o /app/build\n");
        text.Append("\nFROM build AS publish\n");
        text.Append("ARG BUILD_CONFIGURATION=Release\n");
        text.Append("RUN dotnet publish \"./").Append(file).Append("\" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false\n");
        text.Append("\nFROM base AS final\n");
        text.Append("WORKDIR /app\n");
        text.Append("COPY --from=publish /app/publish .\n");
        text.Append("ENTRYPOINT [\"dotnet\", \"").Append(project.Assembly).Append(".dll\"]\n");
        return text.ToString();
    }

    /// <summary>Where a COPY puts a file of a folder of the context into the build's work folder: the same folder under it.</summary>
    private static string Into(string folder) => folder.Length == 0 ? "./" : folder + "/";

    /// <summary>The files a restore reads besides the projects, where the context holds them: each Directory.Build.props,
    /// Directory.Packages.props, nuget.config and global.json from the context down to each project's folder, named as
    /// the disk names them (NuGet.Config, nuget.config), since the image's file system tells letter case apart.</summary>
    private static IReadOnlyList<string> Shared(string context, DockerProject project)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase) { "global.json", "nuget.config", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" };
        List<string> found = [];
        foreach (string restored in project.Restores)
        {
            for (string? folder = System.IO.Path.GetDirectoryName(restored); folder is not null && (PathNames.Same(folder, context) || PathNames.IsUnder(folder, context));
                folder = System.IO.Path.GetDirectoryName(folder))
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(folder).Where(f => names.Contains(System.IO.Path.GetFileName(f))).ToList();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    files = [];
                }
                foreach (string file in files)
                {
                    if (!found.Contains(file, StringComparer.Ordinal))
                    {
                        found.Add(file);
                    }
                }
                if (PathNames.Same(folder, context))
                {
                    break;
                }
            }
        }
        // The context's first, then deeper, as MSBuild finds them.
        return [.. found.OrderBy(f => f.Count(c => c is '/' or '\\')).ThenBy(f => f, StringComparer.Ordinal)];
    }

    /// <summary>A path from the build's context, with '/' as the Dockerfile writes it.</summary>
    public static string Relative(string context, string path)
    {
        string relative = System.IO.Path.GetRelativePath(context, path).Replace('\\', '/');
        return relative == "." ? string.Empty : relative;
    }

    /// <summary>What a build's context leaves out of the image: build output, the IDEs' and Git's folders, packages a
    /// restore makes again, secrets, and the Docker files themselves.</summary>
    public const string Ignore =
        "# Kept out of the build's context, so a change to them never rebuilds an image.\n"
        + "**/bin\n**/obj\nartifacts/\n**/.git\n**/.gitignore\n**/.vs\n**/.vscode\n**/.idea\n**/*.user\n**/node_modules\n**/npm-debug.log\n"
        + "**/.env\n**/secrets.json\n**/.dockerignore\n**/Dockerfile*\n**/compose*.y*ml\n**/docker-compose*\nLICENSE\nREADME.md\n";

    /// <summary>A compose service's name from a project's: lowercase, every run of other characters a dash.</summary>
    public static string ServiceName(string name)
    {
        StringBuilder made = new();
        foreach (char c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                made.Append(c);
            }
            else if (made.Length > 0 && made[^1] != '-')
            {
                made.Append('-');
            }
        }
        return made.ToString().Trim('-') is { Length: > 0 } service ? service : "app";
    }

    /// <summary>The connection strings a project's appsettings.json declares (ConnectionStrings), each name with its value
    /// (empty for one that is no string), in its order.</summary>
    public static IReadOnlyList<(string Name, string Value)> ConnectionStrings(string folder)
    {
        string settings = System.IO.Path.Combine(folder, "appsettings.json");
        try
        {
            if (FileText.Load(settings) is not { } file)
            {
                return [];
            }
            using JsonDocument read = JsonDocument.Parse(file.Text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return read.RootElement.ValueKind == JsonValueKind.Object && read.RootElement.TryGetProperty("ConnectionStrings", out JsonElement strings)
                && strings.ValueKind == JsonValueKind.Object
                ? [.. strings.EnumerateObject().Select(s => (s.Name, s.Value.ValueKind == JsonValueKind.String ? s.Value.GetString() ?? string.Empty : string.Empty))]
                : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>How compose.yaml builds a project's image, from the solution's folder (<paramref name="context"/>, where
    /// compose.yaml is), and the ports it publishes: Passenger's own Dockerfile (<paramref name="existing"/> null) from
    /// that folder on the launch profiles' HTTP ports; a Dockerfile the project had from the folder its COPY lines read
    /// (the project's, unless more of what they copy is found from the solution's), on the ports it sets
    /// (ASPNETCORE_HTTP_PORTS, else ASPNETCORE_URLS' HTTP ones, else what it EXPOSEs, else ASP.NET Core's 8080).</summary>
    public static (string Context, string Dockerfile, IReadOnlyList<int> Ports) BuildFor(DockerProject project, string? existing, string context)
    {
        if (existing is null)
        {
            return (".", Relative(context, project.Dockerfile), project.Web ? project.HttpPorts.Count > 0 ? project.HttpPorts : [8080] : []);
        }
        DockerfileStages stages;
        try
        {
            stages = DockerfileStages.Read(FileText.Load(existing)?.Text ?? string.Empty);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            stages = DockerfileStages.Read(string.Empty);
        }
        List<string> sources = [.. stages.Stages.SelectMany(s => s.Copies.Select(c => c.Source))];
        string from = Found(sources, context) > Found(sources, project.Folder) ? context : project.Folder;
        string folder = Relative(context, from);
        List<int> ports = [];
        if (stages.Final is { } final)
        {
            DockerfileStage[] lineage = [.. stages.Lineage(final)];
            string? Variable(string name) => lineage.Select(s => s.Environment.GetValueOrDefault(name)).FirstOrDefault(v => v is not null);
            if (Variable("ASPNETCORE_HTTP_PORTS") is { } http)
            {
                ports.AddRange(http.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(p => int.TryParse(p, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : 0).Where(n => n > 0));
            }
            else if (Variable("ASPNETCORE_URLS") is { } urls)
            {
                ports.AddRange(urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(u => Uri.TryCreate(u.Replace("*", "localhost", StringComparison.Ordinal).Replace("+", "localhost", StringComparison.Ordinal), UriKind.Absolute, out Uri? url)
                        && url.Scheme == Uri.UriSchemeHttp ? url.Port : 0).Where(n => n > 0));
            }
            if (ports.Count == 0)
            {
                ports.AddRange(lineage.SelectMany(s => s.Exposed));
            }
            if (ports.Count == 0 && (project.Web || lineage[^1].From.Contains("/dotnet/aspnet", StringComparison.OrdinalIgnoreCase)))
            {
                ports.Add(8080);
            }
        }
        return (folder.Length == 0 ? "." : folder, Relative(from, existing), [.. ports.Distinct()]);
    }

    /// <summary>How many of a Dockerfile's COPY sources a folder holds as written (a glob's folder holding a match).</summary>
    private static int Found(IEnumerable<string> sources, string folder) => sources.Count(source =>
    {
        string path = source;
        while (path.StartsWith("./", StringComparison.Ordinal))
        {
            path = path[2..];
        }
        if (path is "" or "." || source.StartsWith('/') || source.Contains("://", StringComparison.Ordinal) || source.Contains('$', StringComparison.Ordinal))
        {
            // The context itself, which every folder is, an absolute path, a URL or a variable: no sign either way.
            return false;
        }
        string full = System.IO.Path.Combine(folder, path);
        if (path.IndexOfAny(['*', '?', '[']) < 0)
        {
            return File.Exists(full) || Directory.Exists(full);
        }
        string under = System.IO.Path.GetDirectoryName(full)!;
        try
        {
            return Directory.Exists(under) && Directory.EnumerateFileSystemEntries(under, System.IO.Path.GetFileName(full)).Any();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    });

    /// <summary>A solution's compose.yaml: a service for each project, built from its Dockerfile (<see cref="BuildFor"/>:
    /// <c>Existing</c> the one it had, null for Passenger's own) and published on its HTTP ports, given its connection
    /// strings to the backing services its packages point to (<see cref="BackingService.Reached"/>, the databases they name
    /// kept), which it waits for until their healthchecks pass; and each backing service with its healthcheck and data
    /// volume, made with the first database or virtual host the apps name.</summary>
    public static string Compose(string context, IReadOnlyList<(DockerProject Project, string? Existing, IReadOnlyList<BackingService> Backing, IReadOnlyList<(string Name, string Value)> Connections)> projects)
    {
        List<BackingService> backing = [.. BackingService.Known.Where(k => projects.Any(p => p.Backing.Contains(k)))];
        List<(DockerProject Project, string? Existing, IReadOnlyList<BackingService> Uses, List<(string Name, string Value, BackingService To)> Reached)> made =
            [.. projects.Select(p => (p.Project, p.Existing, p.Backing,
                p.Connections.Select(c => (c.Name, c.Value, To: BackingService.Reached(c.Name, c.Value, p.Backing))).Where(c => c.To is not null).Select(c => (c.Name, c.Value, c.To!)).ToList()))];
        Dictionary<BackingService, List<string>> databases = backing.ToDictionary(b => b, b => made.SelectMany(m => m.Reached)
            .Where(r => r.To == b).Select(r => b.Database(r.Name, r.Value)).OfType<string>().Distinct(StringComparer.Ordinal).ToList());
        StringBuilder text = new();
        foreach (BackingService service in backing.Where(b => b.Note is not null))
        {
            text.Append("# ").Append(service.Note).Append('\n');
        }
        text.Append("services:\n");
        foreach ((DockerProject project, string? existing, IReadOnlyList<BackingService> uses, List<(string Name, string Value, BackingService To)> reached) in made)
        {
            (string from, string dockerfile, IReadOnlyList<int> ports) = BuildFor(project, existing, context);
            text.Append("  ").Append(project.Service).Append(":\n");
            text.Append("    build:\n      context: ").Append(from).Append("\n      dockerfile: ").Append(dockerfile).Append('\n');
            text.Append("    image: ").Append(project.Service).Append('\n');
            if (ports.Count > 0)
            {
                text.Append("    ports:\n");
                foreach (int port in ports)
                {
                    text.Append("      - \"").Append(port).Append(':').Append(port).Append("\"\n");
                }
            }
            if (reached.Count > 0)
            {
                text.Append("    environment:\n");
                foreach ((string name, string value, BackingService to) in reached)
                {
                    text.Append("      ConnectionStrings__").Append(name).Append(": \"").Append(to.ConnectionString(name, value)).Append("\"\n");
                }
            }
            if (uses.Count > 0)
            {
                text.Append("    depends_on:\n");
                foreach (BackingService service in uses)
                {
                    text.Append("      ").Append(service.Service).Append(":\n        condition: service_healthy\n");
                }
            }
        }
        foreach (BackingService service in backing)
        {
            text.Append("  ").Append(service.Service).Append(":\n");
            foreach (string comment in service.Comments(databases[service]))
            {
                text.Append("    # ").Append(comment).Append('\n');
            }
            text.Append("    image: ").Append(service.Image).Append('\n');
            foreach (string line in service.Body(databases[service]))
            {
                text.Append("    ").Append(line).Append('\n');
            }
            text.Append("    healthcheck:\n      test: ").Append(service.Test).Append('\n');
            text.Append("      interval: 5s\n      timeout: 5s\n      retries: 10\n");
            if (service.Service == "sqlserver")
            {
                text.Append("      start_period: 20s\n");
            }
        }
        string[] volumes = [.. backing.Select(b => b.Volume).OfType<string>()];
        if (volumes.Length > 0)
        {
            text.Append("\nvolumes:\n");
            foreach (string volume in volumes)
            {
                text.Append("  ").Append(volume).Append(":\n");
            }
        }
        return text.ToString();
    }
}
