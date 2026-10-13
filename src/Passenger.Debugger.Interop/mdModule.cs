#nullable enable

namespace Passenger.Debugger.Interop;

public readonly record struct mdModule
{
    /// <summary>Gets the Row ID of the metadata token.</summary>
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    /// <summary>Gets the raw numeric value of the metadata token.</summary>
    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly mdModule Nil = new mdModule(0u);

    public mdModule(uint value)
    {
        Value = value;
    }

    public static implicit operator mdModule(int value)
    {
        return new mdModule((uint)value);
    }

    public static implicit operator int(mdModule value)
    {
        return (int)value.Value;
    }

    public static implicit operator uint(mdModule value)
    {
        return value.Value;
    }

    public static implicit operator mdToken(mdModule value)
    {
        return new mdToken(value.Value);
    }
}
