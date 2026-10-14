using System.Reflection;
using System.Runtime.Loader;
using Passenger.Inspector;

/// <summary>The runtime calls this before the app's Main, as DOTNET_STARTUP_HOOKS names this assembly: only in a run
/// Passenger launched for its Live Visual Tree, never in the app as shipped. Nothing here touches Avalonia, which the app has
/// not set up yet: the hook takes itself out of the environment (so nothing the app starts loads it), checks the app's
/// Avalonia, and starts the inspector in a context of its own. Nothing it does can stop the app.</summary>
internal static class StartupHook
{
    public static void Initialize()
    {
        string? socket = Environment.GetEnvironmentVariable(Wire.SocketVariable);
        Unlist();
        if (socket is not { Length: > 0 })
        {
            return;
        }
        try
        {
            Thread main = Thread.CurrentThread;
            (Version? avalonia, string? problem) = Avalonia();
            if (problem is not null)
            {
                Connection.Refuse(socket, avalonia, problem);
                return;
            }
            string self = typeof(StartupHook).Assembly.Location;
            Assembly inside = new AppAvaloniaContext(self).LoadFromAssemblyPath(self);
            inside.GetType("Passenger.Inspector.Agent", throwOnError: true)!
                .GetMethod("Start", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [socket, main, avalonia!.ToString()]);
        }
        catch (Exception error)
        {
            Connection.Refuse(socket, null, $"The inspector could not start in the app: {error.GetBaseException().Message}");
        }
    }

    /// <summary>This hook and the socket, out of the environment the app's own programs inherit; other hooks stay.</summary>
    private static void Unlist()
    {
        Environment.SetEnvironmentVariable(Wire.SocketVariable, null);
        string self = typeof(StartupHook).Assembly.Location;
        if (Environment.GetEnvironmentVariable(Wire.StartupHooks) is { Length: > 0 } hooks)
        {
            string[] others = [.. hooks.Split(Path.PathSeparator).Where(h => h.Length > 0 && !string.Equals(Path.GetFullPath(h), self, StringComparison.Ordinal))];
            Environment.SetEnvironmentVariable(Wire.StartupHooks, others.Length == 0 ? null : string.Join(Path.PathSeparator, others));
        }
    }

    /// <summary>The app's Avalonia, from the assemblies it will load, and why the inspector cannot serve it, if it cannot.</summary>
    private static (Version? Version, string? Problem) Avalonia()
    {
        string? path = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?.Split(Path.PathSeparator)
            .FirstOrDefault(p => string.Equals(Path.GetFileName(p), "Avalonia.Base.dll", StringComparison.OrdinalIgnoreCase));
        if (path is null)
        {
            return (null, "The app does not use Avalonia");
        }
        Version? version = AssemblyName.GetAssemblyName(path).Version;
        return version?.Major == Wire.AvaloniaMajor
            ? (version, null)
            : (version, $"The app runs Avalonia {version?.ToString(3)}; the Live Visual Tree needs Avalonia {Wire.AvaloniaMajor}");
    }
}

/// <summary>Where the inspector runs: its own context, which takes Avalonia's assemblies from the app whatever their 12.x
/// version, since the default context would refuse one older than the 12.x it was compiled against.</summary>
internal sealed class AppAvaloniaContext(string inspector) : AssemblyLoadContext($"Passenger inspector ({Path.GetFileName(inspector)})")
{
    protected override Assembly? Load(AssemblyName name) =>
        name.Name is { } simple && (simple.StartsWith("Avalonia", StringComparison.Ordinal) || simple.StartsWith("MicroCom", StringComparison.Ordinal))
            ? Default.LoadFromAssemblyName(new AssemblyName(simple))
            : null;
}
