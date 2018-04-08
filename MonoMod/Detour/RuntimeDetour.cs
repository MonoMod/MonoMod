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
using MonoMod.RuntimeDetour;

namespace MonoMod.Detour {
    [Obsolete("Please switch to the new MonoMod.RuntimeDetour namespace. A subset of the old API is still available, using the new ")]
    public static class RuntimeDetour {

        private static Dictionary<long, Stack<NativeDetour>> _Detours = new Dictionary<long, Stack<NativeDetour>>();
        private static Stack<NativeDetour> _GetDetours(long from) {
            Stack<NativeDetour> detours;
            if (_Detours.TryGetValue(from, out detours))
                return detours;
            return _Detours[from] = new Stack<NativeDetour>();
        }

        public static bool IsX64 { get; } = IntPtr.Size == 8;
        public static int DetourSize {
            get {
                throw new NotSupportedException("Use MonoMod.RuntimeDetour.DetourManager.Native.Size(...) instead.");
            }
        }

        public static unsafe void* GetMethodStart(long token) {
            throw new NotSupportedException("Tokens no longer supported.");
        }
        public static unsafe void* GetMethodStart(MethodBase method) {
            return method.GetJITStart().ToPointer();
        }
        public static unsafe void* GetDelegateStart(Delegate d) {
            return d.Method.GetJITStart().ToPointer();
        }

        public static long GetToken(MethodBase method) {
            throw new NotSupportedException("Tokens no longer supported.");
        }
        public static long GetToken(Mono.Cecil.MethodReference method) {
            throw new NotSupportedException("Tokens no longer supported.");
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
            NativeDetour detour = new NativeDetour((IntPtr) from, (IntPtr) to);
            if (!store)
                return;
            Stack<NativeDetour> detours = _GetDetours((long) from);
            detours.Push(detour);
        }

        public static unsafe void Undetour(this MethodBase target, int level = -1)
            => Undetour(GetMethodStart(target), level);
        public static unsafe void Undetour(this Delegate target, int level = -1)
            => Undetour(GetDelegateStart(target), level);
        public static unsafe void Undetour(void* target, int level = -1) {
            Stack<NativeDetour> detours = _GetDetours((long) target);
            if (detours.Count == 0)
                return;
            NativeDetour detour = detours.Pop();
            detour.Undo();
            detour.Free();
        }

        public static unsafe int GetDetourLevel(this MethodBase target)
            => GetDetourLevel(GetMethodStart(target));
        public static unsafe int GetDetourLevel(this Delegate target)
            => GetDetourLevel(GetDelegateStart(target));
        public static unsafe int GetDetourLevel(void* target) {
            return _GetDetours((long) target).Count;
        }

        public static unsafe bool Refresh(this MethodBase target)
            => Refresh(GetMethodStart(target));
        public static unsafe bool Refresh(this Delegate target)
            => Refresh(GetDelegateStart(target));
        public static unsafe bool Refresh(void* target) {
            Stack<NativeDetour> detours = _GetDetours((long) target);
            if (detours.Count == 0)
                return false;
            NativeDetour detour = detours.Peek();
            detour.Apply();
            return true;
        }

        public static T GetOrigTrampoline<T>(this MethodBase target) {
            Stack<NativeDetour> detours = _GetDetours((long) target.GetJITStart());
            if (detours.Count == 0)
                return default(T);
            // Last() returns the first element in a Stack.
            return detours.Last()._GenerateTrampoline<T>();
        }

        public static T GetTrampoline<T>(this MethodBase target) {
            Stack<NativeDetour> detours = _GetDetours((long) target.GetJITStart());
            if (detours.Count == 0)
                return default(T);
            return detours.Peek()._GenerateTrampoline<T>();
        }

        public static T GetNextTrampoline<T>(this MethodBase target) {
            throw new NotSupportedException("Old trampoline generator no longer available. This usage is no longer supported.");
        }

        public static T CreateTrampolineDirect<T>(MethodBase target) {
            Stack<NativeDetour> detours = _GetDetours((long) target.GetJITStart());
            if (detours.Count == 0)
                return default(T);
            return detours.Peek()._GenerateTrampoline<T>();
        }
        public static T CreateTrampolineDirect<T>(MethodBase target, IntPtr code) {
            throw new NotSupportedException("Old trampoline generator no longer available. This usage is no longer supported.");
        }

        public static DynamicMethod CreateOrigTrampoline(this MethodBase target, MethodInfo invokeInfo = null) {
            Stack<NativeDetour> detours = _GetDetours((long) target.GetJITStart());
            if (detours.Count == 0)
                return null;
            // Last() returns the first element in a Stack.
            return detours.Last().GenerateTrampoline(invokeInfo);
        }

        public static DynamicMethod CreateTrampoline(this MethodBase target, MethodInfo invokeInfo = null) {
            Stack<NativeDetour> detours = _GetDetours((long) target.GetJITStart());
            if (detours.Count == 0)
                return null;
            return detours.Peek().GenerateTrampoline(invokeInfo);
        }

        public static DynamicMethod CreateTrampolineDirect(MethodBase target) {
            throw new NotSupportedException("Old trampoline generator no longer available. This usage is no longer supported.");
        }
        public static DynamicMethod CreateTrampolineDirect(MethodBase target, IntPtr code, MethodInfo invokeInfo = null) {
            throw new NotSupportedException("Old trampoline generator no longer available. This usage is no longer supported.");
        }

        public static MethodInfo TrampolinePrefix = null;
        public static void PrepareOrig(long targetToken) {
            throw new NotSupportedException("Old trampoline generator no longer available.");
        }

        public static MethodInfo TrampolineSuffix = null;
        public static void UnprepareOrig(long targetToken) {
            throw new NotSupportedException("Old trampoline generator no longer available.");
        }

    }
}
