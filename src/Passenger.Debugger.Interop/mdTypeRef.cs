#nullable enable

namespace Passenger.Debugger.Interop;

public readonly record struct mdTypeRef
{
    /// <summary>Gets the Row ID of the metadata token.</summary>
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    /// <summary>Gets the raw numeric value of the metadata token.</summary>
    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly mdTypeRef Nil = new mdTypeRef(16777216u);

    public mdTypeRef(uint value)
    {
        Value = value;
    }

    public static implicit operator mdTypeRef(int value)
    {
        return new mdTypeRef((uint)value);
    }

    public static implicit operator int(mdTypeRef value)
    {
        return (int)value.Value;
    }

    public static implicit operator uint(mdTypeRef value)
    {
        return value.Value;
    }

    public static implicit operator mdToken(mdTypeRef value)
    {
        return new mdToken(value.Value);
    }
}
