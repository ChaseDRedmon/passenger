namespace ICorDebugSharp;

public readonly record struct mdString
{
	/// <summary>Gets the Row ID of the metadata token.</summary>
	public int Rid => (int)(Value & 0xFFFFFF);

	public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

	/// <summary>Gets the raw numeric value of the metadata token.</summary>
	public uint Value { get; }

	public bool IsNil => Rid == 0;

	public static readonly mdString Nil = new mdString(1879048192u);

	public mdString(uint value)
	{
		Value = value;
	}

	public static implicit operator mdString(int value)
	{
		return new mdString((uint)value);
	}

	public static implicit operator int(mdString value)
	{
		return (int)value.Value;
	}

	public static implicit operator uint(mdString value)
	{
		return value.Value;
	}

	public static implicit operator mdToken(mdString value)
	{
		return new mdToken(value.Value);
	}
}
