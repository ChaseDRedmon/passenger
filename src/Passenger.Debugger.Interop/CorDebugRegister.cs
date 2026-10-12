namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Specifies the registers associated with a given processor architecture.</para>
/// </summary>
/// <remarks>
/// <para>There are 128 general-purpose data registers and 128 floating-point data registers on the IA-64 processor, but only values <c>REGISTER_IA64_R0</c> and <c>REGISTER_IA64_F0</c> are provided. The other values can be determined as follows:</para>
/// <list type="bullet">
/// <item><description><para>Add the register number to <c>REGISTER_IA64_R0</c> for values <c>REGISTER_IA64_R1</c> through <c>REGISTER_IA64_R127</c>, which correspond to the #1 data register through the #127 data register on the IA-64 processor.</para>
/// </description></item>
/// <item><description><para>Add the register number to <c>REGISTER_IA64_F0</c> for values <c>REGISTER_IA64_F1</c> through <c>REGISTER_IA64_F127</c>, which correspond to the #1 FP data register through the #127 FP data register on the IA-64 processor.</para>
/// </description></item>
/// </list>
/// <para>For example, if you need to specify the #83 data register on the IA-64 processor, use <c>REGISTER_IA64_R0</c> + 83.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugregister-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugRegister
{
    /// <summary>
    /// <para>An instruction pointer register on any processor.</para>
    /// </summary>
    REGISTER_INSTRUCTION_POINTER = 0,
    /// <summary>
    /// <para>A stack pointer register on any processor.</para>
    /// </summary>
    REGISTER_STACK_POINTER = 1,
    /// <summary>
    /// <para>A frame pointer register on any processor.</para>
    /// </summary>
    REGISTER_FRAME_POINTER = 2,
    /// <summary>
    /// <para>The instruction pointer register on the x86 processor.</para>
    /// </summary>
    REGISTER_X86_EIP = REGISTER_INSTRUCTION_POINTER,
    /// <summary>
    /// <para>The stack pointer register on the x86 processor.</para>
    /// </summary>
    REGISTER_X86_ESP = REGISTER_STACK_POINTER,
    /// <summary>
    /// <para>The base pointer register on the x86 processor.</para>
    /// </summary>
    REGISTER_X86_EBP = REGISTER_FRAME_POINTER,
    /// <summary>
    /// <para>The A data register on the x86 processor.</para>
    /// </summary>
    REGISTER_X86_EAX = 3,
    /// <summary>
    /// <para>The C data register on the x86 processor.</para>
    /// </summary>
    REGISTER_X86_ECX = 4,
    /// <summary>
    /// <para>The D data register on the x86 processor.</para>
    /// </summary>
    REGISTER_X86_EDX = 5,
    /// <summary>
    /// <para>The B data register on the x86 processor.</para>
    /// </summary>
    REGISTER_X86_EBX = 6,
    /// <summary>
    /// <para>The source index register on the x86 processor.</para>
    /// </summary>
    REGISTER_X86_ESI = 7,
    /// <summary>
    /// <para>The destination index register on the x86 processor.</para>
    /// </summary>
    REGISTER_X86_EDI = 8,
    /// <summary>
    /// <para>The stack register 0 on the x86 floating-point (FP) processor.</para>
    /// </summary>
    REGISTER_X86_FPSTACK_0 = 9,
    /// <summary>
    /// <para>The #1 stack register on the x86 FP processor.</para>
    /// </summary>
    REGISTER_X86_FPSTACK_1 = 10,
    /// <summary>
    /// <para>The #2 stack register on the x86 FP processor.</para>
    /// </summary>
    REGISTER_X86_FPSTACK_2 = 11,
    /// <summary>
    /// <para>The #3 stack register on the x86 FP processor.</para>
    /// </summary>
    REGISTER_X86_FPSTACK_3 = 12,
    /// <summary>
    /// <para>The #4 stack register on the x86 FP processor.</para>
    /// </summary>
    REGISTER_X86_FPSTACK_4 = 13,
    /// <summary>
    /// <para>The #5 stack register on the x86 FP processor.</para>
    /// </summary>
    REGISTER_X86_FPSTACK_5 = 14,
    /// <summary>
    /// <para>The #6 stack register on the x86 FP processor.</para>
    /// </summary>
    REGISTER_X86_FPSTACK_6 = 15,
    /// <summary>
    /// <para>The #7 stack register on the x86 FP processor.</para>
    /// </summary>
    REGISTER_X86_FPSTACK_7 = 16,
    /// <summary>
    /// <para>The instruction pointer register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_RIP = REGISTER_INSTRUCTION_POINTER,
    /// <summary>
    /// <para>The stack pointer register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_RSP = REGISTER_STACK_POINTER,
    /// <summary>
    /// <para>The base pointer register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_RBP = REGISTER_FRAME_POINTER,
    /// <summary>
    /// <para>The A data register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_RAX = REGISTER_X86_EAX,
    /// <summary>
    /// <para>The C data register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_RCX = REGISTER_X86_ECX,
    /// <summary>
    /// <para>The D data register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_RDX = REGISTER_X86_EDX,
    /// <summary>
    /// <para>The B data register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_RBX = REGISTER_X86_EBX,
    /// <summary>
    /// <para>The source index register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_RSI = REGISTER_X86_ESI,
    /// <summary>
    /// <para>The destination index register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_RDI = REGISTER_X86_EDI,
    /// <summary>
    /// <para>The #8 data register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_R8 = REGISTER_X86_FPSTACK_0,
    /// <summary>
    /// <para>The #9 data register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_R9 = REGISTER_X86_FPSTACK_1,
    /// <summary>
    /// <para>The #10 data register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_R10 = REGISTER_X86_FPSTACK_2,
    /// <summary>
    /// <para>The #11 data register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_R11 = REGISTER_X86_FPSTACK_3,
    /// <summary>
    /// <para>The #12 data register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_R12 = REGISTER_X86_FPSTACK_4,
    /// <summary>
    /// <para>The #13 data register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_R13 = REGISTER_X86_FPSTACK_5,
    /// <summary>
    /// <para>The #14 data register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_R14 = REGISTER_X86_FPSTACK_6,
    /// <summary>
    /// <para>The #15 data register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_R15 = REGISTER_X86_FPSTACK_7,
    /// <summary>
    /// <para>The #0 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM0 = 17,
    /// <summary>
    /// <para>The #1 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM1 = 18,
    /// <summary>
    /// <para>The #2 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM2 = 19,
    /// <summary>
    /// <para>The #3 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM3 = 20,
    /// <summary>
    /// <para>The #4 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM4 = 21,
    /// <summary>
    /// <para>The #5 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM5 = 22,
    /// <summary>
    /// <para>The #6 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM6 = 23,
    /// <summary>
    /// <para>The #7 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM7 = 24,
    /// <summary>
    /// <para>The #8 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM8 = 25,
    /// <summary>
    /// <para>The #9 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM9 = 26,
    /// <summary>
    /// <para>The #10 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM10 = 27,
    /// <summary>
    /// <para>The #11 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM11 = 28,
    /// <summary>
    /// <para>The #12 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM12 = 29,
    /// <summary>
    /// <para>The #13 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM13 = 30,
    /// <summary>
    /// <para>The #14 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM14 = 31,
    /// <summary>
    /// <para>The #15 multimedia register on the AMD64 processor.</para>
    /// </summary>
    REGISTER_AMD64_XMM15 = 32,
    /// <summary>
    /// <para>The stack pointer register on the IA-64 processor.</para>
    /// </summary>
    REGISTER_IA64_BSP = REGISTER_FRAME_POINTER,
    /// <summary>
    /// <para>The #0 data register on the IA-64 processor.</para>
    /// </summary>
    REGISTER_IA64_R0 = REGISTER_X86_EAX,
    /// <summary>
    /// <para>The #0 FP data register on the IA-64 processor.</para>
    /// </summary>
    REGISTER_IA64_F0 = 131,
    /// <summary>
    /// <para>The program counter register (R15) on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_PC = REGISTER_INSTRUCTION_POINTER,
    /// <summary>
    /// <para>The stack pointer register (R13) on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_SP = REGISTER_STACK_POINTER,
    /// <summary>
    /// <para>Data register R0 on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_R0 = REGISTER_FRAME_POINTER,
    /// <summary>
    /// <para>Data register R1 on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_R1 = REGISTER_X86_EAX,
    /// <summary>
    /// <para>Data register R2 on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_R2 = REGISTER_X86_ECX,
    /// <summary>
    /// <para>Data register R3 on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_R3 = REGISTER_X86_EDX,
    /// <summary>
    /// <para>Register R4 on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_R4 = REGISTER_X86_EBX,
    /// <summary>
    /// <para>Register R5 on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_R5 = REGISTER_X86_ESI,
    /// <summary>
    /// <para>Register R6 on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_R6 = REGISTER_X86_EDI,
    /// <summary>
    /// <para>Register R7 (the THUMB frame pointer) on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_R7 = REGISTER_X86_FPSTACK_0,
    /// <summary>
    /// <para>Register R8 on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_R8 = REGISTER_X86_FPSTACK_1,
    /// <summary>
    /// <para>Register R9 on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_R9 = REGISTER_X86_FPSTACK_2,
    /// <summary>
    /// <para>Register R10 on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_R10 = REGISTER_X86_FPSTACK_3,
    /// <summary>
    /// <para>The frame pointer on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_R11 = REGISTER_X86_FPSTACK_4,
    /// <summary>
    /// <para>Register R12 on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_R12 = REGISTER_X86_FPSTACK_5,
    /// <summary>
    /// <para>The link register (R14) on the ARM processor.</para>
    /// </summary>
    REGISTER_ARM_LR = REGISTER_X86_FPSTACK_6,
    REGISTER_ARM_D0 = REGISTER_X86_FPSTACK_7,
    REGISTER_ARM_D1 = REGISTER_AMD64_XMM0,
    REGISTER_ARM_D2 = REGISTER_AMD64_XMM1,
    REGISTER_ARM_D3 = REGISTER_AMD64_XMM2,
    REGISTER_ARM_D4 = REGISTER_AMD64_XMM3,
    REGISTER_ARM_D5 = REGISTER_AMD64_XMM4,
    REGISTER_ARM_D6 = REGISTER_AMD64_XMM5,
    REGISTER_ARM_D7 = REGISTER_AMD64_XMM6,
    REGISTER_ARM_D8 = REGISTER_AMD64_XMM7,
    REGISTER_ARM_D9 = REGISTER_AMD64_XMM8,
    REGISTER_ARM_D10 = REGISTER_AMD64_XMM9,
    REGISTER_ARM_D11 = REGISTER_AMD64_XMM10,
    REGISTER_ARM_D12 = REGISTER_AMD64_XMM11,
    REGISTER_ARM_D13 = REGISTER_AMD64_XMM12,
    REGISTER_ARM_D14 = REGISTER_AMD64_XMM13,
    REGISTER_ARM_D15 = REGISTER_AMD64_XMM14,
    REGISTER_ARM_D16 = REGISTER_AMD64_XMM15,
    REGISTER_ARM_D17 = 33,
    REGISTER_ARM_D18 = 34,
    REGISTER_ARM_D19 = 35,
    REGISTER_ARM_D20 = 36,
    REGISTER_ARM_D21 = 37,
    REGISTER_ARM_D22 = 38,
    REGISTER_ARM_D23 = 39,
    REGISTER_ARM_D24 = 40,
    REGISTER_ARM_D25 = 41,
    REGISTER_ARM_D26 = 42,
    REGISTER_ARM_D27 = 43,
    REGISTER_ARM_D28 = 44,
    REGISTER_ARM_D29 = 45,
    REGISTER_ARM_D30 = 46,
    REGISTER_ARM_D31 = 47,
    REGISTER_ARM64_PC = REGISTER_INSTRUCTION_POINTER,
    REGISTER_ARM64_SP = REGISTER_STACK_POINTER,
    REGISTER_ARM64_FP = REGISTER_FRAME_POINTER,
    REGISTER_ARM64_X0 = REGISTER_X86_EAX,
    REGISTER_ARM64_X1 = REGISTER_X86_ECX,
    REGISTER_ARM64_X2 = REGISTER_X86_EDX,
    REGISTER_ARM64_X3 = REGISTER_X86_EBX,
    REGISTER_ARM64_X4 = REGISTER_X86_ESI,
    REGISTER_ARM64_X5 = REGISTER_X86_EDI,
    REGISTER_ARM64_X6 = REGISTER_X86_FPSTACK_0,
    REGISTER_ARM64_X7 = REGISTER_X86_FPSTACK_1,
    REGISTER_ARM64_X8 = REGISTER_X86_FPSTACK_2,
    REGISTER_ARM64_X9 = REGISTER_X86_FPSTACK_3,
    REGISTER_ARM64_X10 = REGISTER_X86_FPSTACK_4,
    REGISTER_ARM64_X11 = REGISTER_X86_FPSTACK_5,
    REGISTER_ARM64_X12 = REGISTER_X86_FPSTACK_6,
    REGISTER_ARM64_X13 = REGISTER_X86_FPSTACK_7,
    REGISTER_ARM64_X14 = REGISTER_AMD64_XMM0,
    REGISTER_ARM64_X15 = REGISTER_AMD64_XMM1,
    REGISTER_ARM64_X16 = REGISTER_AMD64_XMM2,
    REGISTER_ARM64_X17 = REGISTER_AMD64_XMM3,
    REGISTER_ARM64_X18 = REGISTER_AMD64_XMM4,
    REGISTER_ARM64_X19 = REGISTER_AMD64_XMM5,
    REGISTER_ARM64_X20 = REGISTER_AMD64_XMM6,
    REGISTER_ARM64_X21 = REGISTER_AMD64_XMM7,
    REGISTER_ARM64_X22 = REGISTER_AMD64_XMM8,
    REGISTER_ARM64_X23 = REGISTER_AMD64_XMM9,
    REGISTER_ARM64_X24 = REGISTER_AMD64_XMM10,
    REGISTER_ARM64_X25 = REGISTER_AMD64_XMM11,
    REGISTER_ARM64_X26 = REGISTER_AMD64_XMM12,
    REGISTER_ARM64_X27 = REGISTER_AMD64_XMM13,
    REGISTER_ARM64_X28 = REGISTER_AMD64_XMM14,
    REGISTER_ARM64_LR = REGISTER_AMD64_XMM15,
    REGISTER_ARM64_V0 = REGISTER_ARM_D17,
    REGISTER_ARM64_V1 = REGISTER_ARM_D18,
    REGISTER_ARM64_V2 = REGISTER_ARM_D19,
    REGISTER_ARM64_V3 = REGISTER_ARM_D20,
    REGISTER_ARM64_V4 = REGISTER_ARM_D21,
    REGISTER_ARM64_V5 = REGISTER_ARM_D22,
    REGISTER_ARM64_V6 = REGISTER_ARM_D23,
    REGISTER_ARM64_V7 = REGISTER_ARM_D24,
    REGISTER_ARM64_V8 = REGISTER_ARM_D25,
    REGISTER_ARM64_V9 = REGISTER_ARM_D26,
    REGISTER_ARM64_V10 = REGISTER_ARM_D27,
    REGISTER_ARM64_V11 = REGISTER_ARM_D28,
    REGISTER_ARM64_V12 = REGISTER_ARM_D29,
    REGISTER_ARM64_V13 = REGISTER_ARM_D30,
    REGISTER_ARM64_V14 = REGISTER_ARM_D31,
    REGISTER_ARM64_V15 = 48,
    REGISTER_ARM64_V16 = 49,
    REGISTER_ARM64_V17 = 50,
    REGISTER_ARM64_V18 = 51,
    REGISTER_ARM64_V19 = 52,
    REGISTER_ARM64_V20 = 53,
    REGISTER_ARM64_V21 = 54,
    REGISTER_ARM64_V22 = 55,
    REGISTER_ARM64_V23 = 56,
    REGISTER_ARM64_V24 = 57,
    REGISTER_ARM64_V25 = 58,
    REGISTER_ARM64_V26 = 59,
    REGISTER_ARM64_V27 = 60,
    REGISTER_ARM64_V28 = 61,
    REGISTER_ARM64_V29 = 62,
    REGISTER_ARM64_V30 = 63,
    REGISTER_ARM64_V31 = 64,
    REGISTER_LOONGARCH64_PC = REGISTER_INSTRUCTION_POINTER,
    REGISTER_LOONGARCH64_SP = REGISTER_STACK_POINTER,
    REGISTER_LOONGARCH64_FP = REGISTER_FRAME_POINTER,
    REGISTER_LOONGARCH64_RA = REGISTER_X86_EAX,
    REGISTER_LOONGARCH64_TP = REGISTER_X86_ECX,
    REGISTER_LOONGARCH64_A0 = REGISTER_X86_EDX,
    REGISTER_LOONGARCH64_A1 = REGISTER_X86_EBX,
    REGISTER_LOONGARCH64_A2 = REGISTER_X86_ESI,
    REGISTER_LOONGARCH64_A3 = REGISTER_X86_EDI,
    REGISTER_LOONGARCH64_A4 = REGISTER_X86_FPSTACK_0,
    REGISTER_LOONGARCH64_A5 = REGISTER_X86_FPSTACK_1,
    REGISTER_LOONGARCH64_A6 = REGISTER_X86_FPSTACK_2,
    REGISTER_LOONGARCH64_A7 = REGISTER_X86_FPSTACK_3,
    REGISTER_LOONGARCH64_T0 = REGISTER_X86_FPSTACK_4,
    REGISTER_LOONGARCH64_T1 = REGISTER_X86_FPSTACK_5,
    REGISTER_LOONGARCH64_T2 = REGISTER_X86_FPSTACK_6,
    REGISTER_LOONGARCH64_T3 = REGISTER_X86_FPSTACK_7,
    REGISTER_LOONGARCH64_T4 = REGISTER_AMD64_XMM0,
    REGISTER_LOONGARCH64_T5 = REGISTER_AMD64_XMM1,
    REGISTER_LOONGARCH64_T6 = REGISTER_AMD64_XMM2,
    REGISTER_LOONGARCH64_T7 = REGISTER_AMD64_XMM3,
    REGISTER_LOONGARCH64_T8 = REGISTER_AMD64_XMM4,
    REGISTER_LOONGARCH64_X0 = REGISTER_AMD64_XMM5,
    REGISTER_LOONGARCH64_S0 = REGISTER_AMD64_XMM6,
    REGISTER_LOONGARCH64_S1 = REGISTER_AMD64_XMM7,
    REGISTER_LOONGARCH64_S2 = REGISTER_AMD64_XMM8,
    REGISTER_LOONGARCH64_S3 = REGISTER_AMD64_XMM9,
    REGISTER_LOONGARCH64_S4 = REGISTER_AMD64_XMM10,
    REGISTER_LOONGARCH64_S5 = REGISTER_AMD64_XMM11,
    REGISTER_LOONGARCH64_S6 = REGISTER_AMD64_XMM12,
    REGISTER_LOONGARCH64_S7 = REGISTER_AMD64_XMM13,
    REGISTER_LOONGARCH64_S8 = REGISTER_AMD64_XMM14,
    REGISTER_LOONGARCH64_F0 = REGISTER_AMD64_XMM15,
    REGISTER_LOONGARCH64_F1 = REGISTER_ARM_D17,
    REGISTER_LOONGARCH64_F2 = REGISTER_ARM_D18,
    REGISTER_LOONGARCH64_F3 = REGISTER_ARM_D19,
    REGISTER_LOONGARCH64_F4 = REGISTER_ARM_D20,
    REGISTER_LOONGARCH64_F5 = REGISTER_ARM_D21,
    REGISTER_LOONGARCH64_F6 = REGISTER_ARM_D22,
    REGISTER_LOONGARCH64_F7 = REGISTER_ARM_D23,
    REGISTER_LOONGARCH64_F8 = REGISTER_ARM_D24,
    REGISTER_LOONGARCH64_F9 = REGISTER_ARM_D25,
    REGISTER_LOONGARCH64_F10 = REGISTER_ARM_D26,
    REGISTER_LOONGARCH64_F11 = REGISTER_ARM_D27,
    REGISTER_LOONGARCH64_F12 = REGISTER_ARM_D28,
    REGISTER_LOONGARCH64_F13 = REGISTER_ARM_D29,
    REGISTER_LOONGARCH64_F14 = REGISTER_ARM_D30,
    REGISTER_LOONGARCH64_F15 = REGISTER_ARM_D31,
    REGISTER_LOONGARCH64_F16 = REGISTER_ARM64_V15,
    REGISTER_LOONGARCH64_F17 = REGISTER_ARM64_V16,
    REGISTER_LOONGARCH64_F18 = REGISTER_ARM64_V17,
    REGISTER_LOONGARCH64_F19 = REGISTER_ARM64_V18,
    REGISTER_LOONGARCH64_F20 = REGISTER_ARM64_V19,
    REGISTER_LOONGARCH64_F21 = REGISTER_ARM64_V20,
    REGISTER_LOONGARCH64_F22 = REGISTER_ARM64_V21,
    REGISTER_LOONGARCH64_F23 = REGISTER_ARM64_V22,
    REGISTER_LOONGARCH64_F24 = REGISTER_ARM64_V23,
    REGISTER_LOONGARCH64_F25 = REGISTER_ARM64_V24,
    REGISTER_LOONGARCH64_F26 = REGISTER_ARM64_V25,
    REGISTER_LOONGARCH64_F27 = REGISTER_ARM64_V26,
    REGISTER_LOONGARCH64_F28 = REGISTER_ARM64_V27,
    REGISTER_LOONGARCH64_F29 = REGISTER_ARM64_V28,
    REGISTER_LOONGARCH64_F30 = REGISTER_ARM64_V29,
    REGISTER_LOONGARCH64_F31 = REGISTER_ARM64_V30,
    REGISTER_RISCV64_PC = REGISTER_INSTRUCTION_POINTER,
    REGISTER_RISCV64_SP = REGISTER_STACK_POINTER,
    REGISTER_RISCV64_FP = REGISTER_FRAME_POINTER,
    REGISTER_RISCV64_RA = REGISTER_X86_EAX,
    REGISTER_RISCV64_GP = REGISTER_X86_ECX,
    REGISTER_RISCV64_TP = REGISTER_X86_EDX,
    REGISTER_RISCV64_T0 = REGISTER_X86_EBX,
    REGISTER_RISCV64_T1 = REGISTER_X86_ESI,
    REGISTER_RISCV64_T2 = REGISTER_X86_EDI,
    REGISTER_RISCV64_S1 = REGISTER_X86_FPSTACK_0,
    REGISTER_RISCV64_A0 = REGISTER_X86_FPSTACK_1,
    REGISTER_RISCV64_A1 = REGISTER_X86_FPSTACK_2,
    REGISTER_RISCV64_A2 = REGISTER_X86_FPSTACK_3,
    REGISTER_RISCV64_A3 = REGISTER_X86_FPSTACK_4,
    REGISTER_RISCV64_A4 = REGISTER_X86_FPSTACK_5,
    REGISTER_RISCV64_A5 = REGISTER_X86_FPSTACK_6,
    REGISTER_RISCV64_A6 = REGISTER_X86_FPSTACK_7,
    REGISTER_RISCV64_A7 = REGISTER_AMD64_XMM0,
    REGISTER_RISCV64_S2 = REGISTER_AMD64_XMM1,
    REGISTER_RISCV64_S3 = REGISTER_AMD64_XMM2,
    REGISTER_RISCV64_S4 = REGISTER_AMD64_XMM3,
    REGISTER_RISCV64_S5 = REGISTER_AMD64_XMM4,
    REGISTER_RISCV64_S6 = REGISTER_AMD64_XMM5,
    REGISTER_RISCV64_S7 = REGISTER_AMD64_XMM6,
    REGISTER_RISCV64_S8 = REGISTER_AMD64_XMM7,
    REGISTER_RISCV64_S9 = REGISTER_AMD64_XMM8,
    REGISTER_RISCV64_S10 = REGISTER_AMD64_XMM9,
    REGISTER_RISCV64_S11 = REGISTER_AMD64_XMM10,
    REGISTER_RISCV64_T3 = REGISTER_AMD64_XMM11,
    REGISTER_RISCV64_T4 = REGISTER_AMD64_XMM12,
    REGISTER_RISCV64_T5 = REGISTER_AMD64_XMM13,
    REGISTER_RISCV64_T6 = REGISTER_AMD64_XMM14,
    REGISTER_RISCV64_F0 = REGISTER_AMD64_XMM15,
    REGISTER_RISCV64_F1 = REGISTER_ARM_D17,
    REGISTER_RISCV64_F2 = REGISTER_ARM_D18,
    REGISTER_RISCV64_F3 = REGISTER_ARM_D19,
    REGISTER_RISCV64_F4 = REGISTER_ARM_D20,
    REGISTER_RISCV64_F5 = REGISTER_ARM_D21,
    REGISTER_RISCV64_F6 = REGISTER_ARM_D22,
    REGISTER_RISCV64_F7 = REGISTER_ARM_D23,
    REGISTER_RISCV64_F8 = REGISTER_ARM_D24,
    REGISTER_RISCV64_F9 = REGISTER_ARM_D25,
    REGISTER_RISCV64_F10 = REGISTER_ARM_D26,
    REGISTER_RISCV64_F11 = REGISTER_ARM_D27,
    REGISTER_RISCV64_F12 = REGISTER_ARM_D28,
    REGISTER_RISCV64_F13 = REGISTER_ARM_D29,
    REGISTER_RISCV64_F14 = REGISTER_ARM_D30,
    REGISTER_RISCV64_F15 = REGISTER_ARM_D31,
    REGISTER_RISCV64_F16 = REGISTER_ARM64_V15,
    REGISTER_RISCV64_F17 = REGISTER_ARM64_V16,
    REGISTER_RISCV64_F18 = REGISTER_ARM64_V17,
    REGISTER_RISCV64_F19 = REGISTER_ARM64_V18,
    REGISTER_RISCV64_F20 = REGISTER_ARM64_V19,
    REGISTER_RISCV64_F21 = REGISTER_ARM64_V20,
    REGISTER_RISCV64_F22 = REGISTER_ARM64_V21,
    REGISTER_RISCV64_F23 = REGISTER_ARM64_V22,
    REGISTER_RISCV64_F24 = REGISTER_ARM64_V23,
    REGISTER_RISCV64_F25 = REGISTER_ARM64_V24,
    REGISTER_RISCV64_F26 = REGISTER_ARM64_V25,
    REGISTER_RISCV64_F27 = REGISTER_ARM64_V26,
    REGISTER_RISCV64_F28 = REGISTER_ARM64_V27,
    REGISTER_RISCV64_F29 = REGISTER_ARM64_V28,
    REGISTER_RISCV64_F30 = REGISTER_ARM64_V29,
    REGISTER_RISCV64_F31 = REGISTER_ARM64_V30
}
