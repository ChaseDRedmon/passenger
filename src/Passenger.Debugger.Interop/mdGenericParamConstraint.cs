namespace ICorDebugSharp;

public readonly record struct mdGenericParamConstraint
{
	/// <summary>Gets the Row ID of the metadata token.</summary>
	public int Rid => (int)(Value & 0xFFFFFF);

	public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

	/// <summary>Gets the raw numeric value of the metadata token.</summary>
	public uint Value { get; }

	public bool IsNil => Rid == 0;

	public static readonly mdGenericParamConstraint Nil = new mdGenericParamConstraint(738197504u);

	public mdGenericParamConstraint(uint value)
	{
		Value = value;
	}

	public static implicit operator mdGenericParamConstraint(int value)
	{
		return new mdGenericParamConstraint((uint)value);
	}

	public static implicit operator int(mdGenericParamConstraint value)
	{
		return (int)value.Value;
	}

	public static implicit operator uint(mdGenericParamConstraint value)
	{
		return value.Value;
	}

	public static implicit operator mdToken(mdGenericParamConstraint value)
	{
		return new mdToken(value.Value);
	}
}
