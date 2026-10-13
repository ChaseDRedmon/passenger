#nullable enable

namespace Passenger.Debugger.Interop;

public readonly record struct mdEvent
{
    /// <summary>Gets the Row ID of the metadata token.</summary>
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    /// <summary>Gets the raw numeric value of the metadata token.</summary>
    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly mdEvent Nil = new mdEvent(335544320u);

    public mdEvent(uint value)
    {
        Value = value;
    }

    public static implicit operator mdEvent(int value)
    {
        return new mdEvent((uint)value);
    }

    public static implicit operator int(mdEvent value)
    {
        return (int)value.Value;
    }

    public static implicit operator uint(mdEvent value)
    {
        return value.Value;
    }

    public static implicit operator mdToken(mdEvent value)
    {
        return new mdToken(value.Value);
    }
}
