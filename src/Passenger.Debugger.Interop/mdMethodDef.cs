namespace Passenger.Debugger.Interop;

public readonly record struct mdMethodDef
{
    /// <summary>Gets the Row ID of the metadata token.</summary>
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    /// <summary>Gets the raw numeric value of the metadata token.</summary>
    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly mdMethodDef Nil = new mdMethodDef(100663296u);

    public mdMethodDef(uint value)
    {
        Value = value;
    }

    public static implicit operator mdMethodDef(int value)
    {
        return new mdMethodDef((uint)value);
    }

    public static implicit operator int(mdMethodDef value)
    {
        return (int)value.Value;
    }

    public static implicit operator uint(mdMethodDef value)
    {
        return value.Value;
    }

    public static implicit operator mdToken(mdMethodDef value)
    {
        return new mdToken(value.Value);
    }
}
