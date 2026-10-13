#nullable enable

namespace Passenger.Debugger.Interop;

public readonly record struct mdPermission
{
    /// <summary>Gets the Row ID of the metadata token.</summary>
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    /// <summary>Gets the raw numeric value of the metadata token.</summary>
    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly mdPermission Nil = new mdPermission(234881024u);

    public mdPermission(uint value)
    {
        Value = value;
    }

    public static implicit operator mdPermission(int value)
    {
        return new mdPermission((uint)value);
    }

    public static implicit operator int(mdPermission value)
    {
        return (int)value.Value;
    }

    public static implicit operator uint(mdPermission value)
    {
        return value.Value;
    }

    public static implicit operator mdToken(mdPermission value)
    {
        return new mdToken(value.Value);
    }
}
