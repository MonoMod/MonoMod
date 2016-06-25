using System;

namespace MonoMod.JIT {
    /// <summary>
    /// Not a real exception. Just to "break out" of the unpatched code.
    /// </summary>
    public class MonoModJITPseudoException : Exception {

        public object Value;

        public MonoModJITPseudoException() {
        }

        public MonoModJITPseudoException(object value)
            : this() {
            Value = value;
        }

    }
}

