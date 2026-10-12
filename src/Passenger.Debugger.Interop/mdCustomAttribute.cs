namespace Passenger.Debugger.Interop;

public readonly record struct mdCustomAttribute
{
    /// <summary>Gets the Row ID of the metadata token.</summary>
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    /// <summary>Gets the raw numeric value of the metadata token.</summary>
    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly mdCustomAttribute Nil = new mdCustomAttribute(201326592u);

    public mdCustomAttribute(uint value)
    {
        Value = value;
    }

    public static implicit operator mdCustomAttribute(int value)
    {
        return new mdCustomAttribute((uint)value);
    }

    public static implicit operator int(mdCustomAttribute value)
    {
        return (int)value.Value;
    }

    public static implicit operator uint(mdCustomAttribute value)
    {
        return value.Value;
    }

    public static implicit operator mdToken(mdCustomAttribute value)
    {
        return new mdToken(value.Value);
    }
}
