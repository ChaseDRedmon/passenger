using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugMergedAssemblyRecordExtensions
{
    extension(ICorDebugMergedAssemblyRecord instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugMergedAssemblyRecord.TryGetSimpleName(System.UInt32,System.UInt32@,System.Char[])" />
        public string SimpleName
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetSimpleName(0u, out var pcchName, null));
                if (pcchName == 0)
                {
                    return string.Empty;
                }
                for (int i = 0; i < 3; i++)
                {
                    uint num = pcchName;
                    char[] array;
                    int num2;
                    checked
                    {
                        array = new char[(int)num];
                        Marshal.ThrowExceptionForHR(instance.TryGetSimpleName(num, out var pcchName2, array));
                        if (pcchName2 > num)
                        {
                            pcchName = pcchName2;
                            continue;
                        }
                        num2 = (int)pcchName2;
                    }
                    if (num2 > 0 && array[num2 - 1] == '\0')
                    {
                        num2--;
                    }
                    return new string(array, 0, num2);
                }
                throw new InvalidOperationException("Native buffer size did not stabilize.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugMergedAssemblyRecord.TryGetCulture(System.UInt32,System.UInt32@,System.Char[])" />
        public string Culture
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetCulture(0u, out var pcchCulture, null));
                if (pcchCulture == 0)
                {
                    return string.Empty;
                }
                for (int i = 0; i < 3; i++)
                {
                    uint num = pcchCulture;
                    char[] array;
                    int num2;
                    checked
                    {
                        array = new char[(int)num];
                        Marshal.ThrowExceptionForHR(instance.TryGetCulture(num, out var pcchCulture2, array));
                        if (pcchCulture2 > num)
                        {
                            pcchCulture = pcchCulture2;
                            continue;
                        }
                        num2 = (int)pcchCulture2;
                    }
                    if (num2 > 0 && array[num2 - 1] == '\0')
                    {
                        num2--;
                    }
                    return new string(array, 0, num2);
                }
                throw new InvalidOperationException("Native buffer size did not stabilize.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugMergedAssemblyRecord.TryGetPublicKey(System.UInt32,System.UInt32@,System.Byte[])" />
        public (byte[] pbPublicKey, int pcbPublicKey) GetPublicKey(int cbPublicKey)
        {
            byte[] array = new byte[cbPublicKey];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetPublicKey((uint)cbPublicKey, out var pcbPublicKey, array));
                return (pbPublicKey: array, pcbPublicKey: (int)pcbPublicKey);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugMergedAssemblyRecord.TryGetPublicKeyToken(System.UInt32,System.UInt32@,System.Byte[])" />
        public (byte[] pbPublicKeyToken, int pcbPublicKeyToken) GetPublicKeyToken(int cbPublicKeyToken)
        {
            byte[] array = new byte[cbPublicKeyToken];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetPublicKeyToken((uint)cbPublicKeyToken, out var pcbPublicKeyToken, array));
                return (pbPublicKeyToken: array, pcbPublicKeyToken: (int)pcbPublicKeyToken);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugMergedAssemblyRecord.TryGetIndex(System.UInt32@)" />
        public int Index
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetIndex(out var pIndex));
                return checked((int)pIndex);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugMergedAssemblyRecord.TryGetVersion(System.UInt16@,System.UInt16@,System.UInt16@,System.UInt16@)" />
        public (ushort pMajor, ushort pMinor, ushort pBuild, ushort pRevision) Version
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetVersion(out var pMajor, out var pMinor, out var pBuild, out var pRevision));
                return (pMajor: pMajor, pMinor: pMinor, pBuild: pBuild, pRevision: pRevision);
            }
        }
    }
}
