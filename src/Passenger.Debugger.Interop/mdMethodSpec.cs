namespace ICorDebugSharp;

public readonly record struct mdMethodSpec
{
	/// <summary>Gets the Row ID of the metadata token.</summary>
	public int Rid => (int)(Value & 0xFFFFFF);

	public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

	/// <summary>Gets the raw numeric value of the metadata token.</summary>
	public uint Value { get; }

	public bool IsNil => Rid == 0;

	public static readonly mdMethodSpec Nil = new mdMethodSpec(721420288u);

	public mdMethodSpec(uint value)
	{
		Value = value;
	}

	public static implicit operator mdMethodSpec(int value)
	{
		return new mdMethodSpec((uint)value);
	}

	public static implicit operator int(mdMethodSpec value)
	{
		return (int)value.Value;
	}

	public static implicit operator uint(mdMethodSpec value)
	{
		return value.Value;
	}

	public static implicit operator mdToken(mdMethodSpec value)
	{
		return new mdToken(value.Value);
	}
}
