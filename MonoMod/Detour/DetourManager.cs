using MonoMod.InlineRT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MonoMod.Helpers;

namespace MonoMod.Detour {
    public static class ManagedDetourManager {

        private static LongDictionary<DynamicMethodDelegate> _ManagedDetours = new LongDictionary<DynamicMethodDelegate>();

        public static void Detour(this MethodInfo from, MulticastDelegate to) {

        }

        public static void Invoke(this RuntimeMethodHandle method) {
        }

    }
}
