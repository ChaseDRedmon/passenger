namespace ICorDebugSharp;

public readonly record struct mdInterfaceImpl
{
	/// <summary>Gets the Row ID of the metadata token.</summary>
	public int Rid => (int)(Value & 0xFFFFFF);

	public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

	/// <summary>Gets the raw numeric value of the metadata token.</summary>
	public uint Value { get; }

	public bool IsNil => Rid == 0;

	public static readonly mdInterfaceImpl Nil = new mdInterfaceImpl(150994944u);

	public mdInterfaceImpl(uint value)
	{
		Value = value;
	}

	public static implicit operator mdInterfaceImpl(int value)
	{
		return new mdInterfaceImpl((uint)value);
	}

	public static implicit operator int(mdInterfaceImpl value)
	{
		return (int)value.Value;
	}

	public static implicit operator uint(mdInterfaceImpl value)
	{
		return value.Value;
	}

	public static implicit operator mdToken(mdInterfaceImpl value)
	{
		return new mdToken(value.Value);
	}
}
