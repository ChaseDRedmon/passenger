using System.Reflection;
using System.Runtime.Loader;
using Passenger.Core;
using Passenger.TestFrames;

/// <summary>The runtime calls this before the test program's Main, as DOTNET_STARTUP_HOOKS names this assembly: only in a
/// test run Passenger started, never in the project as built. Nothing here touches Avalonia, which the tests have not set
/// up yet: the hook takes itself out of the environment (so nothing the tests start loads it), checks the project's
/// Avalonia, and starts watching for failures in a context of its own. Nothing it does can stop the tests.</summary>
internal static class StartupHook
{
    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable(Variables.Frames) != "1")
        {
            Unlist();
            return;
        }
        // VSTest's test host runs an xunit v3 project's tests in the project's own program, which it starts: that program
        // inherits the hook from it, so here the hook stays listed and does nothing.
        if (Assembly.GetEntryAssembly()?.GetName().Name is { } entry && entry.StartsWith("testhost", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        Unlist();
        try
        {
            if (!OnAvalonia12())
            {
                return;
            }
            string self = typeof(StartupHook).Assembly.Location;
            Assembly inside = new AppAvaloniaContext(self).LoadFromAssemblyPath(self);
            inside.GetType("Passenger.TestFrames.Frames", throwOnError: true)!
                .GetMethod("Watch", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null);
        }
        catch (Exception error) when (error is IOException or BadImageFormatException or TypeLoadException or MissingMethodException or TargetInvocationException)
        {
            // A program the hook cannot serve runs its tests as it would without it.
        }
    }

    /// <summary>This hook and its variable, out of the environment the tests' own programs inherit; other hooks stay.</summary>
    private static void Unlist()
    {
        Environment.SetEnvironmentVariable(Variables.Frames, null);
        StartupHooks.TakeOut(typeof(StartupHook).Assembly.Location);
    }

    /// <summary>Whether the program will load Avalonia 12, from the assemblies the runtime was given for it.</summary>
    private static bool OnAvalonia12()
    {
        string? path = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?.Split(Path.PathSeparator)
            .FirstOrDefault(p => string.Equals(Path.GetFileName(p), "Avalonia.Base.dll", StringComparison.OrdinalIgnoreCase));
        return path is not null && AssemblyName.GetAssemblyName(path).Version?.Major == StartupHooks.AvaloniaMajor;
    }
}

/// <summary>Where the hook runs: its own context, which takes Avalonia's assemblies from the test program whatever their 12.x
/// version, since the default context would refuse one older than the 12.x it was compiled against.</summary>
internal sealed class AppAvaloniaContext(string hook) : AssemblyLoadContext($"Passenger test frames ({Path.GetFileName(hook)})")
{
    protected override Assembly? Load(AssemblyName name) =>
        name.Name is { } simple && (simple.StartsWith("Avalonia", StringComparison.Ordinal) || simple.StartsWith("MicroCom", StringComparison.Ordinal))
            ? Default.LoadFromAssemblyName(new AssemblyName(simple))
            : null;
}
