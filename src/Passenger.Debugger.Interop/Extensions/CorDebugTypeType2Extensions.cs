using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugTypeType2Extensions
{
	extension(ICorDebugType instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugType2" />
		public bool SupportsType2
		{
			get
			{
				return instance is ICorDebugType2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugType2" />
		public ICorDebugType2 Type2
		{
			get
			{
				return (instance as ICorDebugType2) ?? throw new NotSupportedException("ICorDebugType does not support ICorDebugType2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugType2.TryGetTypeID(ICorDebugSharp.COR_TYPEID@)" />
		public int TryGetTypeID(out COR_TYPEID id)
		{
			return get_Type2(instance).TryGetTypeID(out id);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugType2.TryGetTypeID(ICorDebugSharp.COR_TYPEID@)" />
		public COR_TYPEID TypeID
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Type2(instance).TryGetTypeID(out var id));
				return id;
			}
		}
	}
}
