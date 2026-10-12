using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugVariableHomeExtensions
{
	extension(ICorDebugVariableHome instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugVariableHome.TryGetArgumentIndex(System.UInt32@)" />
		public int ArgumentIndex
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetArgumentIndex(out var pArgumentIndex));
				return checked((int)pArgumentIndex);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugVariableHome.TryGetCode(ICorDebugSharp.ICorDebugCode@)" />
		public ICorDebugCode Code
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetCode(out ICorDebugCode ppCode));
				return ppCode;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugVariableHome.TryGetLiveRange(System.UInt32@,System.UInt32@)" />
		public (int pStartOffset, int pEndOffset) LiveRange
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetLiveRange(out var pStartOffset, out var pEndOffset));
				return checked((pStartOffset: (int)pStartOffset, pEndOffset: (int)pEndOffset));
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugVariableHome.TryGetLocationType(ICorDebugSharp.VariableLocationType@)" />
		public VariableLocationType LocationType
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetLocationType(out var pLocationType));
				return pLocationType;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugVariableHome.TryGetOffset(System.Int32@)" />
		public int Offset
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetOffset(out var pOffset));
				return pOffset;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugVariableHome.TryGetRegister(ICorDebugSharp.CorDebugRegister@)" />
		public CorDebugRegister Register
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetRegister(out var pRegister));
				return pRegister;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugVariableHome.TryGetSlotIndex(System.UInt32@)" />
		public int SlotIndex
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetSlotIndex(out var pSlotIndex));
				return checked((int)pSlotIndex);
			}
		}
	}
}
