namespace Passenger.Dap;

/// <summary>What Passenger's debugger says of a breakpoint's condition it could not tell (one that does not evaluate, or is
/// no bool), in the breakpoint's message and in the console the IDE shows: here, so both ends say it in the same words.</summary>
public static class BreakpointConditions
{
    /// <summary>How the debugger begins to say it, before what stopped and why.</summary>
    public static string NotEvaluated(string condition) => $"The condition '{condition}' could not be evaluated";
}
