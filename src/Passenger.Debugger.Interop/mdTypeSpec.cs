namespace ICorDebugSharp;

public readonly record struct mdTypeSpec
{
	/// <summary>Gets the Row ID of the metadata token.</summary>
	public int Rid => (int)(Value & 0xFFFFFF);

	public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

	/// <summary>Gets the raw numeric value of the metadata token.</summary>
	public uint Value { get; }

	public bool IsNil => Rid == 0;

	public static readonly mdTypeSpec Nil = new mdTypeSpec(452984832u);

	public mdTypeSpec(uint value)
	{
		Value = value;
	}

	public static implicit operator mdTypeSpec(int value)
	{
		return new mdTypeSpec((uint)value);
	}

	public static implicit operator int(mdTypeSpec value)
	{
		return (int)value.Value;
	}

	public static implicit operator uint(mdTypeSpec value)
	{
		return value.Value;
	}

	public static implicit operator mdToken(mdTypeSpec value)
	{
		return new mdToken(value.Value);
	}
}
