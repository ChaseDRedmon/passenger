namespace Passenger.Debugger.Interop;

public readonly record struct mdFile
{
    /// <summary>Gets the Row ID of the metadata token.</summary>
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    /// <summary>Gets the raw numeric value of the metadata token.</summary>
    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly mdFile Nil = new mdFile(637534208u);

    public mdFile(uint value)
    {
        Value = value;
    }

    public static implicit operator mdFile(int value)
    {
        return new mdFile((uint)value);
    }

    public static implicit operator int(mdFile value)
    {
        return (int)value.Value;
    }

    public static implicit operator uint(mdFile value)
    {
        return value.Value;
    }

    public static implicit operator mdToken(mdFile value)
    {
        return new mdToken(value.Value);
    }
}
