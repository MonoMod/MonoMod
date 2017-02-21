using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace MonoMod.InlineRT {
    public delegate object MMILProxyDelegate(params object[] args);

    public static class MMILProxy {

        private static readonly Type[] _ManyObjects = new Type[2] { typeof(object), typeof(object[]) };

        public static List<Tuple<string, DynamicMethodDelegate>> Cache = new List<Tuple<string, DynamicMethodDelegate>>();

        static MMILProxy() {
            FillCache(typeof(MMILRT));
        }

        public static void FillCache(Type type) {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                Cache.Add(Tuple.Create(
                    method.GetFindableID(type: "MMIL" + type.FullName.Substring("MonoMod.InlineRT.MMILRT".Length).Replace("+", "/"), proxyMethod: true),
                    method.GetDelegate()
                ));

            foreach (Type nested in type.GetNestedTypes())
                FillCache(nested);
        }

        public static void FillMap(MonoModder self, Dictionary<string, MMILProxyDelegate> map) {
            foreach (Tuple<string, DynamicMethodDelegate> tuple in Cache)
                map[tuple.Item1] = (object[] args) => {
                    // MMILRT expects self as first argument
                    object[] fullArgs = new object[args.Length + 1];
                    Array.Copy(args, 0, fullArgs, 1, args.Length);
                    fullArgs[0] = self;
                    return tuple.Item2(null, fullArgs);
                };
        }

    }
}
