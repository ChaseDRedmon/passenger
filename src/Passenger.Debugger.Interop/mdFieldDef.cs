namespace Passenger.Debugger.Interop;

public readonly record struct mdFieldDef
{
    /// <summary>Gets the Row ID of the metadata token.</summary>
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    /// <summary>Gets the raw numeric value of the metadata token.</summary>
    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly mdFieldDef Nil = new mdFieldDef(67108864u);

    public mdFieldDef(uint value)
    {
        Value = value;
    }

    public static implicit operator mdFieldDef(int value)
    {
        return new mdFieldDef((uint)value);
    }

    public static implicit operator int(mdFieldDef value)
    {
        return (int)value.Value;
    }

    public static implicit operator uint(mdFieldDef value)
    {
        return value.Value;
    }

    public static implicit operator mdToken(mdFieldDef value)
    {
        return new mdToken(value.Value);
    }
}
