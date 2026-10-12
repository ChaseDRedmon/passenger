using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugSymbolProviderSymbolProvider2Extensions
{
    extension(ICorDebugSymbolProvider instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugSymbolProvider2" />
        public bool SupportsSymbolProvider2
        {
            get
            {
                return instance is ICorDebugSymbolProvider2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugSymbolProvider2" />
        public ICorDebugSymbolProvider2 SymbolProvider2
        {
            get
            {
                return (instance as ICorDebugSymbolProvider2) ?? throw new NotSupportedException("ICorDebugSymbolProvider does not support ICorDebugSymbolProvider2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider2.TryGetFrameProps(System.UInt32,System.UInt32@,System.UInt32@)" />
        public int TryGetFrameProps(uint codeRva, out uint pCodeStartRva, out uint pParentFrameStartRva)
        {
            return instance.SymbolProvider2.TryGetFrameProps(codeRva, out pCodeStartRva, out pParentFrameStartRva);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider2.TryGetFrameProps(System.UInt32,System.UInt32@,System.UInt32@)" />
        public (int pCodeStartRva, int pParentFrameStartRva) GetFrameProps(int codeRva)
        {
            return instance.SymbolProvider2.GetFrameProps(codeRva);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider2.TryGetGenericDictionaryInfo(Passenger.Debugger.Interop.ICorDebugMemoryBuffer@)" />
        public int TryGetGenericDictionaryInfo(out ICorDebugMemoryBuffer ppMemoryBuffer)
        {
            return instance.SymbolProvider2.TryGetGenericDictionaryInfo(out ppMemoryBuffer);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider2.TryGetGenericDictionaryInfo(Passenger.Debugger.Interop.ICorDebugMemoryBuffer@)" />
        public ICorDebugMemoryBuffer GenericDictionaryInfo
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.SymbolProvider2.TryGetGenericDictionaryInfo(out ICorDebugMemoryBuffer ppMemoryBuffer));
                return ppMemoryBuffer;
            }
        }
    }
}
