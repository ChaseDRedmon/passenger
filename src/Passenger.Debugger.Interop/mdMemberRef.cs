namespace Passenger.Debugger.Interop;

public readonly record struct mdMemberRef
{
    /// <summary>Gets the Row ID of the metadata token.</summary>
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    /// <summary>Gets the raw numeric value of the metadata token.</summary>
    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly mdMemberRef Nil = new mdMemberRef(167772160u);

    public mdMemberRef(uint value)
    {
        Value = value;
    }

    public static implicit operator mdMemberRef(int value)
    {
        return new mdMemberRef((uint)value);
    }

    public static implicit operator int(mdMemberRef value)
    {
        return (int)value.Value;
    }

    public static implicit operator uint(mdMemberRef value)
    {
        return value.Value;
    }

    public static implicit operator mdToken(mdMemberRef value)
    {
        return new mdToken(value.Value);
    }
}
