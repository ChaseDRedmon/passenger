using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class EnumExtensions
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdNotPublic(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdVisibilityMask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdPublic(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdVisibilityMask) == CorTypeAttr.tdPublic;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdNestedPublic(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdVisibilityMask) == CorTypeAttr.tdNestedPublic;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdNestedPrivate(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdVisibilityMask) == CorTypeAttr.tdNestedPrivate;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdNestedFamily(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdVisibilityMask) == CorTypeAttr.tdNestedFamily;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdNestedAssembly(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdVisibilityMask) == CorTypeAttr.tdNestedAssembly;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdNestedFamANDAssem(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdVisibilityMask) == CorTypeAttr.tdNestedFamANDAssem;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdNestedFamORAssem(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdVisibilityMask) == CorTypeAttr.tdVisibilityMask;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdNested(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdVisibilityMask) >= CorTypeAttr.tdNestedPublic;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdAutoLayout(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdLayoutMask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdSequentialLayout(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdLayoutMask) == CorTypeAttr.tdSequentialLayout;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdExplicitLayout(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdLayoutMask) == CorTypeAttr.tdExplicitLayout;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdClass(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdClassSemanticsMask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdInterface(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdClassSemanticsMask) == CorTypeAttr.tdClassSemanticsMask;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdAbstract(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdAbstract) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdSealed(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdSealed) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdSpecialName(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdSpecialName) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdImport(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdImport) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdSerializable(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdSerializable) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdWindowsRuntime(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdWindowsRuntime) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdAnsiClass(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdStringFormatMask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdUnicodeClass(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdStringFormatMask) == CorTypeAttr.tdUnicodeClass;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdAutoClass(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdStringFormatMask) == CorTypeAttr.tdAutoClass;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdCustomFormatClass(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdStringFormatMask) == CorTypeAttr.tdStringFormatMask;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdBeforeFieldInit(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdBeforeFieldInit) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdForwarder(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdForwarder) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdRTSpecialName(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdRTSpecialName) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsTdHasSecurity(this CorTypeAttr attr)
	{
		return (attr & CorTypeAttr.tdHasSecurity) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdPrivateScope(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdMemberAccessMask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdPrivate(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdMemberAccessMask) == CorMethodAttr.mdPrivate;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdFamANDAssem(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdMemberAccessMask) == CorMethodAttr.mdFamANDAssem;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdAssem(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdMemberAccessMask) == CorMethodAttr.mdAssem;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdFamily(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdMemberAccessMask) == CorMethodAttr.mdFamily;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdFamORAssem(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdMemberAccessMask) == CorMethodAttr.mdFamORAssem;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdPublic(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdMemberAccessMask) == CorMethodAttr.mdPublic;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdStatic(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdStatic) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdFinal(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdFinal) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdVirtual(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdVirtual) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdHideBySig(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdHideBySig) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdReuseSlot(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdVtableLayoutMask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdNewSlot(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdVtableLayoutMask) == CorMethodAttr.mdVtableLayoutMask;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdCheckAccessOnOverride(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdCheckAccessOnOverride) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdAbstract(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdAbstract) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdSpecialName(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdSpecialName) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdPinvokeImpl(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdPinvokeImpl) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdUnmanagedExport(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdUnmanagedExport) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdRTSpecialName(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdRTSpecialName) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdInstanceInitializer(this CorMethodAttr attr, string str)
	{
		if ((attr & CorMethodAttr.mdRTSpecialName) != 0)
		{
			return str == ".ctor";
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdClassConstructor(this CorMethodAttr attr, string str)
	{
		if ((attr & CorMethodAttr.mdRTSpecialName) != 0)
		{
			return str == ".cctor";
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdHasSecurity(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdHasSecurity) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMdRequireSecObject(this CorMethodAttr attr)
	{
		return (attr & CorMethodAttr.mdRequireSecObject) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdPrivateScope(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdFieldAccessMask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdPrivate(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdFieldAccessMask) == CorFieldAttr.fdPrivate;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdFamANDAssem(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdFieldAccessMask) == CorFieldAttr.fdFamANDAssem;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdAssembly(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdFieldAccessMask) == CorFieldAttr.fdAssembly;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdFamily(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdFieldAccessMask) == CorFieldAttr.fdFamily;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdFamORAssem(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdFieldAccessMask) == CorFieldAttr.fdFamORAssem;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdPublic(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdFieldAccessMask) == CorFieldAttr.fdPublic;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdStatic(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdStatic) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdInitOnly(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdInitOnly) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdLiteral(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdLiteral) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdNotSerialized(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdNotSerialized) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdPinvokeImpl(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdPinvokeImpl) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdSpecialName(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdSpecialName) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdHasFieldRVA(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdHasFieldRVA) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdRTSpecialName(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdRTSpecialName) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdHasFieldMarshal(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdHasFieldMarshal) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFdHasDefault(this CorFieldAttr attr)
	{
		return (attr & CorFieldAttr.fdHasDefault) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPdIn(this CorParamAttr attr)
	{
		return (attr & CorParamAttr.pdIn) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPdOut(this CorParamAttr attr)
	{
		return (attr & CorParamAttr.pdOut) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPdOptional(this CorParamAttr attr)
	{
		return (attr & CorParamAttr.pdOptional) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPdHasDefault(this CorParamAttr attr)
	{
		return (attr & CorParamAttr.pdHasDefault) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPdHasFieldMarshal(this CorParamAttr attr)
	{
		return (attr & CorParamAttr.pdHasFieldMarshal) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPrSpecialName(this CorPropertyAttr attr)
	{
		return (attr & CorPropertyAttr.prSpecialName) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPrRTSpecialName(this CorPropertyAttr attr)
	{
		return (attr & CorPropertyAttr.prRTSpecialName) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPrHasDefault(this CorPropertyAttr attr)
	{
		return (attr & CorPropertyAttr.prHasDefault) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsEvSpecialName(this CorEventAttr attr)
	{
		return (attr & CorEventAttr.evSpecialName) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsEvRTSpecialName(this CorEventAttr attr)
	{
		return (attr & CorEventAttr.evReservedMask) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMsSetter(this CorMethodSemanticsAttr attr)
	{
		return (attr & CorMethodSemanticsAttr.msSetter) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMsGetter(this CorMethodSemanticsAttr attr)
	{
		return (attr & CorMethodSemanticsAttr.msGetter) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMsOther(this CorMethodSemanticsAttr attr)
	{
		return (attr & CorMethodSemanticsAttr.msOther) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMsAddOn(this CorMethodSemanticsAttr attr)
	{
		return (attr & CorMethodSemanticsAttr.msAddOn) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMsRemoveOn(this CorMethodSemanticsAttr attr)
	{
		return (attr & CorMethodSemanticsAttr.msRemoveOn) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMsFire(this CorMethodSemanticsAttr attr)
	{
		return (attr & CorMethodSemanticsAttr.msFire) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsDclActionNil(this CorDeclSecurity attr)
	{
		return (attr & CorDeclSecurity.dclActionMask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsDclActionAnyStackModifier(this CorDeclSecurity attr)
	{
		if ((attr & CorDeclSecurity.dclActionMask) != CorDeclSecurity.dclAssert && (attr & CorDeclSecurity.dclActionMask) != CorDeclSecurity.dclDeny)
		{
			return (attr & CorDeclSecurity.dclActionMask) == CorDeclSecurity.dclPermitOnly;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAssemblyDclAction(this CorDeclSecurity attr)
	{
		if (attr >= CorDeclSecurity.dclRequestMinimum)
		{
			return attr <= CorDeclSecurity.dclRequestRefuse;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsNGenOnlyDclAction(this CorDeclSecurity attr)
	{
		if (attr != CorDeclSecurity.dclPrejitGrant)
		{
			return attr == CorDeclSecurity.dclPrejitDenied;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiIL(this CorMethodImpl attr)
	{
		return (attr & CorMethodImpl.miCodeTypeMask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiNative(this CorMethodImpl attr)
	{
		return (attr & CorMethodImpl.miCodeTypeMask) == CorMethodImpl.miNative;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiOPTIL(this CorMethodImpl attr)
	{
		return (attr & CorMethodImpl.miCodeTypeMask) == CorMethodImpl.miOPTIL;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiRuntime(this CorMethodImpl attr)
	{
		return (attr & CorMethodImpl.miCodeTypeMask) == CorMethodImpl.miCodeTypeMask;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiUnmanaged(this CorMethodImpl attr)
	{
		return (attr & CorMethodImpl.miManagedMask) == CorMethodImpl.miManagedMask;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiManaged(this CorMethodImpl attr)
	{
		return (attr & CorMethodImpl.miManagedMask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiForwardRef(this CorMethodImpl attr)
	{
		return (attr & CorMethodImpl.miForwardRef) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiPreserveSig(this CorMethodImpl attr)
	{
		return (attr & CorMethodImpl.miPreserveSig) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiInternalCall(this CorMethodImpl attr)
	{
		return (attr & CorMethodImpl.miInternalCall) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiSynchronized(this CorMethodImpl attr)
	{
		return (attr & CorMethodImpl.miSynchronized) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiNoInlining(this CorMethodImpl attr)
	{
		return (attr & CorMethodImpl.miNoInlining) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiAggressiveInlining(this CorMethodImpl attr)
	{
		return (attr & CorMethodImpl.miAggressiveInlining) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiNoOptimization(this CorMethodImpl attr)
	{
		return (attr & CorMethodImpl.miNoOptimization) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMiAggressiveOptimization(this CorMethodImpl attr)
	{
		return (attr & (CorMethodImpl)0x240) == CorMethodImpl.miAggressiveOptimization;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmNoMangle(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmNoMangle) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmCharSetNotSpec(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmCharSetMask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmCharSetAnsi(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmCharSetMask) == CorPinvokeMap.pmCharSetAnsi;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmCharSetUnicode(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmCharSetMask) == CorPinvokeMap.pmCharSetUnicode;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmCharSetAuto(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmCharSetMask) == CorPinvokeMap.pmCharSetMask;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmSupportsLastError(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmSupportsLastError) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmCallConvWinapi(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmCallConvMask) == CorPinvokeMap.pmCallConvWinapi;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmCallConvCdecl(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmCallConvMask) == CorPinvokeMap.pmCallConvCdecl;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmCallConvStdcall(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmCallConvMask) == CorPinvokeMap.pmCallConvStdcall;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmCallConvThiscall(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmCallConvMask) == CorPinvokeMap.pmCallConvThiscall;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmCallConvFastcall(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmCallConvMask) == CorPinvokeMap.pmCallConvFastcall;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmBestFitEnabled(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmBestFitMask) == CorPinvokeMap.pmBestFitEnabled;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmBestFitDisabled(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmBestFitMask) == CorPinvokeMap.pmBestFitDisabled;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmBestFitUseAssem(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmBestFitMask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmThrowOnUnmappableCharEnabled(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmThrowOnUnmappableCharMask) == CorPinvokeMap.pmThrowOnUnmappableCharEnabled;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmThrowOnUnmappableCharDisabled(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmThrowOnUnmappableCharMask) == CorPinvokeMap.pmThrowOnUnmappableCharDisabled;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPmThrowOnUnmappableCharUseAssem(this CorPinvokeMap attr)
	{
		return (attr & CorPinvokeMap.pmThrowOnUnmappableCharMask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfRetargetable(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afRetargetable) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfContentType_Default(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afContentType_Mask) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfContentType_WindowsRuntime(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afContentType_Mask) == CorAssemblyFlags.afContentType_WindowsRuntime;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfPA_MSIL(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afPA_NoPlatform) == CorAssemblyFlags.afPA_MSIL;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfPA_x86(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afPA_NoPlatform) == CorAssemblyFlags.afPA_x86;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfPA_IA64(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afPA_NoPlatform) == CorAssemblyFlags.afPA_IA64;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfPA_AMD64(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afPA_NoPlatform) == CorAssemblyFlags.afPA_AMD64;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfPA_ARM(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afPA_NoPlatform) == CorAssemblyFlags.afPA_ARM;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfPA_ARM64(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afPA_NoPlatform) == CorAssemblyFlags.afPA_ARM64;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfPA_NoPlatform(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afPA_FullMask) == CorAssemblyFlags.afPA_NoPlatform;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfPA_Specified(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afPA_Specified) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool PAIndex(this CorAssemblyFlags attr)
	{
		return (int)(attr & CorAssemblyFlags.afPA_NoPlatform) >> 4 != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool PAFlag(this CorAssemblyFlags attr)
	{
		return (((int)attr << 4) & 0x70) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CorAssemblyFlags PrepareForSaving(this CorAssemblyFlags attr)
	{
		return (CorAssemblyFlags)((int)attr & (((attr & CorAssemblyFlags.afPA_Specified) != 0) ? (-129) : (-241)));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfEnableJITcompileTracking(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afEnableJITcompileTracking) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfDisableJITcompileOptimizer(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afDisableJITcompileOptimizer) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfPublicKey(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afPublicKey) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAfPublicKeyToken(this CorAssemblyFlags attr)
	{
		return (attr & CorAssemblyFlags.afPublicKey) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMrPublic(this CorManifestResourceFlags attr)
	{
		return (attr & CorManifestResourceFlags.mrVisibilityMask) == CorManifestResourceFlags.mrPublic;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMrPrivate(this CorManifestResourceFlags attr)
	{
		return (attr & CorManifestResourceFlags.mrVisibilityMask) == CorManifestResourceFlags.mrPrivate;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFfContainsMetaData(this CorFileFlags attr)
	{
		return (attr & CorFileFlags.ffContainsNoMetaData) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFfContainsNoMetaData(this CorFileFlags attr)
	{
		return (attr & CorFileFlags.ffContainsNoMetaData) != 0;
	}
}
