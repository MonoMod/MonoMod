using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;

namespace MonoMod.InlineRT {
    public static class MMILProxyManager {

        public static Type t_MMILProxy = typeof(MMILProxy);

        public static Dictionary<long, WeakReference> ModderMap = new Dictionary<long, WeakReference>();
        public static ObjectIDGenerator ModderIdGen = new ObjectIDGenerator();

        private static Assembly MonoModAsm = Assembly.GetExecutingAssembly();

        static MMILProxyManager() {
            // TODO automatically create MMILProxy
        }

        public static MonoModder Self {
            get {
                StackTrace st = new StackTrace();
                for (int i = 1; i < st.FrameCount; i++) {
                    StackFrame frame = st.GetFrame(i);
                    MethodBase method = frame.GetMethod();
                    if (method.DeclaringType.Assembly != MonoModAsm)
                        return GetModder(method.DeclaringType.Assembly.GetName().Name);
                }
                return null;
            }
        }

        public static void Register(MonoModder self) {
            bool firstTime;
            ModderMap[ModderIdGen.GetId(self, out firstTime)] = new WeakReference(self);
            if (!firstTime)
                throw new InvalidOperationException("MonoModder instance already registered in MMILProxyManager");
        }

        public static long GetId(MonoModder self) {
            bool firstTime;
            long id = ModderIdGen.GetId(self, out firstTime);
            if (firstTime)
                throw new InvalidOperationException("MonoModder instance wasn't registered in MMILProxyManager");
            return id;
        }

        public static MonoModder GetModder(string asmName) {
            string idString = asmName;
            idString = idString.Substring(idString.IndexOf("-ID:") + 4) + ' ';
            idString = idString.Substring(0, idString.IndexOf(' '));
            long id;
            if (!long.TryParse(idString, out id))
                throw new InvalidOperationException($"Cannot get MonoModder ID from assembly name {asmName}");
            return (MonoModder) ModderMap[id].Target;
        }


        public static MethodReference Relink(MonoModder self, MethodReference orig) {
            orig.DeclaringType = self.Module.ImportReference(MonoModAsm.GetType(
                $"{t_MMILProxy.FullName}{orig.DeclaringType.FullName.Substring(4)}"
                    .Replace('/', '+')
            ));
            return orig;
        }

    }
}
