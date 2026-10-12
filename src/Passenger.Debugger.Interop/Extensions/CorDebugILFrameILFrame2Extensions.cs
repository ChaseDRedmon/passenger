using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugILFrameILFrame2Extensions
{
	extension(ICorDebugILFrame instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugILFrame2" />
		public bool SupportsILFrame2
		{
			get
			{
				return instance is ICorDebugILFrame2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugILFrame2" />
		public ICorDebugILFrame2 ILFrame2
		{
			get
			{
				return (instance as ICorDebugILFrame2) ?? throw new NotSupportedException("ICorDebugILFrame does not support ICorDebugILFrame2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame2.TryEnumerateTypeParameters(ICorDebugSharp.ICorDebugTypeEnum@)" />
		public int TryEnumerateTypeParameters(out ICorDebugTypeEnum ppTyParEnum)
		{
			return get_ILFrame2(instance).TryEnumerateTypeParameters(out ppTyParEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame2.TryEnumerateTypeParameters(ICorDebugSharp.ICorDebugTypeEnum@)" />
		public IEnumerable<ICorDebugType> EnumerateTypeParameters()
		{
			return get_ILFrame2(instance).EnumerateTypeParameters();
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame2.TryEnumerateTypeParameters(ICorDebugSharp.ICorDebugTypeEnum@)" />
		public ICorDebugType[] TypeParameters
		{
			get
			{
				return get_ILFrame2(instance).EnumerateTypeParameters().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame2.TryRemapFunction(System.UInt32)" />
		public int TryRemapFunction(uint newILOffset)
		{
			return get_ILFrame2(instance).TryRemapFunction(newILOffset);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame2.TryRemapFunction(System.UInt32)" />
		public void RemapFunction(int newILOffset)
		{
			get_ILFrame2(instance).RemapFunction(newILOffset);
		}
	}
}
