using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugILCodeILCode2Extensions
{
	extension(ICorDebugILCode instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugILCode2" />
		public bool SupportsILCode2
		{
			get
			{
				return instance is ICorDebugILCode2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugILCode2" />
		public ICorDebugILCode2 ILCode2
		{
			get
			{
				return (instance as ICorDebugILCode2) ?? throw new NotSupportedException("ICorDebugILCode does not support ICorDebugILCode2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILCode2.TryGetInstrumentedILMap(System.UInt32,System.UInt32@,ICorDebugSharp.COR_IL_MAP[])" />
		public int TryGetInstrumentedILMap(uint cMap, out uint pcMap, COR_IL_MAP[]? map)
		{
			return get_ILCode2(instance).TryGetInstrumentedILMap(cMap, out pcMap, map);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILCode2.TryGetLocalVarSigToken(ICorDebugSharp.mdSignature@)" />
		public int TryGetLocalVarSigToken(out mdSignature pmdSig)
		{
			return get_ILCode2(instance).TryGetLocalVarSigToken(out pmdSig);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILCode2.TryGetLocalVarSigToken(ICorDebugSharp.mdSignature@)" />
		public mdSignature LocalVarSigToken
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_ILCode2(instance).TryGetLocalVarSigToken(out var pmdSig));
				return pmdSig;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILCode2.TryGetInstrumentedILMap(System.UInt32,System.UInt32@,ICorDebugSharp.COR_IL_MAP[])" />
		public COR_IL_MAP[] InstrumentedILMap
		{
			get
			{
				return CorDebugILCode2Extensions.get_InstrumentedILMap(get_ILCode2(instance));
			}
		}
	}
}
