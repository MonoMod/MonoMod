using MonoMod.InlineRT;
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

namespace MonoMod.Detour {
    public static class RuntimeDetour {

        public static bool IsX64 { get; } = IntPtr.Size == 8;

        private readonly static FieldInfo f_DynamicMethod_m_method =
            typeof(DynamicMethod).GetField("m_method", BindingFlags.NonPublic | BindingFlags.Instance) ??
            typeof(DynamicMethod).GetField("mhandle", BindingFlags.NonPublic | BindingFlags.Instance);
        private readonly static MethodInfo m_Console_WriteLine_string = typeof(Console).GetMethod("WriteLine", new Type[] { typeof(string) });

        private readonly static MethodInfo m_PrepareOrig = typeof(RuntimeDetour).GetMethod("PrepareOrig", new Type[] { typeof(long) });
        private readonly static MethodInfo m_UnprepareOrig = typeof(RuntimeDetour).GetMethod("UnprepareOrig", new Type[] { typeof(long) });

        private readonly static unsafe LongDictionary<byte[]> _Origs = new LongDictionary<byte[]>();
        private readonly static unsafe LongDictionary<Stack<byte[]>> _Reverts = new LongDictionary<Stack<byte[]>>();
        private readonly static unsafe LongDictionary<byte[]> _Current = new LongDictionary<byte[]>();
        private readonly static unsafe FastDictionary<ulong, Type, Delegate> _OrigTrampolines = new FastDictionary<ulong, Type, Delegate>();

        public static unsafe void* GetMethodStart(MethodBase method) {
            RuntimeMethodHandle handle;
            if (method is DynamicMethod)
                handle = (RuntimeMethodHandle) f_DynamicMethod_m_method.GetValue(method);
            else
                handle = method.MethodHandle;

            RuntimeHelpers.PrepareMethod(handle);
            return handle.GetFunctionPointer().ToPointer();
        }
        public static unsafe void* GetDelegateStart(Delegate d) {
            RuntimeHelpers.PrepareDelegate(d);
            return Marshal.GetFunctionPointerForDelegate(d).ToPointer();
        }

        public static unsafe void Detour(this MethodBase from, IntPtr to)
            => Detour(GetMethodStart(from), to.ToPointer());
        public static unsafe void Detour(this MethodBase from, MethodBase to)
            => Detour(GetMethodStart(from), GetMethodStart(to));
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
        public static unsafe T Detour<T>(this MethodBase from, Delegate to) {
            Detour(from, to);
            return GetTrampoline<T>(from);
        }

