using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;

namespace MonoMod.RuntimeDetour.Generics
{
    // Mono only. The managed dispatcher is a normal method: it can only read its declared arguments, not a physical
    // register. But Mono passes the shared-generic context (RGCTX/MRGCTX) in a dedicated non-argument register
    // (r10 on x86_64, x15 on Arm64) rather than as an argument, so the dispatcher can't see it.
    // This stub copies that register into the argument register the dispatcher expects the context in, then jumps to
    // the dispatcher.
    internal sealed class GenericContextCaptureStub : IDisposable
    {
        private readonly IAllocatedMemory _alloc;
        public IntPtr Address { get; private set; }

        public GenericContextCaptureStub(IntPtr target, int integerArgRegisterIndex)
        {
            // Moves Mono's RGCTX/MRGCTX register into an argument register and jumps to the dispatcher.
            var code = PlatformDetection.Architecture switch
            {
                ArchitectureKind.x86_64 => BuildX86_64(target, integerArgRegisterIndex),
                ArchitectureKind.Arm64 => BuildArm64(target, integerArgRegisterIndex),
                _ => throw new PlatformNotSupportedException(
                    $"The Mono generic-context capture stub is only implemented for x86_64 and Arm64 (got {PlatformDetection.Architecture})."),
            };

            _alloc = AllocateExecutable(code);
            Address = _alloc.BaseAddress;
        }

        internal static IAllocatedMemory AllocateExecutable(byte[] code)
        {
            var system = PlatformTriple.Current.System;
            if (!system.MemoryAllocator.TryAllocate(new AllocationRequest(code.Length) { Executable = true }, out var alloc))
            {
                throw new InvalidOperationException("Could not allocate executable memory for a generic dispatch stub.");
            }
            system.PatchData(PatchTargetKind.Executable, alloc.BaseAddress, code, default);
            return alloc;
        }

        // x86_64: Mono passes the RGCTX/MRGCTX in r10.
        private static byte[] BuildX86_64(IntPtr target, int integerArgRegisterIndex)
        {
            var windows = PlatformDetection.OS is OSKind.Windows or OSKind.Wine;
            byte[][] movFromR10ByIndex = windows
                // Windows x64: rcx, rdx, r8, r9
                ? [
                    [0x4C, 0x89, 0xD1], // mov rcx, r10
                    [0x4C, 0x89, 0xD2], // mov rdx, r10
                    [0x4D, 0x89, 0xD0], // mov r8, r10
                    [0x4D, 0x89, 0xD1], // mov r9, r10
                ]
                // SysV x64: rdi, rsi, rdx, rcx, r8, r9
                : [
                    [0x4C, 0x89, 0xD7], // mov rdi, r10
                    [0x4C, 0x89, 0xD6], // mov rsi, r10
                    [0x4C, 0x89, 0xD2], // mov rdx, r10
                    [0x4C, 0x89, 0xD1], // mov rcx, r10
                    [0x4D, 0x89, 0xD0], // mov r8, r10
                    [0x4D, 0x89, 0xD1], // mov r9, r10
                ];

            if (integerArgRegisterIndex < 0 || integerArgRegisterIndex >= movFromR10ByIndex.Length)
            {
                throw new PlatformNotSupportedException($"The generic context cannot be captured into integer-argument register index {integerArgRegisterIndex} (it would be passed on the stack).");
            }

            // mov <argreg>, r10, from the index onward: the context is the dispatcher's last integer parameter,
            // so registers past its index carry no arguments and overwriting them is harmless.
            var movBytes = (movFromR10ByIndex.Length - integerArgRegisterIndex) * 3;
            var code = new byte[movBytes + 12];
            var i = 0;
            for (var idx = integerArgRegisterIndex; idx < movFromR10ByIndex.Length; idx++)
            {
                movFromR10ByIndex[idx].CopyTo(code, i);
                i += 3;
            }
            // mov rax, imm64
            code[i++] = 0x48;
            code[i++] = 0xB8;
            BitConverter.GetBytes((long)target).CopyTo(code, i);
            i += 8;
            // jmp rax
            code[i++] = 0xFF;
            code[i] = 0xE0;
            return code;
        }

        // Arm64: Mono's RGCTX/MRGCTX register is x15
        // Integer args are x0..x7 and the return buffer is x8
        private static byte[] BuildArm64(IntPtr target, int integerArgRegisterIndex)
        {
            const int paramRegs = 8;
            if (integerArgRegisterIndex < 0 || integerArgRegisterIndex >= paramRegs)
            {
                throw new PlatformNotSupportedException($"The generic context cannot be captured into integer-argument register index {integerArgRegisterIndex} (it would be passed on the stack).");
            }

            // Copy x15 into the arg register(s) from the index onward (the context is the dispatcher's last integer
            // parameter, so higher registers carry no arguments), then branch to `target`.
            var movCount = paramRegs - integerArgRegisterIndex;
            var code = new byte[(movCount * 4) + 4 + 4 + 8];
            var pos = 0;

            // orr x<d>, xzr, x15
            for (var d = integerArgRegisterIndex; d < paramRegs; d++)
            {
                WriteUInt32LittleEndian(code, pos, 0xAA0F03E0u | (uint)d);
                pos += 4;
            }

            // ldr x16, #8
            WriteUInt32LittleEndian(code, pos, 0x58000050u);
            pos += 4;
            // br x16
            WriteUInt32LittleEndian(code, pos, 0xD61F0200u);
            pos += 4;
            // .quad target
            BitConverter.GetBytes((long)target).CopyTo(code, pos);

            return code;
        }

        internal static void WriteUInt32LittleEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset + 0] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        public void Dispose()
        {
            if (Address == IntPtr.Zero)
            {
                return;
            }
            Address = IntPtr.Zero;
            _alloc.Dispose();
        }
    }
}
