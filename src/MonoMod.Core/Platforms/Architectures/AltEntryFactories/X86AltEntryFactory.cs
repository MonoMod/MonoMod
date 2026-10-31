using MonoMod.Utils;
using System;

namespace MonoMod.Core.Platforms.Architectures.AltEntryFactories
{
    /// <summary>Creates relocated x86/x86-64 alternate entrypoints.</summary>
    public sealed class X86AltEntryFactory : IAltEntryFactory
    {
        // above these sizes, scratch buffers go on the heap instead of the stack
        private const int MaxStackInstructions = 16;
        private const int MaxStackCodeSize = 512;

        private readonly ISystem system;
        private readonly IMemoryAllocator alloc;
        private readonly bool is64;

        /// <summary>Creates a factory for the specified system and instruction mode.</summary>
        /// <param name="system">The system used to allocate and patch executable memory.</param>
        /// <param name="is64"><see langword="true"/> for x86-64 code; otherwise, x86 code.</param>
        public X86AltEntryFactory(ISystem system, bool is64)
        {
            Helpers.ThrowIfArgumentNull(system);
            this.system = system;
            this.is64 = is64;

            alloc = system.MemoryAllocator;
        }

        /// <inheritdoc/>
        public unsafe IntPtr CreateAlternateEntrypoint(IntPtr entrypoint, int minLength, out IDisposable? handle)
        {
            // IntPtr -> ulong must not sign-extend 32-bit addresses
            ulong srcIp = (nuint)(nint)entrypoint;

            // the decoder only reads the bytes of the instructions it decodes, so this span never touches memory past the last one
            var code = new ReadOnlySpan<byte>((void*)entrypoint, minLength + X86InstructionDecoder.MaxInstructionLength);

            // every instruction is at least one byte, so there are at most minLength of them
            var insns = minLength <= MaxStackInstructions
                ? stackalloc X86Instruction[MaxStackInstructions]
                : new X86Instruction[minLength];
            insns = insns.Slice(0, X86CodeRelocator.DecodeBlock(code, srcIp, is64, minLength, insns, out var blockLength));

            // RIP-relative operands and XBEGIN have no absolute form; constrain allocation against every target.
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
                var low = (nint)entrypoint + int.MinValue;
                var high = (nint)entrypoint + int.MaxValue;
                for (var i = 0; i < insns.Length; i++)
                {
                    ref readonly var insn = ref insns[i];
                    if (insn.Kind is not (X86RelocKind.RipRelative or X86RelocKind.RelOperand))
                        continue;

                    var target = (nint)(nuint)insn.Target;
                    var operandLow = target - int.MaxValue - insn.Length;
                    var operandHigh = target - int.MinValue - maxSize;
                    if (operandLow > low)
                        low = operandLow;
                    if (operandHigh < high)
                        high = operandHigh;
                }

                if (low > high)
                    throw new InvalidOperationException("No allocation range can preserve the block's relative operands");

                Helpers.Assert(alloc.TryAllocateInRange(
                    new(entrypoint, low, high, new(maxSize) { Executable = true }), out allocated));
            }
            else
            {
                Helpers.Assert(alloc.TryAllocate(new(maxSize) { Executable = true }, out allocated));
            }

            try
            {
                var buffer = maxSize <= MaxStackCodeSize
                    ? stackalloc byte[MaxStackCodeSize]
                    : new byte[maxSize];
                buffer = buffer.Slice(maxSize);

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
