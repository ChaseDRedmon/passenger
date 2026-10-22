namespace Passenger.Core;

/// <summary>The environment variables of .NET's runtime, its host and WPF that Passenger sets or reads in the programs it
/// starts, each named once. Framework-free, as what Passenger puts inside another program (the inspectors, the test frames'
/// hook, the WPF previewer) compiles it in too: linked, since those never load Passenger's own assemblies.</summary>
public static class RuntimeVariables
{
    /// <summary>The runtime's list of startup hooks, each an assembly it calls before the program's Main; Passenger's own
    /// go first through StartupHooks.First.</summary>
    public const string StartupHooks = "DOTNET_STARTUP_HOOKS";

    /// <summary>"1" holds the runtime at its start, before the program's first instruction, until a diagnostic client
    /// (a debugger attaching, a profiler) lets it go; the variable stays in the program's environment for every program it
    /// starts, so whoever lets it go clears it there.</summary>
    public const string DefaultDiagnosticPortSuspend = "DOTNET_DefaultDiagnosticPortSuspend";

    /// <summary>A diagnostic port the runtime connects to as it starts (address, then connect or listen, suspend or
    /// nosuspend), as dotnet-trace's --diagnostic-port gives one.</summary>
    public const string DiagnosticPorts = "DOTNET_DiagnosticPorts";

    /// <summary>"debug" lets a debugger change the program's unoptimized code as it runs (Hot Reload), read by the runtime
    /// as each module loads.</summary>
    public const string ModifiableAssemblies = "DOTNET_MODIFIABLE_ASSEMBLIES";

    /// <summary>"1" lets the host roll a program forward onto a prerelease runtime when no release one will do.</summary>
    public const string RollForwardToPrerelease = "DOTNET_ROLL_FORWARD_TO_PRERELEASE";

    /// <summary>"1" has WPF record where in its XAML each object was made (VisualDiagnostics.GetXamlSourceInfo) and raise
    /// BindingDiagnostics.BindingFailed, as Visual Studio has it for a run it starts. WPF reads it as it first loads XAML,
    /// so it stays in the app's environment.</summary>
    public const string XamlDiagnosticsSourceInfo = "ENABLE_XAML_DIAGNOSTICS_SOURCE_INFO";
}
