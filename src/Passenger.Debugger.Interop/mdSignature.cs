#nullable enable

namespace Passenger.Debugger.Interop;

public readonly record struct mdSignature
{
    /// <summary>Gets the Row ID of the metadata token.</summary>
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    /// <summary>Gets the raw numeric value of the metadata token.</summary>
    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly mdSignature Nil = new mdSignature(285212672u);

    public mdSignature(uint value)
    {
        Value = value;
    }

    public static implicit operator mdSignature(int value)
    {
        return new mdSignature((uint)value);
    }

    public static implicit operator int(mdSignature value)
    {
        return (int)value.Value;
    }

    public static implicit operator uint(mdSignature value)
    {
        return value.Value;
    }

    public static implicit operator mdToken(mdSignature value)
    {
        return new mdToken(value.Value);
    }
}
