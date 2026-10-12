namespace ICorDebugSharp;

public readonly record struct mdParamDef
{
	/// <summary>Gets the Row ID of the metadata token.</summary>
	public int Rid => (int)(Value & 0xFFFFFF);

	public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

	/// <summary>Gets the raw numeric value of the metadata token.</summary>
	public uint Value { get; }

	public bool IsNil => Rid == 0;

	public static readonly mdParamDef Nil = new mdParamDef(134217728u);

	public mdParamDef(uint value)
	{
		Value = value;
	}

	public static implicit operator mdParamDef(int value)
	{
		return new mdParamDef((uint)value);
	}

	public static implicit operator int(mdParamDef value)
	{
		return (int)value.Value;
	}

	public static implicit operator uint(mdParamDef value)
	{
		return value.Value;
	}

	public static implicit operator mdToken(mdParamDef value)
	{
		return new mdToken(value.Value);
	}
}
