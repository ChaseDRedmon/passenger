using Passenger.App.Run;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>The stand-in for dotnet dev-certs every HTTPS run is tested with (a container run's, an AppHost's), trusting
/// nothing here: it notes what it was asked, answers the check with the exit code in "check" (naming "thumbprint"'s
/// certificate as found, if any; first taking two minutes while "slow" is there), and exports a certificate and its .key,
/// both none, at its umask's modes (or fails, as a locked keychain does, while "fail" is there), noting in "folder" the
/// mode of the folder it exports into. Its first word is where it keeps those. A shell script: tests of it skip on Windows.</summary>
internal static class DevCerts
{
    internal const string Script = """
        dir=$1; shift
        echo "$*" >> "$dir/asked"
        case " $* " in
          *" --check "*) if [ -f "$dir/slow" ]; then sleep 120; fi
                         if [ -f "$dir/thumbprint" ]; then echo "A valid certificate was found: $(cat "$dir/thumbprint") - CN=localhost"; fi
                         exit "$(cat "$dir/check")" ;;
          *" -ep "*) if [ -f "$dir/fail" ]; then echo "The keychain is locked" >&2; exit 3; fi
                     while [ "$1" != "-ep" ]; do shift; done; ls -ld "$(dirname "$2")" | cut -c1-10 > "$dir/folder"
                     printf 'stand-in' > "$2"; printf 'stand-in key' > "${2%.*}.key"; exit 0 ;;
          *" --trust "*) exit 0 ;;
        esac
        exit 1
        """;

    /// <summary>A window's dotnet for dev-certs: the stand-in, keeping what it is asked and told in <paramref name="folder"/>.</summary>
    internal static DotnetWords In(string folder) => (words, at) => ChildProcesses.Command("/bin/sh", ["-c", Script, "dev-certs", folder, .. words], at);

    /// <summary>What the stand-in kept in <paramref name="folder"/> of what it was asked, a line each.</summary>
    internal static string Asked(string folder) => File.Exists(Path.Combine(folder, "asked")) ? File.ReadAllText(Path.Combine(folder, "asked")) : string.Empty;
}
