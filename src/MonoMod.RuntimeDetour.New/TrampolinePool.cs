using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.RuntimeDetour {
    internal static class TrampolinePool {

        private static readonly ConcurrentDictionary<MethodSignature, ConcurrentBag<WeakReference<MethodInfo>>> pool
            = new();

        private static ConcurrentBag<WeakReference<MethodInfo>> PoolForSig(MethodSignature sig)
            => pool.GetOrAdd(sig, _ => new());

        public static MethodInfo Rent(MethodSignature sig) {
            var pool = PoolForSig(sig);

            while (pool.TryTake(out var wr)) {
                if (wr.TryGetTarget(out var meth)) {
                    return meth;
                }
            }

            // we couldn't get one from the pool, so we'll create one
            using var dmd = new DynamicMethodDefinition(
                $"Trampoline<{sig}>", sig.ReturnType, (Type[]) sig.Parameters
            );
            return dmd.StubCriticalDetour().Generate();
        }

        public static void Return(MethodInfo trampoline) {
            var pool = PoolForSig(MethodSignature.ForMethod(trampoline));
            pool.Add(new(trampoline));
        }

        private static readonly ConstructorInfo Exception_ctor
            = typeof(Exception).GetConstructor(new Type[] { typeof(string) })?? throw new InvalidOperationException();

        /// <summary>
        /// Fill the DynamicMethodDefinition with a throw.
        /// </summary>
        public static DynamicMethodDefinition StubCriticalDetour(this DynamicMethodDefinition dm) {
            ILProcessor il = dm.GetILProcessor();
            ModuleDefinition ilModule = il.Body.Method.Module;
            for (var i = 0; i < 32; i++) {
                // Prevent mono from inlining the DynamicMethod.
                il.Emit(OpCodes.Nop);
            }
            il.Emit(OpCodes.Ldstr, $"{dm.Definition.Name} should've been detoured!");
            il.Emit(OpCodes.Newobj, ilModule.ImportReference(Exception_ctor));
            il.Emit(OpCodes.Throw);
            return dm;
        }

    }
}
