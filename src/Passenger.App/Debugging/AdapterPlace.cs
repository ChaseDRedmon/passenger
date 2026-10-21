using System.Diagnostics;

namespace Passenger.App.Debugging;

/// <summary>How a path the IDE names (a breakpoint's file, Set Next Statement's, a Hot Reload update's document, a launched
/// program and its folder) is named where the program runs, and back (a frame's source, an active statement's document):
/// applied at the protocol's edge in <see cref="DebugSession"/>. The same path by default, which a container's program
/// built on this machine (its PDB naming this machine's files) needs; a container run's path map where its image was built
/// with the sources at the container's own paths. A run configuration's map plugs in as
/// <c>new DebugPaths(map.ToContainer, map.ToHost)</c>.</summary>
public sealed record DebugPaths(Func<string, string> ToProgram, Func<string, string> ToIde)
{
    public static DebugPaths Same { get; } = new(path => path, path => path);
}

/// <summary>Where a session's debug adapter runs when not beside the app: the command that starts it there, given the
/// adapter's flags (<c>docker exec -i CONTAINER …/passenger-tools dap</c>, the protocol over its standard input and output
/// as ever), the folder it keeps decompiled symbols in there, how paths cross (<see cref="DebugPaths"/>), and what to call
/// the place (a container's name). A session with one is <see cref="DebugSession.Elsewhere"/>: its program's process id is
/// the container's, never held or killed here. Ending the command (Stop, Detach, the IDE's end) closes the adapter's
/// standard input there, which lets the program go as a client's going does.</summary>
public sealed record AdapterPlace(Func<IReadOnlyList<string>, ProcessStartInfo> Command, string SymbolCache, DebugPaths Paths, string Where);
