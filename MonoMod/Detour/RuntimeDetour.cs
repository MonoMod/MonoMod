using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MonoMod.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.InlineRT;

using Trampolines = System.Collections.Generic.HashSet<System.Reflection.Emit.DynamicMethod>;
using TargetTokenToTrampolinesStackMap = MonoMod.Helpers.LongDictionary<System.Collections.Generic.List<System.Collections.Generic.HashSet<System.Reflection.Emit.DynamicMethod>>>;

namespace MonoMod.Detour {
    public static class RuntimeDetour {

        public static bool IsX64 { get; } = IntPtr.Size == 8;
        public static int DetourSize { get; } = IsX64 ? 12 : 6;

        private readonly static FieldInfo f_DynamicMethod_m_method =
            // .NET
            typeof(DynamicMethod).GetField("m_method", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly static MethodInfo m_DynamicMethod_CreateDynMethod =
            // Mono
            typeof(DynamicMethod).GetMethod("CreateDynMethod", BindingFlags.NonPublic | BindingFlags.Instance);
        private readonly static DynamicMethodDelegate dmd_DynamicMethod_CreateDynMethod =
            m_DynamicMethod_CreateDynMethod?.CreateDelegate();

        private readonly static MethodInfo m_DynamicMethod_GetMethodDescriptor =
            // .NET
            typeof(DynamicMethod).GetMethod("GetMethodDescriptor", BindingFlags.NonPublic | BindingFlags.Instance);
        private readonly static DynamicMethodDelegate dmd_DynamicMethod_GetMethodDescriptor =
            m_DynamicMethod_GetMethodDescriptor?.CreateDelegate();

        private readonly static MethodInfo m_RuntimeHelpers__CompileMethod =
            // .NET
            typeof(RuntimeHelpers).GetMethod("_CompileMethod", BindingFlags.NonPublic | BindingFlags.Static);
        private readonly static DynamicMethodDelegate dmd_RuntimeHelpers__CompileMethod =
            m_RuntimeHelpers__CompileMethod?.CreateDelegate();
        private readonly static bool m_RuntimeHelpers__CompileMethod_TakesIntPtr =
            m_RuntimeHelpers__CompileMethod != null &&
            m_RuntimeHelpers__CompileMethod.GetParameters()[0].ParameterType == typeof(IntPtr);

        private readonly static MethodInfo m_RuntimeMethodHandle_GetMethodInfo =
            // .NET
            typeof(RuntimeMethodHandle).GetMethod("GetMethodInfo", BindingFlags.NonPublic | BindingFlags.Instance);
        private readonly static DynamicMethodDelegate dmd_RuntimeMethodHandle_GetMethodInfo =
            m_RuntimeMethodHandle_GetMethodInfo?.CreateDelegate();

        private readonly static MethodInfo m_Console_WriteLine_string =
            typeof(Console).GetMethod("WriteLine", new Type[] { typeof(string) });

        // TODO: Free the GCHandles!
        private readonly static unsafe FastDictionary<Delegate, GCHandle> _DelegateGCHandles = new FastDictionary<Delegate, GCHandle>();

        private readonly static unsafe LongDictionary<IntPtr> _Origs = new LongDictionary<IntPtr>();
        private readonly static unsafe LongDictionary<List<IntPtr>> _Reverts = new LongDictionary<List<IntPtr>>();
        private readonly static unsafe LongDictionary<IntPtr> _Current = new LongDictionary<IntPtr>();
        private readonly static unsafe FastDictionary<ulong, Type, Delegate> _OrigTrampolines = new FastDictionary<ulong, Type, Delegate>();

        private readonly static unsafe LongDictionary<MethodBase> _TokenToMethod = new LongDictionary<MethodBase>();
        private readonly static unsafe HashSet<MethodBase> _Tokenized = new HashSet<MethodBase>();

        private readonly static unsafe TargetTokenToTrampolinesStackMap _Trampolines = new TargetTokenToTrampolinesStackMap();

        [Flags]
        private enum Protection {
            PAGE_NOACCESS = 0x01,
            PAGE_READONLY = 0x02,
            PAGE_READWRITE = 0x04,
            PAGE_WRITECOPY = 0x08,
            PAGE_EXECUTE = 0x10,
            PAGE_EXECUTE_READ = 0x20,
            PAGE_EXECUTE_READWRITE = 0x40,
            PAGE_EXECUTE_WRITECOPY = 0x80,
            PAGE_GUARD = 0x100,
            PAGE_NOCACHE = 0x200,
            PAGE_WRITECOMBINE = 0x400
        }

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(IntPtr lpAddress, IntPtr dwSize, Protection flNewProtect, out Protection lpflOldProtect);

        private unsafe static void _Unprotect(void* addr) {
            if ((PlatformHelper.Current & Platform.Windows) == Platform.Windows) {
                Protection oldProtection;
                // For comparison: dwSize is 1 in Harmony.
                if (!VirtualProtect((IntPtr) addr, (IntPtr) DetourSize, Protection.PAGE_EXECUTE_READWRITE, out oldProtection))
                    throw new System.ComponentModel.Win32Exception();
            }
        }

        private unsafe static void _Copy(void* from, void* to) {
            if (IsX64) {
                *((ulong*) ((ulong) to)) = *((ulong*) ((ulong) from));
                *((uint*) ((ulong) to + 8)) = *((uint*) ((ulong) from + 8));
            } else {
                *((uint*) ((uint) to)) = *((uint*) ((uint) from));
                *((ushort*) ((uint) to + 4)) = *((ushort*) ((uint) from + 4));
            }
        }

        private static void _CreateToken(MethodBase method) {
            if (_Tokenized.Contains(method))
                return;
            _Tokenized.Add(method);

            // DynamicMethod can get disposed.
            if (method is DynamicMethod)
                return;
            long token = GetToken(method);
            _TokenToMethod[token] = method;
        }

        private static void _CreateDynMethod(DynamicMethod dm) {
            if (dmd_DynamicMethod_CreateDynMethod != null) {
                dmd_DynamicMethod_CreateDynMethod(dm);
            } else {
                RuntimeMethodHandle handle = (RuntimeMethodHandle) dmd_DynamicMethod_GetMethodDescriptor(dm);
                if (m_RuntimeHelpers__CompileMethod_TakesIntPtr) {
                    dmd_RuntimeHelpers__CompileMethod(null, handle.Value);
                } else {
                    // This likes to die with leo's HookedMethod.
                    // dmd_RuntimeHelpers__CompileMethod(null, dmd_RuntimeMethodHandle_GetMethodInfo(handle));
                }
            }
        }

        public static unsafe void* GetMethodStart(long token)
            => GetMethodStart(_TokenToMethod[token]);
        public static unsafe void* GetMethodStart(MethodBase method) {
            _CreateToken(method);

            RuntimeMethodHandle handle;
            if (method is DynamicMethod) {
                _CreateDynMethod((DynamicMethod) method);
                if (f_DynamicMethod_m_method != null)
                    handle = (RuntimeMethodHandle) f_DynamicMethod_m_method.GetValue(method);
                else if (dmd_DynamicMethod_GetMethodDescriptor != null)
                    handle = (RuntimeMethodHandle) dmd_DynamicMethod_GetMethodDescriptor(method);
                else
                    handle = method.MethodHandle;
            } else
                handle = method.MethodHandle;

            // "Pin" the method.
            RuntimeHelpers.PrepareMethod(handle);

            return handle.GetFunctionPointer().ToPointer();
        }
        [Obsolete("Delegates behave uncontrollably. Use MethodBases instead. This API will be removed soon.")]
        public static unsafe void* GetDelegateStart(Delegate d) {
            // Prevent the GC from touching the delegate.
            _DelegateGCHandles[d] = GCHandle.Alloc(d);
            // "Pin" the method.
            RuntimeHelpers.PrepareDelegate(d);
            RuntimeHelpers.PrepareMethod(d.Method.MethodHandle);
            return Marshal.GetFunctionPointerForDelegate(d).ToPointer();
        }

        public static unsafe long GetToken(MethodBase method)
            => (long) ((ulong) method.MetadataToken) << 32 | (
                (uint) ((method.Module.Name.GetHashCode() << 5) + method.Module.Name.GetHashCode()) ^
                (uint) method.Module.Assembly.FullName.GetHashCode()
            );

        public static long GetToken(Mono.Cecil.MethodReference method)
            => (long) ((ulong) method.MetadataToken.ToInt32()) << 32 | (
                (uint) ((method.Module.Name.GetHashCode() << 5) + method.Module.Name.GetHashCode()) ^
                (uint) method.Module.Assembly.FullName.GetHashCode()
            );

        public static unsafe void Detour(this MethodBase from, IntPtr to)
            => Detour(GetMethodStart(from), to.ToPointer());
        public static unsafe void Detour(this MethodBase from, MethodBase to)
            => Detour(GetMethodStart(from), GetMethodStart(to));
        [Obsolete("Delegates behave uncontrollably. Use MethodBases instead. This API will be removed soon.")]
        public static unsafe void Detour(this MethodBase from, Delegate to)
            => Detour(GetMethodStart(from), GetDelegateStart(to));
        public static unsafe void DetourMethod(IntPtr from, IntPtr to)
            => Detour(from.ToPointer(), to.ToPointer());

        public static unsafe T Detour<T>(this MethodBase from, IntPtr to) {
            Detour(from, to);
            return GetTrampoline<T>(from);
        }
        public static unsafe T Detour<T>(this MethodBase from, MethodBase to) {
            Detour(from, to);
            return GetTrampoline<T>(from);
        }
        [Obsolete("Delegates behave uncontrollably. Use MethodBases instead. This API will be removed soon.")]
        public static unsafe T Detour<T>(this MethodBase from, Delegate to) {
            Detour(from, to);
            return GetTrampoline<T>(from);
        }

        public static unsafe void Detour(void* from, void* to, bool store = true) {
            _Unprotect(from);
            _Unprotect(to);

            IntPtr orig = IntPtr.Zero;
            if (store) {
                orig = Marshal.AllocHGlobal(DetourSize);
                _Unprotect(orig.ToPointer());
                _Copy(from, orig.ToPointer());
            }

            if (IsX64) {
                *((byte*)  ((ulong) from))      = 0x48;
                *((byte*)  ((ulong) from + 1))  = 0xB8;
                *((ulong*) ((ulong) from + 2))  = (ulong) to;
                *((byte*)  ((ulong) from + 10)) = 0xFF;
                *((byte*)  ((ulong) from + 11)) = 0xE0;
            } else {
                *((byte*)  ((ulong) from))      = 0x68;
                *((uint*)  ((ulong) from + 1))  = (uint) to;
                *((byte*)  ((ulong) from + 5))  = 0xC3;
            }

            if (!store)
                return;

            long key = (long) from;

            if (!_Origs.ContainsKey(key))
                _Origs[key] = orig;

            List<IntPtr> reverts;
            if (!_Reverts.TryGetValue(key, out reverts)) {
                reverts = new List<IntPtr>();
                _Reverts[key] = reverts; 
            }
            reverts.Add(orig);

            IntPtr curr = Marshal.AllocHGlobal(DetourSize);
            _Copy(from, curr.ToPointer());
            _Current[key] = curr;

            _GetAllTrampolines(from).Add(new Trampolines());
        }

        public static unsafe void Undetour(this MethodBase target, int level = -1)
            => Undetour(GetMethodStart(target), level);
        [Obsolete("Delegates behave uncontrollably. Use MethodBases instead. This API will be removed soon.")]
        public static unsafe void Undetour(this Delegate target, int level = -1)
            => Undetour(GetDelegateStart(target), level);
        public static unsafe void Undetour(void* target, int level = -1) {
            List<IntPtr> reverts;
            if (!_Reverts.TryGetValue((long) target, out reverts) ||
                reverts.Count == 0)
                return;

            if (level < 0)
                level = reverts.Count + level;
            if (level < 0)
                level = 0;
            if (level >= reverts.Count)
                level = reverts.Count - 1;

            IntPtr current = reverts[level];
            reverts.RemoveAt(level);

            if (level == reverts.Count) {
                if (reverts.Count == 0) {
                    current = _Origs[(long) target];
                }
                _Copy(current.ToPointer(), target);
                _Current[(long) target] = current;
                return;
            }

            Marshal.FreeHGlobal(current);

            List<Trampolines> allTrampolines = _GetAllTrampolines(target);
            allTrampolines.RemoveAt(level);
            void* code;
            for (int tlevel = level; tlevel < reverts.Count; tlevel++) {
                code = reverts[tlevel].ToPointer();
                Trampolines trampolines = allTrampolines[tlevel];
                foreach (DynamicMethod dm in trampolines)
                    _Copy(code, GetMethodStart(dm));
            }
        }

        public static unsafe int GetDetourLevel(this MethodBase target)
            => GetDetourLevel(GetMethodStart(target));
        [Obsolete("Delegates behave uncontrollably. Use MethodBases instead. This API will be removed soon.")]
        public static unsafe int GetDetourLevel(this Delegate target)
            => GetDetourLevel(GetDelegateStart(target));
        public static unsafe int GetDetourLevel(void* target) {
            List<IntPtr> reverts;
            if (!_Reverts.TryGetValue((long) target, out reverts))
                return 0;
            return reverts.Count;
        }

        public static unsafe bool Refresh(this MethodBase target)
            => Refresh(GetMethodStart(target));
        [Obsolete("Delegates behave uncontrollably. Use MethodBases instead. This API will be removed soon.")]
        public static unsafe bool Refresh(this Delegate target)
            => Refresh(GetDelegateStart(target));
        public static unsafe bool Refresh(void* target) {
            List<IntPtr> reverts;
            if (!_Reverts.TryGetValue((long) target, out reverts) ||
                reverts.Count == 0)
                return false;
            _Copy(_Current[(long) target].ToPointer(), target);
            return true;
        }

        private static unsafe List<Trampolines> _GetAllTrampolines(this MethodBase target)
            => _GetAllTrampolines(GetMethodStart(target));
        private static unsafe List<Trampolines> _GetAllTrampolines(void* target) {
            long key = (long) target;
            List<Trampolines> trampolines;
            if (_Trampolines.TryGetValue(key, out trampolines))
                return trampolines;
            return _Trampolines[key] = new List<Trampolines>();
        }

        private static unsafe Trampolines _GetTrampolines(this MethodBase target, int level = -1)
            => _GetTrampolines(GetMethodStart(target), level);
        private static unsafe Trampolines _GetTrampolines(void* target, int level) {
            List<Trampolines> trampolines = _GetAllTrampolines(target);

            if (level < 0)
                level = trampolines.Count + level;
            if (level < 0)
                level = 0;
            if (level >= trampolines.Count)
                level = trampolines.Count - 1;

            return trampolines.Count == 0 ? null : trampolines[level - 1];
        }

        public static unsafe T GetOrigTrampoline<T>(this MethodBase target) {
            void* p = GetMethodStart(target);
            if (!_Origs.ContainsKey((long) p))
                return default(T);

            ulong key = (ulong) p;
            Type t = typeof(T);
            Delegate del;
            if (_OrigTrampolines.TryGetValue(key, t, out del))
                return (T) (object) del;

            del = (Delegate) (object) CreateTrampolineDirect<T>(target);
            _OrigTrampolines[key, t] = del;
            return (T) (object) del;
        }

        public static unsafe T GetTrampoline<T>(this MethodBase target) {
            void* p = GetMethodStart(target);
            List<IntPtr> reverts;
            if (!_Reverts.TryGetValue((long) p, out reverts) ||
                reverts.Count == 0)
                return default(T);

            if (reverts.Count == 1)
                return GetOrigTrampoline<T>(target);

            IntPtr code = reverts[reverts.Count - 1];
            ulong key = (ulong) p;
            Type t = typeof(T);
            T del = CreateTrampolineDirect<T>(target, code);
            return (T) (object) del;
        }

        public static unsafe T GetNextTrampoline<T>(this MethodBase target) {
            void* p = GetMethodStart(target);
            IntPtr code;
            bool codeTmp = false;
            if (!_Current.TryGetValue((long) p, out code)) {
                codeTmp = true;
                code = Marshal.AllocHGlobal(DetourSize);
                _Copy(p, code.ToPointer());
            }

            ulong key = (ulong) p;
            Type t = typeof(T);
            T del = CreateTrampolineDirect<T>(target, code);
            if (codeTmp)
                Marshal.FreeHGlobal(code);
            return (T) (object) del;
        }

        public static unsafe T CreateTrampolineDirect<T>(MethodBase target)
            => CreateTrampolineDirect<T>(target, IntPtr.Zero);
        public static unsafe T CreateTrampolineDirect<T>(MethodBase target, IntPtr code) {
            Type t = typeof(T);
            return (T) (object) CreateTrampolineDirect(target, code, t.GetMethod("Invoke")).CreateDelegate(t);
        }

        public static unsafe DynamicMethod CreateOrigTrampoline(this MethodBase target, MethodInfo invokeInfo = null)
            => CreateTrampolineDirect(target, IntPtr.Zero, invokeInfo);

        public static unsafe DynamicMethod CreateTrampoline(this MethodBase target, MethodInfo invokeInfo = null) {
            void* p = GetMethodStart(target);
            List<IntPtr> reverts;
            if (!_Reverts.TryGetValue((long) p, out reverts) ||
                reverts.Count == 0)
                return null;

            if (reverts.Count == 1)
                return CreateOrigTrampoline(target, invokeInfo);

            IntPtr code = reverts[reverts.Count - 1];
            return CreateTrampolineDirect(target, code, invokeInfo);
        }

        public static unsafe DynamicMethod CreateTrampolineDirect(MethodBase target)
            => CreateTrampolineDirect(target, IntPtr.Zero);
        public static unsafe DynamicMethod CreateTrampolineDirect(MethodBase target, IntPtr code, MethodInfo invokeInfo = null) {
            _CreateToken(target);

            invokeInfo = invokeInfo ?? (target as MethodInfo);
            Type returnType = invokeInfo?.ReturnType ?? target.DeclaringType;

            ParameterInfo[] args = invokeInfo.GetParameters();
            Type[] argTypes;

            if (invokeInfo == target /* not a delegate Invoke method */ && !target.IsStatic) {
                argTypes = new Type[args.Length + 1];
                argTypes[0] = target.DeclaringType;
                for (int i = 0; i < args.Length; i++)
                    argTypes[i + 1] = args[i].ParameterType;
            } else {
                argTypes = new Type[args.Length];
                for (int i = 0; i < args.Length; i++)
                    argTypes[i] = args[i].ParameterType;
            }

            DynamicMethod dm = new DynamicMethod($"trampoline_{target.Name}_{(code == IntPtr.Zero ? "orig" : ((ulong) code).ToString(IsX64 ? "X16" : "X8"))}", returnType, argTypes, target.Module, true);
            ILGenerator il = dm.GetILGenerator();

            if (code != IntPtr.Zero) {
                for (int i = 64; i > -1; --i)
                    il.Emit(OpCodes.Nop);
                if (returnType != typeof(void)) {
                    il.Emit(OpCodes.Ldnull);
                    if (returnType.IsValueType)
                        il.Emit(OpCodes.Box, returnType);
                }
                il.Emit(OpCodes.Ret);

                dm.Invoke(null, new object[argTypes.Length]);

                _Copy(code.ToPointer(), GetMethodStart(dm));

                // Need to be added this early - they aren't cached, but updated.
                target._GetTrampolines().Add(dm);

            } else {
                il.Emit(OpCodes.Ldc_I8, GetToken(target));
                il.Emit(OpCodes.Call, TrampolinePrefix);

                // TODO: [RuntimeDetour] Can be optimized; What about ref types?
                for (int i = 0; i < argTypes.Length; i++)
                    il.Emit(OpCodes.Ldarg, i);

                if (target is MethodInfo)
                    il.Emit(OpCodes.Call, (MethodInfo) target);
                else if (target is ConstructorInfo)
                    // Calls base constructor
                    il.Emit(OpCodes.Call, (ConstructorInfo) target);

                il.Emit(OpCodes.Ldc_I8, GetToken(target));
                il.Emit(OpCodes.Call, TrampolineSuffix);

                il.Emit(OpCodes.Ret);
            }

            return dm;
        }

        public static MethodInfo TrampolinePrefix = typeof(RuntimeDetour).GetMethod("PrepareOrig", new Type[] { typeof(long) });
        public static unsafe void PrepareOrig(long targetToken) {
            long target = (long) GetMethodStart(targetToken);
            IntPtr code = _Origs[target];
            _Copy(code.ToPointer(), (void*) target);
        }

        public static MethodInfo TrampolineSuffix = typeof(RuntimeDetour).GetMethod("UnprepareOrig", new Type[] { typeof(long) });
        public static unsafe void UnprepareOrig(long targetToken) {
            long target = (long) GetMethodStart(targetToken);
            IntPtr code = _Current[target];
            _Copy(code.ToPointer(), (void*) target);
        }

    }
}
