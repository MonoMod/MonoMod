using MonoMod.Utils;
using System;

namespace MonoMod.Core.Platforms.Architectures.AltEntryFactories
{
    internal sealed class X86AltEntryFactory : IAltEntryFactory
    {
        // above these sizes, scratch buffers go on the heap instead of the stack
        private const int MaxStackInstructions = 64;
        private const int MaxStackCodeSize = 1024;

        private readonly ISystem system;
        private readonly IMemoryAllocator alloc;
        private readonly bool is64;

        public X86AltEntryFactory(ISystem system, bool is64)
        {
            this.system = system;
            this.is64 = is64;

            alloc = system.MemoryAllocator;
        }

        public unsafe IntPtr CreateAlternateEntrypoint(IntPtr entrypoint, int minLength, out IDisposable? handle)
        {
            // IntPtr -> ulong must not sign-extend 32-bit addresses
            ulong srcIp = (nuint)(nint)entrypoint;

            // the decoder only reads the bytes of the instructions it decodes, so this span never touches memory past the last one
            var code = new ReadOnlySpan<byte>((void*)entrypoint, minLength + X86InstructionDecoder.MaxInstructionLength);

            // every instruction is at least one byte, so there are at most minLength of them
            Span<X86Instruction> insns = minLength <= MaxStackInstructions
                ? stackalloc X86Instruction[minLength]
                : new X86Instruction[minLength];
            insns = insns.Slice(0, X86CodeRelocator.DecodeBlock(code, srcIp, is64, minLength, insns, out var blockLength));

            // RIP-relative operands cannot be rewritten to an absolute form, so the new code must be within reach of the original
            var needsNearAlloc = false;
            for (var i = 0; i < insns.Length; i++)
            {
                needsNearAlloc |= insns[i].Kind is X86RelocKind.RipRelative or X86RelocKind.RelOperand;
            }
            needsNearAlloc &= is64;

            // Allocate for the worst case, where every branch needs an absolute form; any unused tail is filled with int3.
            var maxSize = X86CodeRelocator.GetMaxEncodedSize(insns, is64);

            IAllocatedMemory? allocated;
            if (needsNearAlloc)
            {
                Helpers.Assert(alloc.TryAllocateInRange(
                    new(entrypoint, (nint)entrypoint + int.MinValue, (nint)entrypoint + int.MaxValue,
                    new(maxSize) { Executable = true }), out allocated));
            }
            else
            {
                Helpers.Assert(alloc.TryAllocate(new(maxSize) { Executable = true }, out allocated));
            }

            try
            {
                Span<byte> buffer = maxSize <= MaxStackCodeSize
                    ? stackalloc byte[maxSize]
                    : new byte[maxSize];

                var size = X86CodeRelocator.Encode(code, srcIp, insns, blockLength, is64, (nuint)(nint)allocated.BaseAddress, buffer);
                buffer.Slice(size).Fill(0xCC);

                system.PatchData(PatchTargetKind.Executable, allocated.BaseAddress, buffer, default);
            }
            catch (Exception e)
            {
                allocated.Dispose();
                MMDbgLog.Error($"Could not create alternate entrypoint for 0x{srcIp:x16}: {e}");
                throw;
            }

            handle = allocated;
            return allocated.BaseAddress;
        }
    }
}
