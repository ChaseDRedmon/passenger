using Passenger.Dap;

namespace Passenger.Tests.Debugging.Helpers;

public static class VariableAssertions
{
    /// <summary>The variables as a variables window would show them, each whole on one line, so a
    /// failure lists the lines that differ; in any order, as upstream's tests held them. An empty
    /// presentation hint reads as none, which is how the protocol leaves it out.</summary>
    public static void ShouldBeEquivalentToDebuggerVariables(this List<DapVariable> variables, List<DapVariable> expectedVariables) =>
        Assert.Equal(expectedVariables.Select(Describe).Order(StringComparer.Ordinal), variables.Select(Describe).Order(StringComparer.Ordinal));

    private static string Describe(DapVariable variable)
    {
        string hint = variable.PresentationHint is { } h
            ? $"{h.Kind}/{string.Join('|', h.Attributes ?? [])}/{h.Visibility}"
            : "//";
        return $"{variable.Name} = {variable.Value} : {variable.Type} (evaluate {variable.EvaluateName}, reference {variable.VariablesReference}, hint {hint})";
    }
}
