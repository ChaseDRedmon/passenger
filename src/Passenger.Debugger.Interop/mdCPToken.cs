namespace ICorDebugSharp;

public readonly record struct mdCPToken
{
	/// <summary>Gets the Row ID of the metadata token.</summary>
	public int Rid => (int)(Value & 0xFFFFFF);

	public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

	/// <summary>Gets the raw numeric value of the metadata token.</summary>
	public uint Value { get; }

	public bool IsNil => Rid == 0;

	public static readonly mdCPToken Nil = new mdCPToken(0u);

	public mdCPToken(uint value)
	{
		Value = value;
	}

	public static implicit operator mdCPToken(int value)
	{
		return new mdCPToken((uint)value);
	}

	public static implicit operator int(mdCPToken value)
	{
		return (int)value.Value;
	}

	public static implicit operator uint(mdCPToken value)
	{
		return value.Value;
	}

	public static implicit operator mdToken(mdCPToken value)
	{
		return new mdToken(value.Value);
	}
}
