namespace ICorDebugSharp;

public readonly record struct mdProperty
{
	/// <summary>Gets the Row ID of the metadata token.</summary>
	public int Rid => (int)(Value & 0xFFFFFF);

	public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

	/// <summary>Gets the raw numeric value of the metadata token.</summary>
	public uint Value { get; }

	public bool IsNil => Rid == 0;

	public static readonly mdProperty Nil = new mdProperty(385875968u);

	public mdProperty(uint value)
	{
		Value = value;
	}

	public static implicit operator mdProperty(int value)
	{
		return new mdProperty((uint)value);
	}

	public static implicit operator int(mdProperty value)
	{
		return (int)value.Value;
	}

	public static implicit operator uint(mdProperty value)
	{
		return value.Value;
	}

	public static implicit operator mdToken(mdProperty value)
	{
		return new mdToken(value.Value);
	}
}
