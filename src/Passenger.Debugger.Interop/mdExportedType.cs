namespace Passenger.Debugger.Interop;

public readonly record struct mdExportedType
{
    /// <summary>Gets the Row ID of the metadata token.</summary>
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    /// <summary>Gets the raw numeric value of the metadata token.</summary>
    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly mdExportedType Nil = new mdExportedType(654311424u);

    public mdExportedType(uint value)
    {
        Value = value;
    }

    public static implicit operator mdExportedType(int value)
    {
        return new mdExportedType((uint)value);
    }

    public static implicit operator int(mdExportedType value)
    {
        return (int)value.Value;
    }

    public static implicit operator uint(mdExportedType value)
    {
        return value.Value;
    }

    public static implicit operator mdToken(mdExportedType value)
    {
        return new mdToken(value.Value);
    }
}