        public static unsafe void Detour(void* from, void* to, bool store = true) {
            byte[] orig = null;
            if (store) {
                orig = new byte[IsX64 ? 12 : 6];
                Marshal.Copy(new IntPtr(from), orig, 0, orig.Length);
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

            if (orig == null)
                return;

            long key = (long) from;

            if (!_Origs.ContainsKey(key))
                _Origs[key] = orig;

            Stack<byte[]> reverts;
            if (!_Reverts.TryGetValue(key, out reverts)) {
                reverts = new Stack<byte[]>();
                _Reverts[key] = reverts; 
            }
            reverts.Push(orig);

            byte[] curr = new byte[orig.Length];
            Marshal.Copy(new IntPtr(from), curr, 0, curr.Length);
            _Current[key] = curr;
        }

        public static unsafe void Undetour(this MethodBase target)
            => Undetour(GetMethodStart(target));
        public static unsafe void Undetour(this Delegate target)
            => Undetour(GetDelegateStart(target));
        public static unsafe void Undetour(void* target) {
            Stack<byte[]> reverts;
            if (!_Reverts.TryGetValue((long) target, out reverts) ||
                reverts.Count == 0)
                return;
            byte[] current = reverts.Pop();
            Marshal.Copy(current, 0, new IntPtr(target), current.Length);
            _Current[(long) target] = current;
        }

        public static unsafe T GetOrigTrampoline<T>(this MethodBase target) {
            void* p = GetMethodStart(target);
            byte[] orig;
            if (!_Origs.TryGetValue((long) p, out orig))
                return default(T);

            ulong key = (ulong) p;
            Type t = typeof(T);
            Delegate del;
            if (_OrigTrampolines.TryGetValue(key, t, out del))
                return (T) (object) del;

            del = (Delegate) (object) CreateTrampoline<T>(target);
            _OrigTrampolines[key, t] = del;
            return (T) (object) del;
        }

        public static unsafe T GetTrampoline<T>(this MethodBase target) {
            void* p = GetMethodStart(target);
            Stack<byte[]> reverts;
            if (!_Reverts.TryGetValue((long) p, out reverts) ||
                reverts.Count == 0)
                return default(T);

            if (reverts.Count == 1)
                return GetOrigTrampoline<T>(target);

            byte[] orig = reverts.Peek();
            ulong key = (ulong) p;
            Type t = typeof(T);
            T del = CreateTrampoline<T>(target, orig);
            return (T) (object) del;
        }

        public static unsafe T CreateTrampoline<T>(MethodBase target, byte[] code = null) {
            Type t = typeof(T);
            return (T) (object) CreateTrampoline(target, code, t.GetMethod("Invoke")).CreateDelegate(t);
        }

        public static unsafe DynamicMethod CreateTrampoline(MethodBase target, byte[] code = null, MethodInfo invoke = null) {
            invoke = invoke ?? (target as MethodInfo);

            ParameterInfo[] args = invoke.GetParameters();
            Type[] argTypes = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                argTypes[i] = args[i].ParameterType;

            DynamicMethod dm = new DynamicMethod($"trampoline_{target.Name}_{(code?.GetHashCode())?.ToString() ?? "orig"}", invoke.ReturnType, argTypes, typeof(ReflectionHelper).Module, true);
            ILGenerator il = dm.GetILGenerator();

            if (code != null) {
                for (int i = 64; i > -1; --i)
                    il.Emit(OpCodes.Nop);
                if (invoke.ReturnType != typeof(void)) {
                    il.Emit(OpCodes.Ldnull);
                    if (invoke.ReturnType.IsValueType)
                        il.Emit(OpCodes.Box, invoke.ReturnType);
                }
                il.Emit(OpCodes.Ret);
                dm.Invoke(null, new object[args.Length]);
                void* p = GetMethodStart(dm);
                Marshal.Copy(code, 0, new IntPtr(p), code.Length);
                p = (void*) ((ulong) p + (ulong) code.Length);
                Detour(p, GetMethodStart(target), false);

            } else {
                LocalBuilder retVal = null;
                if (target is MethodInfo && ((MethodInfo) target).ReturnType != typeof(void)) {
                    retVal = il.DeclareLocal(((MethodInfo) target).ReturnType);
                } else if (target is ConstructorInfo) {
                    retVal = il.DeclareLocal(target.DeclaringType);
                }

                il.Emit(OpCodes.Ldc_I8, (long) GetMethodStart(target));
                il.Emit(OpCodes.Call, m_PrepareOrig);

                for (int i = 0; i < args.Length; i++)
                    // TODO: [RuntimeDetour] Can be optimized; What about ref types?
                    il.Emit(OpCodes.Ldarg, i);

                if (target is MethodInfo)
                    il.Emit(OpCodes.Call, (MethodInfo) target);
                else if (target is ConstructorInfo)
                    il.Emit(OpCodes.Newobj, (ConstructorInfo) target);

                if (retVal != null)
                    il.Emit(OpCodes.Stloc_0);

                il.Emit(OpCodes.Ldc_I8, (long) GetMethodStart(target));
                il.Emit(OpCodes.Call, m_UnprepareOrig);

                if (retVal != null)
                    il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Ret);
            }

            return dm;
        }

        public static unsafe void PrepareOrig(long target) {
            byte[] code = _Origs[target];
            Marshal.Copy(code, 0, new IntPtr(target), code.Length);
        }

        public static unsafe void UnprepareOrig(long target) {
            byte[] code = _Current[target];
            Marshal.Copy(code, 0, new IntPtr(target), code.Length);
        }

    }
}
