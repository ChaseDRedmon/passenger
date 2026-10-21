using System.Diagnostics;
using Passenger.App.Containers;

namespace Passenger.App.Debugging;

/// <summary>Where a session's debug adapter runs when not beside the app: the command that starts it there, given the
/// adapter's flags (<c>docker exec -i CONTAINER …/passenger-tools dap</c>, the protocol over its standard input and output
/// as ever), the folder it keeps decompiled symbols in there, how paths cross at the protocol's edge (a container run's
/// <see cref="ContainerPaths"/>: a breakpoint's file, Set Next Statement's, Hot Reload's line updates, a launched program
/// and its folder named as the container names them, and a frame's source and an active statement's document back; a
/// path the map does not cover, or <see cref="ContainerPaths.None"/>, crosses as it is, as a program built on this machine
/// needs), and what to call the place (the container's name, which docker exec reaches it by: Attach Profiler runs the
/// profiler's tools there too). A session with one is <see cref="DebugSession.Elsewhere"/>:
/// its program's process id is the container's, never held or killed here. Ending the command (Stop, Detach, the IDE's
/// end, the CLI killed) closes the adapter's standard input there, which lets the program go as a client's going does.</summary>
public sealed record AdapterPlace(Func<IReadOnlyList<string>, ProcessStartInfo> Command, string SymbolCache, ContainerPaths Paths, string Where)
{
    /// <summary>What follows the program's output from outside, shown in its session's console as a launch's is (a container
    /// run's service: <c>docker logs --follow</c> of its container from its start), where an attach gets none through the
    /// protocol; null for a program attached to as it already ran, whose output goes where it always went.</summary>
    public Func<ProcessStartInfo>? Output { get; init; }

    /// <summary>A path of this machine as the program's names it.</summary>
    public string ToProgram(string path) => Paths.ToContainer(path) ?? path;

    /// <summary>A path the program names as this machine's.</summary>
    public string ToIde(string path) => Paths.ToHost(path) ?? path;
}
