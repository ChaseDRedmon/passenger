namespace Passenger.Debugger.PresentationHintModels;

public record struct VariablePresentationHint
{
    public PresentationHintKind? Kind { get; set; }
    public AttributesValue? Attributes { get; set; }

    /// <summary>The protocol's lazy row: its value is read only as it is opened, its reference then naming one row, the value
    /// in its place, since reading it runs code in the program (an EF Core query's SQL).</summary>
    public bool? Lazy { get; set; }
}
