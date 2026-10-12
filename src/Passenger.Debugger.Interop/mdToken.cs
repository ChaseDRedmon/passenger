namespace Passenger.Debugger.Interop;

public readonly record struct mdToken
{
    /// <summary>Gets the Row ID of the metadata token.</summary>
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    /// <summary>Gets the raw numeric value of the metadata token.</summary>
    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly mdToken Nil = new mdToken(0u);

    public mdToken(uint value)
    {
        Value = value;
    }

    public static implicit operator mdToken(int value)
    {
        return new mdToken((uint)value);
    }

    public static implicit operator int(mdToken value)
    {
        return (int)value.Value;
    }

    public static implicit operator uint(mdToken value)
    {
        return value.Value;
    }
}
