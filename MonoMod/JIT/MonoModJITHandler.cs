using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace MonoMod.JIT
{
    public static class MonoModJITHandler {

        private readonly static Dictionary<Assembly, MonoModJIT> CacheMonoModJIT = new Dictionary<Assembly, MonoModJIT>();
        private static Assembly MonoModAsm;

        static MonoModJITHandler() {
            MonoModAsm = Assembly.GetExecutingAssembly();
        }

        //Main patching helpers
        public static MethodInfo MMGetCallingMethod(bool ignoreMonoMod = true) {
            StackTrace st = new StackTrace();
            for (int i = 1; i < st.FrameCount; i++) {
                StackFrame frame = st.GetFrame(i);
                MethodInfo method = (MethodInfo) frame.GetMethod();
                if (ignoreMonoMod && method.DeclaringType.Assembly == MonoModAsm) {
                    continue;
                }
                return method;
            }
            return null;
        }

        public static MonoModJIT MMGetJIT(this Assembly asm) {
            if (asm == null) {
                asm = MMGetCallingMethod().DeclaringType.Assembly;
            }

            MonoModJIT jit;
            if (!CacheMonoModJIT.TryGetValue(asm, out jit)) {
                jit = new MonoModJIT(asm);
                CacheMonoModJIT[asm] = jit;
            }

            return jit;
        }

        public static object MMRun(object instance, params object[] args) {
            return MMRunM(MMGetCallingMethod(), instance, true, args);
        }

        public static object MMRunD(this Delegate del, object instance, params object[] args) {
            if (del == null) {
                return MMRunM(MMGetCallingMethod(), instance, true, args);
            }
            return MMRunM(del.Method, instance, false, args);
        }

        public static object MMRunM(this MethodInfo method, object instance, bool shouldThrow, params object[] args) {
            MonoModJIT jit = MMGetJIT(method.DeclaringType.Assembly);

            if (method.DeclaringType.Assembly == jit.PatchedAssembly) {
                return null;
            }
            
            DynamicMethodDelegate dmd = delegate(object target, object[] args_) {
                Console.WriteLine("debug: target: " + instance);
                for (int i = 0; i < args.Length; i++) {
                    Console.WriteLine("debug: " + i + ": " + args_[i]);
                }
                return null;
            };
            dmd(instance, args);
            
            object value = jit.GetParsed(method)(instance, args);
            if (shouldThrow) {
                throw new MonoModJITPseudoException(value);
            } else {
                return value;
            }
        }
        
    }
}
