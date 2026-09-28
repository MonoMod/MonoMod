using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Mono.Cecil.Cil;

namespace MonoMod.RuntimeDetour.Generics
{
    // Determines empirically which integer argument register Mono's JIT assigns to the dispatcher's
    // appended generic-context parameter; TODO: is there a way to know this without having to do this hacky check?
    //
    // Builds a probe with the dispatcher's signature shape that returns its context parameter, then calls it through
    // a native stub that writes a distinct magic into every integer argument register. The returned magic identifies
    // the register; if none comes back, the context is on the stack and the capture stub can't work.
    internal static class MonoContextRegisterCalibrator
    {
        private const int MagicBase = 0x4D430000;

        private static readonly ConcurrentDictionary<string, int> cache = [];

        // Returns the exact integer-argument-register index of the context parameter, -1 if the context is not
        // passed in an integer register (stack), or null when calibration is unavailable for this signature/platform
        public static int? TryFindContextArgRegister(Type[] paramTypes, int genCtxPos)
        {
            if (PlatformDetection.Architecture is not (ArchitectureKind.x86_64 or ArchitectureKind.Arm64))
            {
                return null;
            }

            var sanitized = new Type[paramTypes.Length];
            for (var i = 0; i < paramTypes.Length; i++)
            {
                var t = paramTypes[i];
                if (!t.IsValueType || t.IsByRef)
                {
                    sanitized[i] = typeof(IntPtr);
                }
                else if (StructContainsReferences(t))
                {
                    return null;
                }
                else
                {
                    sanitized[i] = t;
                }
            }

            var key = CacheKey(sanitized, genCtxPos);
            if (cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            try
            {
                var index = Calibrate(sanitized, genCtxPos);
                cache[key] = index;
                return index;
            }
            catch (Exception ex)
            {
                MMDbgLog.Trace($"Context-register calibration failed ({ex.GetType().Name}: {ex.Message}); using heuristic.");
                return null;
            }
        }

        private static int Calibrate(Type[] paramTypes, int genCtxPos)
        {
            using var probeDmd = new DynamicMethodDefinition("GenericContextRegisterProbe", typeof(IntPtr), paramTypes);
            var probeIl = probeDmd.GetILProcessor();
            probeIl.Emit(OpCodes.Ldarg, probeDmd.Definition.Parameters[genCtxPos]);
            probeIl.Emit(OpCodes.Ret);
            var probe = DMDCecilGenerator.Generate(probeDmd);
            RuntimeHelpers.PrepareMethod(probe.MethodHandle);
            var probeEntry = probe.MethodHandle.GetFunctionPointer();

            var regCount = IntegerArgRegisterCount();
            var stubCode = PlatformDetection.Architecture == ArchitectureKind.x86_64
                ? BuildX86_64Stub(probeEntry, regCount)
                : BuildArm64Stub(probeEntry, regCount);
            using var stub = GenericContextCaptureStub.AllocateExecutable(stubCode);

            using var callerDmd = new DynamicMethodDefinition("GenericContextRegisterCalib", typeof(IntPtr), Type.EmptyTypes);
            var module = callerDmd.Module;
            var il = callerDmd.GetILProcessor();
            callerDmd.Definition.Body.InitLocals = true;

            var callSite = new Mono.Cecil.CallSite(module.ImportReference(typeof(IntPtr)));
            foreach (var t in paramTypes)
            {
                var local = new VariableDefinition(module.ImportReference(t));
                callerDmd.Definition.Body.Variables.Add(local);
                il.Emit(OpCodes.Ldloc, local);
                callSite.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.ImportReference(t)));
            }
            if (IntPtr.Size == 8)
            {
                il.Emit(OpCodes.Ldc_I8, (long)stub.BaseAddress);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4, (int)stub.BaseAddress);
            }
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Calli, callSite);
            il.Emit(OpCodes.Ret);

            var caller = DMDCecilGenerator.Generate(callerDmd);
            var result = (IntPtr)caller.Invoke(null, null)!;
            GC.KeepAlive(probe);

            var value = result.ToInt64();
            if ((value & ~0xFFL) == MagicBase && (value & 0xFF) < regCount)
            {
                return (int)(value & 0xFF);
            }
            return -1;
        }

        private static int IntegerArgRegisterCount()
        {
            if (PlatformDetection.Architecture == ArchitectureKind.Arm64)
            {
                return 8;
            }
            return PlatformDetection.OS is OSKind.Windows or OSKind.Wine ? 4 : 6;
        }

        // mov <argreg_i>, MagicBase|i (for every integer arg register) ; movabs rax, probe ; jmp rax
        private static byte[] BuildX86_64Stub(IntPtr probe, int regCount)
        {
            var windows = PlatformDetection.OS is OSKind.Windows or OSKind.Wine;
            // REX.W C7 /0 (mov r/m64, imm32) prefixes per register, in arg order.
            byte[][] movImm32 = windows
                // Windows x64: rcx, rdx, r8, r9
                ? [
                    [0x48, 0xC7, 0xC1],
                    [0x48, 0xC7, 0xC2],
                    [0x49, 0xC7, 0xC0],
                    [0x49, 0xC7, 0xC1],
                ]
                // SysV x64: rdi, rsi, rdx, rcx, r8, r9
                : [
                    [0x48, 0xC7, 0xC7],
                    [0x48, 0xC7, 0xC6],
                    [0x48, 0xC7, 0xC2],
                    [0x48, 0xC7, 0xC1],
                    [0x49, 0xC7, 0xC0],
                    [0x49, 0xC7, 0xC1],
                ];

            var code = new byte[(regCount * 7) + 12];
            var pos = 0;
            for (var i = 0; i < regCount; i++)
            {
                movImm32[i].CopyTo(code, pos);
                pos += 3;
                BitConverter.GetBytes(MagicBase | i).CopyTo(code, pos);
                pos += 4;
            }
            // movabs rax, probe
            code[pos++] = 0x48;
            code[pos++] = 0xB8;
            BitConverter.GetBytes((long)probe).CopyTo(code, pos);
            pos += 8;
            // jmp rax
            code[pos++] = 0xFF;
            code[pos] = 0xE0;
            return code;
        }

        // movz x<i>, #0x4D43, lsl #16 ; movk x<i>, #i (for x0..x7) ; ldr x16, #8 ; br x16 ; .quad probe
        private static byte[] BuildArm64Stub(IntPtr probe, int regCount)
        {
            const uint MagicHigh16 = MagicBase >> 16;

            var code = new byte[(regCount * 8) + 4 + 4 + 8];
            var pos = 0;
            for (var i = 0; i < regCount; i++)
            {
                // movz x<i>, #imm16, lsl #16 == 0xD2A00000 | imm16 << 5 | Rd
                WriteUInt32(code, pos, 0xD2A00000u | (MagicHigh16 << 5) | (uint)i);
                pos += 4;
                // movk x<i>, #imm16 == 0xF2800000 | imm16 << 5 | Rd
                WriteUInt32(code, pos, 0xF2800000u | ((uint)i << 5) | (uint)i);
                pos += 4;
            }
            // ldr x16, #8
            WriteUInt32(code, pos, 0x58000050u);
            pos += 4;
            // br x16
            WriteUInt32(code, pos, 0xD61F0200u);
            pos += 4;
            BitConverter.GetBytes((long)probe).CopyTo(code, pos);
            return code;
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value) => GenericContextCaptureStub.WriteUInt32LittleEndian(buffer, offset, value);

        private static bool StructContainsReferences(Type t)
        {
            if (t.IsPrimitive || t.IsEnum || t.IsPointer)
            {
                return false;
            }
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var ft = f.FieldType;
                if (!ft.IsValueType || StructContainsReferences(ft))
                {
                    return true;
                }
            }
            return false;
        }

        private static string CacheKey(Type[] paramTypes, int genCtxPos)
        {
            var sb = new StringBuilder();
            sb.Append(genCtxPos).Append(':');
            foreach (var t in paramTypes)
            {
                sb.Append(t.FullName ?? t.Name).Append(';');
            }
            return sb.ToString();
        }
    }
}
