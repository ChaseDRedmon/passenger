using System.Globalization;

namespace Passenger.Dap;

/// <summary>What the IDE and the debug adapter read from the environment, named once for both ends: how long the adapter
/// lets an evaluation run (the IDE's own adapter has it as the IDE has it, a container's is given it), and the file the IDE
/// has its adapters keep the engine's log in.</summary>
public static class AdapterEnvironment
{
    /// <summary>The variable that gives <see cref="EvaluationTimeout"/> in milliseconds (the tests' run, shorter).</summary>
    public const string EvaluationTimeoutVariable = "PASSENGER_EVAL_TIMEOUT_MS";

    /// <summary>The variable naming a file the engine's own log of every session is kept in, for a stop or an end that goes
    /// wrong; in a container, its adapter keeps it in the container's own temporary folder.</summary>
    public const string EngineLogVariable = "PASSENGER_ENGINE_LOG";

    /// <summary>How long an evaluation runs when nothing says otherwise.</summary>
    public static readonly TimeSpan DefaultEvaluationTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long code the debugger runs in the program may take: <see cref="DefaultEvaluationTimeout"/>, or what
    /// <see cref="EvaluationTimeoutVariable"/> gives; a value there that is no whole number of milliseconds above 0 is fatal,
    /// as a malformed flag is (the IDE checks it before any window starts, the adapter as it starts).</summary>
    public static TimeSpan EvaluationTimeout => Environment.GetEnvironmentVariable(EvaluationTimeoutVariable) is { Length: > 0 } given
        ? int.TryParse(given, NumberStyles.None, CultureInfo.InvariantCulture, out int milliseconds) && milliseconds > 0
            ? TimeSpan.FromMilliseconds(milliseconds)
            : throw new FormatException($"{EvaluationTimeoutVariable} takes a whole number of milliseconds above 0, not '{given}'")
        : DefaultEvaluationTimeout;

    /// <summary>The file <see cref="EngineLogVariable"/> names, or null when it names none.</summary>
    public static string? EngineLog => Environment.GetEnvironmentVariable(EngineLogVariable) is { Length: > 0 } file ? file : null;
}
