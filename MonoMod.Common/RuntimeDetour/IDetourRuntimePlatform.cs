using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Linq;

namespace MonoMod.RuntimeDetour {
#if !MONOMOD_INTERNAL
    public
#endif
    interface IDetourRuntimePlatform {
        IntPtr GetNativeStart(MethodBase method);
        MethodInfo CreateCopy(MethodBase method);
        bool TryCreateCopy(MethodBase method, out MethodInfo dm);
        void Pin(MethodBase method);
        void Unpin(MethodBase method);
        MethodBase GetDetourTarget(MethodBase from, MethodBase to);
    }
}
