using Passenger.Debugger.PresentationHintModels;

namespace Passenger.Debugger.Models.Response;

public class VariableInfo
{
    public required string Name { get; set; }

    /// <summary>The expression that names the value, where it is not the name: a return value's $ReturnValue.</summary>
    public string? EvaluateName { get; set; }
    public required string Value { get; set; }
    public required string? Type { get; set; }
    public required int VariablesReference { get; set; }
    public VariablePresentationHint? PresentationHint { get; set; }
}
