using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using Passenger.App.State;
using Passenger.Text;
using Passenger.Xaml.Metadata;

namespace Passenger.App.Xaml;

/// <summary>A library's type as code to read, for go to declaration from XAML: decompiled by ILSpy's decompiler from the
/// package's implementation assembly (its lib/ beside the ref/ a project compiles against; the runtime's own for the
/// framework's), written once per assembly under the state folder's MetadataAsSource/, where the editor shows it
/// read-only, as Roslyn's server keeps what it decompiles.</summary>
public static partial class LibrarySources
{
    /// <summary>Where the decompiled files are kept.</summary>
    public static string Folder => Path.Combine(StateStore.DefaultRoot, "MetadataAsSource");

    /// <summary>A library type's decompiled file, and where in it a member (or the type itself) is declared; null when its
    /// assembly cannot be read.</summary>
    public static (string Path, TextPosition Position)? Decompile(ClrType type, ClrMember? member)
    {
        if (type.Origin.AssemblyPath is not { } reference)
        {
            return null;
        }
        string assembly = Implementation(reference);
        string outermost = type.FullName.Split('+')[0];
        string folder = Path.Combine(Folder, "Decompiled", type.Origin.AssemblyName, Stamp(assembly));
        string path = Path.Combine(folder, outermost.Replace('`', '_') + ".cs");
        if (!File.Exists(path))
        {
            try
            {
                CSharpDecompiler decompiler = new(assembly, new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });
                string code = decompiler.DecompileTypeAsString(new FullTypeName(outermost));
                Directory.CreateDirectory(folder);
                File.WriteAllText(path, code);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException
                or ICSharpCode.Decompiler.Metadata.MetadataFileNotSupportedException)
            {
                return null;
            }
        }
        string[] lines = File.ReadAllLines(path);
        return (path, Declaration(lines, member?.Name ?? type.Name, enumMember: member is { Kind: MemberKind.Field } && type.Kind == ClrTypeKind.Enum));
    }

    /// <summary>The assembly with the code: a package's lib/ beside the ref/ compiled against, the running runtime's for
    /// the framework's reference pack, else the one given.</summary>
    public static string Implementation(string reference)
    {
        string separator = Path.DirectorySeparatorChar.ToString();
        string refFolder = $"{separator}ref{separator}";
        if (reference.Contains(refFolder, StringComparison.Ordinal))
        {
            string lib = reference.Replace(refFolder, $"{separator}lib{separator}", StringComparison.Ordinal);
            if (File.Exists(lib))
            {
                return lib;
            }
        }
        if (reference.Contains($"{separator}packs{separator}", StringComparison.Ordinal))
        {
            string runtime = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), Path.GetFileName(reference));
            if (File.Exists(runtime))
            {
                return runtime;
            }
        }
        return reference;
    }

    /// <summary>A folder name for one version of an assembly, so a package updated decompiles afresh.</summary>
    public static string Stamp(string assembly)
    {
        FileInfo file = new(assembly);
        return $"{(uint)HashCode.Combine(assembly.GetHashCode(StringComparison.Ordinal), file.LastWriteTimeUtc.Ticks, file.Length):x8}";
    }

    /// <summary>Where a name is declared in decompiled code: the first line declaring it with an access modifier (an enum's
    /// member, the line it starts), else its first mention, else the top.</summary>
    private static TextPosition Declaration(string[] lines, string name, bool enumMember)
    {
        Regex word = new($@"\b{Regex.Escape(name)}\b", RegexOptions.CultureInvariant);
        int mentioned = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            Match found = word.Match(lines[i]);
            if (!found.Success)
            {
                continue;
            }
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }
            if ((enumMember && trimmed.StartsWith(name, StringComparison.Ordinal)) || (!enumMember && Modifier().IsMatch(trimmed)))
            {
                return new TextPosition(i, found.Index);
            }
            if (mentioned < 0)
            {
                mentioned = i;
            }
        }
        return mentioned < 0 ? new TextPosition(0, 0) : new TextPosition(mentioned, Math.Max(0, word.Match(lines[mentioned]).Index));
    }

    [GeneratedRegex(@"^(public|protected|internal)\b")]
    private static partial Regex Modifier();
}
