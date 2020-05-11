using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.Utils;
using System.Linq;
using System.Diagnostics;
using System.Threading;

namespace MonoMod.RuntimeDetour.Platforms {
#if !MONOMOD_INTERNAL
    public
#endif
    class DetourRuntimeNETPlatform : DetourRuntimeILPlatform {
        private static readonly object[] _NoArgs = new object[0];

#pragma warning disable IDE0044 // Add readonly modifier
#pragma warning disable CS0169 // The field is never used
        private static bool Debugging;
#pragma warning restore IDE0044 // Add readonly modifier
#pragma warning restore CS0169 // The field is never used


        private static readonly FieldInfo _DynamicMethod_m_method =
            typeof(DynamicMethod).GetField("m_method", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo _DynamicMethod_GetMethodDescriptor =
            typeof(DynamicMethod).GetMethod("GetMethodDescriptor", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _RuntimeMethodHandle_m_value =
            typeof(RuntimeMethodHandle).GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo _RuntimeHelpers__CompileMethod =
            typeof(RuntimeHelpers).GetMethod("_CompileMethod", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly bool _RuntimeHelpers__CompileMethod_TakesIntPtr =
            _RuntimeHelpers__CompileMethod != null &&
            _RuntimeHelpers__CompileMethod.GetParameters()[0].ParameterType.FullName == "System.IntPtr";
        private static readonly bool _RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo =
            _RuntimeHelpers__CompileMethod != null &&
            _RuntimeHelpers__CompileMethod.GetParameters()[0].ParameterType.FullName == "System.IRuntimeMethodInfo";

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            // Compile the method handle before getting our hands on the final method handle.
            if (method is DynamicMethod dm) {
                if (_RuntimeHelpers__CompileMethod_TakesIntPtr) {
                    // mscorlib 2.0.0.0
                    _RuntimeHelpers__CompileMethod.Invoke(null, new object[] { ((RuntimeMethodHandle) _DynamicMethod_GetMethodDescriptor.Invoke(dm, _NoArgs)).Value });

                } else if (_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo) {
                    // mscorlib 4.0.0.0
                    _RuntimeHelpers__CompileMethod.Invoke(null, new object[] { _RuntimeMethodHandle_m_value.GetValue(((RuntimeMethodHandle) _DynamicMethod_GetMethodDescriptor.Invoke(dm, _NoArgs))) });

                } else {
                    // This should work just fine.
                    // It abuses the fact that CreateDelegate first compiles the DynamicMethod, before creating the delegate and failing.
                    // Only side effect: It introduces a possible deadlock in f.e. tModLoader, which adds a FirstChanceException handler.
                    try {
                        dm.CreateDelegate(typeof(MulticastDelegate));
                    } catch {
                    }
                }

                if (_DynamicMethod_m_method != null)
                    return (RuntimeMethodHandle) _DynamicMethod_m_method.GetValue(method);
                if (_DynamicMethod_GetMethodDescriptor != null)
                    return (RuntimeMethodHandle) _DynamicMethod_GetMethodDescriptor.Invoke(method, _NoArgs);
            }

            return method.MethodHandle;
        }

        protected override void DisableInlining(MethodBase method, RuntimeMethodHandle handle) {
            // This is not needed for .NET Framework - see DisableInliningTest.
        }

        protected override unsafe IntPtr GetFunctionPointer(MethodBase method, RuntimeMethodHandle handle) {
            if (method.IsVirtual && (method.DeclaringType?.IsValueType ?? false)) {
                /* .NET has got TWO MethodDescs and thus TWO ENTRY POINTS for virtual struct methods (f.e. override ToString).
                 * More info: https://mattwarren.org/2017/08/02/A-look-at-the-internals-of-boxing-in-the-CLR/#unboxing-stub-creation
                 *
                 * Observations made so far:
                 * - GetFunctionPointer ALWAYS returns a pointer to the unboxing stub handle.
                 * - On x86, the "real" entry point is often found 8 bytes after the unboxing stub entry point.
                 * - Methods WILL be called INDIRECTLY using the pointer found in the "real" MethodDesc.
                 * - The "real" MethodDesc will be updated, which isn't an issue except that we can't patch the stub in time.
                 * - The "real" stub will stay untouched.
                 * - LDFTN RETURNS A POINTER TO THE "REAL" ENTRY POINT.
                 */
                return method.GetLdftnPointer();
            }


            IntPtr ptr = base.GetFunctionPointer(method, handle);

            // When in doubt, enable this debugging helper block, add Debugger.Break() where needed and attach WinDbg quickly.
#if false
            if (!Debugging) {
                Debugging = true;
                // WinDbg doesn't trigger Debugger.IsAttached
                Thread.Sleep(6000);
            }

            Console.WriteLine($"mets: {method.GetID()}");
            Console.WriteLine($"meth: 0x{(long) handle.Value:X16}");
            Console.WriteLine($"getf: 0x{(long) handle.GetFunctionPointer():X16}");
#endif

            /* Many (if not all) NGEN'd methods (f.e. those from mscorlib.ni.dll) are handled in a special manner.
             * When debugged using WinDbg, !dumpmd for the handle gives a different CodeAddr than ldftn or GetFunctionPointer.
             * When using !ip2md on the ldftn / GetFunctionPointer result, no MD is found.
             * There is only one MD, we're already accessing it, but we still can't access the "real" entry point.
             * Luckily a jmp to it exists within the stub returned by GetFunctionPointer.
             * Sadly detecting when to read it is... ugly, to say the least.
             * This pretty much acts as the reverse of DetourNative*Platform.Apply
             * Maybe this should be Native*Platform-ified in the future, but for now...
             */

            // IMPORTANT: IN SOME CIRCUMSTANCES, THIS CAN FIND ThePreStub AS THE ENTRY POINT.

            long lptr = (long) ptr;
            if (PlatformHelper.Is(Platform.ARM)) {
                // TODO: Debug detouring NGEN'd methods on ARM.

            } else if (IntPtr.Size == 4) {
                // x86
                if (*(byte*) (lptr + 0x00) == 0xb8 && // mov ... (mscorlib_ni!???)
                    *(byte*) (lptr + 0x05) == 0x90 && // nop
                    *(byte*) (lptr + 0x06) == 0xe8 && // call ... (clr!PrecodeRemotingThunk)
                    *(byte*) (lptr + 0x0b) == 0xe9 // jmp {DELTA}
                ) {
                    // delta = to - (from + 1 + sizeof(int))
                    // to = delta + (from + 1 + sizeof(int))
                    long from = lptr + 0x0b;
                    long delta = *(int*) (from + 1);
                    long to = delta + (from + 1 + sizeof(int));
                    return NotThePreStub(ptr, (IntPtr) to);
                }

            } else {
                // x64 .NET Framework
                if (*(uint*) (lptr + 0x00) == 0x74___c9_85_48 && // in reverse order: test rcx, rcx | je ...
                    *(uint*) (lptr + 0x05) == 0x49___01_8b_48 && // in reverse order: rax, qword ptr [rcx] | mov ...
                    *(uint*) (lptr + 0x12) == 0x74___c2_3b_49 && // in reverse order: cmp rax, r10 | je ...
                    *(ushort*) (lptr + 0x17) == 0xb8_48 // in reverse order: mov {TARGET}
                ) {
                    return NotThePreStub(ptr, (IntPtr) (*(ulong*) (lptr + 0x19)));
                }

                // x64 .NET Core
                if (*(byte*) (lptr + 0x00) == 0xe9 && // jmp {DELTA}
                    *(byte*) (lptr + 0x05) == 0x5f // pop rdi
                ) {
                    // delta = to - (from + 1 + sizeof(int))
                    // to = delta + (from + 1 + sizeof(int))
                    long from = lptr;
                    long delta = *(int*) (from + 1);
                    long to = delta + (from + 1 + sizeof(int));
                    return NotThePreStub(ptr, (IntPtr) to);
                }
            }


            return ptr;
        }

        private static IntPtr ThePreStub = IntPtr.Zero;
        private IntPtr NotThePreStub(IntPtr ptrGot, IntPtr ptrParsed) {
            if (ThePreStub == IntPtr.Zero) {
                ThePreStub = (IntPtr) (-2);

                // FIXME: Find a better less likely called NGEN'd candidate that points to ThePreStub.
                // This was "found" by tModLoader.
                // Can be missing in .NET 5.0 outside of Windows for some reason.
                MethodInfo mi = typeof(System.Net.HttpWebRequest).Assembly
                    .GetType("System.Net.Connection")
                    ?.GetMethod("SubmitRequest", BindingFlags.NonPublic | BindingFlags.Instance);

                if (mi != null) {
                    ThePreStub = GetNativeStart(mi);
                } else if (PlatformHelper.Is(Platform.Windows)) {
                    // FIXME: This should be -1 (always return ptrGot) on all plats, but SubmitRequest is Windows-only?
                    ThePreStub = (IntPtr) (-1);
                }
            }

            return (ptrParsed == ThePreStub || ThePreStub == (IntPtr) (-1)) ? ptrGot : ptrParsed;
        }
    }
}
