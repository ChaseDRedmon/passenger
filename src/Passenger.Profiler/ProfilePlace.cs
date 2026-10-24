using System.Diagnostics;

namespace Passenger.Profiler;

/// <summary>Where a program to profile runs when it is not on this machine (a Linux container's): the command that runs
/// Passenger's container tools there with the words given (<c>docker exec -i CONTAINER …/passenger-tools trace …</c>, their
/// standard input and output the profiler's), what to call the place (the container's name), and how a path the program
/// names (a PDB's source document) is this machine's: as it is for a program built here, else through the run's path map.
/// The program's process id is the place's own, never one of this machine's.</summary>
public sealed record ProfilePlace(Func<IReadOnlyList<string>, ProcessStartInfo> Tools, string Where, Func<string, string> ToHost);
