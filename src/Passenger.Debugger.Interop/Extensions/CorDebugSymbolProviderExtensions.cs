using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugSymbolProviderExtensions
{
    extension(ICorDebugSymbolProvider instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider.TryGetInstanceFieldSymbols(System.UInt32,System.Byte[],System.UInt32,System.UInt32@,Passenger.Debugger.Interop.ICorDebugInstanceFieldSymbol[])" />
        public (ICorDebugInstanceFieldSymbol[] pSymbols, int pcFetchedSymbols) GetInstanceFieldSymbols(int cbSignature, byte[] typeSig, int cRequestedSymbols)
        {
            ICorDebugInstanceFieldSymbol[] array = new ICorDebugInstanceFieldSymbol[cRequestedSymbols];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetInstanceFieldSymbols((uint)cbSignature, typeSig, (uint)cRequestedSymbols, out var pcFetchedSymbols, array));
                return (pSymbols: array, pcFetchedSymbols: (int)pcFetchedSymbols);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider.TryGetMergedAssemblyRecords(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.ICorDebugMergedAssemblyRecord[])" />
        public (ICorDebugMergedAssemblyRecord[] pRecords, int pcFetchedRecords) GetMergedAssemblyRecords(int cRequestedRecords)
        {
            ICorDebugMergedAssemblyRecord[] array = new ICorDebugMergedAssemblyRecord[cRequestedRecords];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetMergedAssemblyRecords((uint)cRequestedRecords, out var pcFetchedRecords, array));
                return (pRecords: array, pcFetchedRecords: (int)pcFetchedRecords);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider.TryGetMethodLocalSymbols(System.UInt32,System.UInt32,System.UInt32@,Passenger.Debugger.Interop.ICorDebugVariableSymbol[])" />
        public (ICorDebugVariableSymbol[] pSymbols, int pcFetchedSymbols) GetMethodLocalSymbols(int nativeRVA, int cRequestedSymbols)
        {
            ICorDebugVariableSymbol[] array = new ICorDebugVariableSymbol[cRequestedSymbols];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetMethodLocalSymbols((uint)nativeRVA, (uint)cRequestedSymbols, out var pcFetchedSymbols, array));
                return (pSymbols: array, pcFetchedSymbols: (int)pcFetchedSymbols);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider.TryGetMethodParameterSymbols(System.UInt32,System.UInt32,System.UInt32@,Passenger.Debugger.Interop.ICorDebugVariableSymbol[])" />
        public (ICorDebugVariableSymbol[] pSymbols, int pcFetchedSymbols) GetMethodParameterSymbols(int nativeRVA, int cRequestedSymbols)
        {
            ICorDebugVariableSymbol[] array = new ICorDebugVariableSymbol[cRequestedSymbols];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetMethodParameterSymbols((uint)nativeRVA, (uint)cRequestedSymbols, out var pcFetchedSymbols, array));
                return (pSymbols: array, pcFetchedSymbols: (int)pcFetchedSymbols);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider.TryGetMethodProps(System.UInt32,Passenger.Debugger.Interop.mdToken@,System.UInt32@,System.UInt32,System.UInt32@,System.Byte[])" />
        public (byte[] signature, mdToken pMethodToken, int pcGenericParams, int pcbSignature) GetMethodProps(int codeRva, int cbSignature)
        {
            byte[] array = new byte[cbSignature];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetMethodProps((uint)codeRva, out var pMethodToken, out var pcGenericParams, (uint)cbSignature, out var pcbSignature, array));
                return (signature: array, pMethodToken: pMethodToken, pcGenericParams: (int)pcGenericParams, pcbSignature: (int)pcbSignature);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider.TryGetStaticFieldSymbols(System.UInt32,System.Byte[],System.UInt32,System.UInt32@,Passenger.Debugger.Interop.ICorDebugStaticFieldSymbol[])" />
        public (ICorDebugStaticFieldSymbol[] pSymbols, int pcFetchedSymbols) GetStaticFieldSymbols(int cbSignature, byte[] typeSig, int cRequestedSymbols)
        {
            ICorDebugStaticFieldSymbol[] array = new ICorDebugStaticFieldSymbol[cRequestedSymbols];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetStaticFieldSymbols((uint)cbSignature, typeSig, (uint)cRequestedSymbols, out var pcFetchedSymbols, array));
                return (pSymbols: array, pcFetchedSymbols: (int)pcFetchedSymbols);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider.TryGetTypeProps(System.UInt32,System.UInt32,System.UInt32@,System.Byte[])" />
        public (byte[] signature, int pcbSignature) GetTypeProps(int vtableRva, int cbSignature)
        {
            byte[] array = new byte[cbSignature];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetTypeProps((uint)vtableRva, (uint)cbSignature, out var pcbSignature, array));
                return (signature: array, pcbSignature: (int)pcbSignature);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider.TryGetAssemblyImageBytes(Passenger.Debugger.Interop.CORDB_ADDRESS,System.UInt32,Passenger.Debugger.Interop.ICorDebugMemoryBuffer@)" />
        public ICorDebugMemoryBuffer GetAssemblyImageBytes(CORDB_ADDRESS rva, int length)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetAssemblyImageBytes(rva, checked((uint)length), out ICorDebugMemoryBuffer ppMemoryBuffer));
            return ppMemoryBuffer;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider.TryGetAssemblyImageMetadata(Passenger.Debugger.Interop.ICorDebugMemoryBuffer@)" />
        public ICorDebugMemoryBuffer AssemblyImageMetadata
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetAssemblyImageMetadata(out ICorDebugMemoryBuffer ppMemoryBuffer));
                return ppMemoryBuffer;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider.TryGetCodeRange(System.UInt32,System.UInt32@,System.UInt32@)" />
        public (int pCodeStartAddress, int pCodeSize) GetCodeRange(int codeRva)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetCodeRange((uint)codeRva, out var pCodeStartAddress, out var pCodeSize));
                return (pCodeStartAddress: (int)pCodeStartAddress, pCodeSize: (int)pCodeSize);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugSymbolProvider.TryGetObjectSize(System.UInt32,System.Byte[],System.UInt32@)" />
        public int GetObjectSize(int cbSignature, byte[] typeSig)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetObjectSize((uint)cbSignature, typeSig, out var pObjectSize));
                return (int)pObjectSize;
            }
        }
    }
}
