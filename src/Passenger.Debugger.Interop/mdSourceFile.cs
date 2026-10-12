namespace ICorDebugSharp;

public readonly record struct mdSourceFile
{
	/// <summary>Gets the Row ID of the metadata token.</summary>
	public int Rid => (int)(Value & 0xFFFFFF);

	public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

	/// <summary>Gets the raw numeric value of the metadata token.</summary>
	public uint Value { get; }

	public bool IsNil => Rid == 0;

	public static readonly mdSourceFile Nil = new mdSourceFile(0u);

	public mdSourceFile(uint value)
	{
		Value = value;
	}

	public static implicit operator mdSourceFile(int value)
	{
		return new mdSourceFile((uint)value);
	}

	public static implicit operator int(mdSourceFile value)
	{
		return (int)value.Value;
	}

	public static implicit operator uint(mdSourceFile value)
	{
		return value.Value;
	}

	public static implicit operator mdToken(mdSourceFile value)
	{
		return new mdToken(value.Value);
	}
}
