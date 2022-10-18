using System;

namespace MonoMod.Utils {
    public static class GCListener {

        public static event Action? OnCollect;

        static GCListener() {
            Gen2GcCallback.Register(InvokeCollect);
        }

        private static bool InvokeCollect() {
            OnCollect?.Invoke();
            return true;
        }

    }
}
