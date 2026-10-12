using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcessProcess7Extensions
{
	extension(ICorDebugProcess instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess7" />
		public bool SupportsProcess7
		{
			get
			{
				return instance is ICorDebugProcess7;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess7" />
		public ICorDebugProcess7 Process7
		{
			get
			{
				return (instance as ICorDebugProcess7) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess7.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess7.TrySetWriteableMetadataUpdateMode(ICorDebugSharp.WriteableMetadataUpdateMode)" />
		public int TrySetWriteableMetadataUpdateMode(WriteableMetadataUpdateMode flags)
		{
			return get_Process7(instance).TrySetWriteableMetadataUpdateMode(flags);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess7.TrySetWriteableMetadataUpdateMode(ICorDebugSharp.WriteableMetadataUpdateMode)" />
		public void SetWriteableMetadataUpdateMode(WriteableMetadataUpdateMode flags)
		{
			get_Process7(instance).SetWriteableMetadataUpdateMode(flags);
		}
	}
}
