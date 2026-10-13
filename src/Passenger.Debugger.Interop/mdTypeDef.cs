#nullable enable

namespace Passenger.Debugger.Interop;

public readonly record struct mdTypeDef
{
    /// <summary>Gets the Row ID of the metadata token.</summary>
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    /// <summary>Gets the raw numeric value of the metadata token.</summary>
    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly mdTypeDef Nil = new mdTypeDef(33554432u);

    public mdTypeDef(uint value)
    {
        Value = value;
    }

    public static implicit operator mdTypeDef(int value)
    {
        return new mdTypeDef((uint)value);
    }

    public static implicit operator int(mdTypeDef value)
    {
        return (int)value.Value;
    }

    public static implicit operator uint(mdTypeDef value)
    {
        return value.Value;
    }

    public static implicit operator mdToken(mdTypeDef value)
    {
        return new mdToken(value.Value);
    }
}
