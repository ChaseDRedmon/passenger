using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Passenger.Core;

namespace Passenger.Profiler;

/// <summary>Where profiled methods are written, from their modules' portable PDBs (beside the module or embedded in it):
/// a method's file and the line of its first sequence point, as the debugger places a frame at a method's start. Each
/// module is read once, and let go with this.</summary>
public sealed class MethodSources : IDisposable
{
    private readonly Dictionary<string, (PEReader Module, MetadataReaderProvider? Pdb)?> read = new(StringComparer.Ordinal);

    /// <summary>The method's source file, as its PDB names it, and line; null where the module has no PDB that matches it
    /// (a framework's or a package's, or one built since) or the method no sequence point (a generated one).</summary>
    public (string File, int Line)? Find(MethodCode code)
    {
        if ((code.Token >>> 24) != 0x06)
        {
            return null;
        }
        if (!read.TryGetValue(code.ModuleFile, out (PEReader Module, MetadataReaderProvider? Pdb)? opened))
        {
            read[code.ModuleFile] = opened = Open(code.ModuleFile);
        }
        if (opened?.Pdb is not { } pdb)
        {
            return null;
        }
        try
        {
            MetadataReader reader = pdb.GetMetadataReader();
            MethodDefinitionHandle method = MetadataTokens.MethodDefinitionHandle(code.Token & 0xFFFFFF);
            if (MetadataTokens.GetRowNumber(method) > reader.MethodDebugInformation.Count)
            {
                return null;
            }
            foreach (SequencePoint point in reader.GetMethodDebugInformation(method).GetSequencePoints())
            {
                if (!point.IsHidden)
                {
                    return (reader.GetString(reader.GetDocument(point.Document).Name), point.StartLine);
                }
            }
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>Where some of a snapshot's methods are written, those found: from their modules' PDBs here, or, for a run
    /// of a program in a container (<see cref="Snapshot.Place"/>), there by Passenger's tools (<c>sources</c>), which read
    /// the modules the trace names where they are, each document then made this machine's by the place's map. Off the
    /// caller's thread; a container gone, or tools that fail, find none.</summary>
    public static async Task<IReadOnlyDictionary<MethodCode, (string File, int Line)>> FindAsync(Snapshot snapshot, IEnumerable<MethodCode> codes,
        CancellationToken cancel = default)
    {
        MethodCode[] asked = [.. codes.Distinct()];
        Dictionary<MethodCode, (string File, int Line)> found = [];
        if (asked.Length == 0)
        {
            return found;
        }
        if (snapshot.Place is not { } place)
        {
            return await Task.Run(() =>
            {
                using MethodSources sources = new();
                foreach (MethodCode code in asked)
                {
                    if (sources.Find(code) is { } at)
                    {
                        found[code] = at;
                    }
                }
                return found;
            }, cancel).ConfigureAwait(false);
        }
        string input = string.Concat(asked.Select(c => $"{c.Token.ToString(CultureInfo.InvariantCulture)}\t{c.ModuleFile}\n"));
        ChildResult answered;
        try
        {
            answered = await ChildProcesses.RunAsync(place.Tools(["sources"]), input, TimeSpan.FromSeconds(30), cancel).ConfigureAwait(false);
        }
        catch (Exception failed) when (failed is TimeoutException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return found;
        }
        if (!answered.Succeeded)
        {
            return found;
        }
        string[] lines = answered.Output.Split('\n');
        for (int i = 0; i < asked.Length && i < lines.Length; i++)
        {
            string[] parts = lines[i].TrimEnd('\r').Split('\t');
            if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int line))
            {
                found[asked[i]] = (place.ToHost(parts[0]), line);
            }
        }
        return found;
    }

    public void Dispose()
    {
        foreach ((PEReader Module, MetadataReaderProvider? Pdb)? opened in read.Values)
        {
            opened?.Pdb?.Dispose();
            opened?.Module.Dispose();
        }
        read.Clear();
    }

    private static (PEReader, MetadataReaderProvider?)? Open(string module)
    {
        if (!File.Exists(module))
        {
            return null;
        }
        PEReader? pe = null;
        try
        {
            pe = new PEReader(File.OpenRead(module));
            pe.TryOpenAssociatedPortablePdb(module, path => File.Exists(path) ? File.OpenRead(path) : null, out MetadataReaderProvider? pdb, out _);
            return (pe, pdb);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            pe?.Dispose();
            return null;
        }
    }
}
