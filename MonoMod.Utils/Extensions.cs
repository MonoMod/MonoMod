using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Utils {
    public static class Extensions {

        /// <summary>
        /// Create a hexadecimal string for the given bytes.
        /// </summary>
        /// <param name="data">The input bytes.</param>
        /// <returns>The output hexadecimal string.</returns>
        public static string ToHexadecimalString(this byte[] data)
            => BitConverter.ToString(data).Replace("-", string.Empty);

        /// <summary>
        /// Invokes all delegates in the invocation list, passing on the result to the next.
        /// </summary>
        /// <typeparam name="T">Type of the result.</typeparam>
        /// <param name="md">The multicast delegate.</param>
        /// <param name="val">The initial value and first parameter.</param>
        /// <param name="args">Any other arguments that may be passed.</param>
        /// <returns>The result of all delegates.</returns>
        public static T InvokePassing<T>(this MulticastDelegate md, T val, params object[] args) {
            if (md == null)
                return val;

            object[] args_ = new object[args.Length + 1];
            args_[0] = val;
            Array.Copy(args, 0, args_, 1, args.Length);

            Delegate[] ds = md.GetInvocationList();
            for (int i = 0; i < ds.Length; i++)
                args_[0] = ds[i].DynamicInvoke(args_);

            return (T) args_[0];
        }

        /// <summary>
        /// Invokes all delegates in the invocation list, as long as the previously invoked delegate returns true.
        /// </summary>
        public static bool InvokeWhileTrue(this MulticastDelegate md, params object[] args) {
            if (md == null)
                return true;

            Delegate[] ds = md.GetInvocationList();
            for (int i = 0; i < ds.Length; i++)
                if (!((bool) ds[i].DynamicInvoke(args)))
                    return false;

            return true;
        }

        /// <summary>
        /// Invokes all delegates in the invocation list, as long as the previously invoked delegate returns false.
        /// </summary>
        public static bool InvokeWhileFalse(this MulticastDelegate md, params object[] args) {
            if (md == null)
                return false;

            Delegate[] ds = md.GetInvocationList();
            for (int i = 0; i < ds.Length; i++)
                if ((bool) ds[i].DynamicInvoke(args))
                    return true;

            return false;
        }

        /// <summary>
        /// Invokes all delegates in the invocation list, as long as the previously invoked delegate returns null.
        /// </summary>
        public static T InvokeWhileNull<T>(this MulticastDelegate md, params object[] args) where T : class {
            if (md == null)
                return null;

            Delegate[] ds = md.GetInvocationList();
            for (int i = 0; i < ds.Length; i++) {
                T result = (T) ds[i].DynamicInvoke(args);
                if (result != null)
                    return result;
            }

            return null;
        }

        /// <summary>
        /// Split PascalCase words to become Pascal Case instead.
        /// </summary>
        /// <param name="input">PascalCaseString</param>
        /// <returns>Pascal Case String</returns>
        public static string SpacedPascalCase(this string input) {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < input.Length; i++) {
                char c = input[i];
                if (i > 0 && char.IsUpper(c))
                    builder.Append(' ');
                builder.Append(c);
            }
            return builder.ToString();
        }

        /// <summary>
        /// Read the string from the BinaryReader BinaryWriter in a C-friendly format.
        /// </summary>
        /// <param name="stream">The input which the method reads from.</param>
        /// <returns>The output string.</returns>
        public static string ReadNullTerminatedString(this BinaryReader stream) {
            string text = "";
            char c;
            while ((c = stream.ReadChar()) != '\0') {
                text += c.ToString();
            }
            return text;
        }

        /// <summary>
        /// Write the string to the BinaryWriter in a C-friendly format.
        /// </summary>
        /// <param name="stream">The output which the method writes to.</param>
        /// <param name="text">The input string.</param>
        public static void WriteNullTerminatedString(this BinaryWriter stream, string text) {
            if (text != null) {
                for (int i = 0; i < text.Length; i++) {
                    char c = text[i];
                    stream.Write(c);
                }
            }
            stream.Write('\0');
        }

        /// <summary>
        /// Cast a delegate from one type to another. Compatible with delegates holding an invocation list (combined delegates).
        /// </summary>
        /// <param name="source">The input delegate.</param>
        /// <returns>The output delegate.</returns>
        public static T CastDelegate<T>(this Delegate source) where T : class => source.CastDelegate(typeof(T)) as T;

        /// <summary>
        /// Cast a delegate from one type to another. Compatible with delegates holding an invocation list (combined delegates).
        /// </summary>
        /// <param name="source">The input delegate.</param>
        /// <param name="type">The wanted output delegate type.</param>
        /// <returns>The output delegate.</returns>
        public static Delegate CastDelegate(this Delegate source, Type type) {
            if (source == null)
                return null;

            Delegate[] delegates = source.GetInvocationList();
            if (delegates.Length == 1)
                return Delegate.CreateDelegate(type, delegates[0].Target, delegates[0].Method);

            Delegate[] delegatesDest = new Delegate[delegates.Length];
            for (int i = 0; i < delegates.Length; i++)
                delegatesDest[i] = delegates[i].CastDelegate(type);
            return Delegate.Combine(delegatesDest);
        }

        public static bool TryCastDelegate<T>(this Delegate source, out T result) where T : class {
            if (source is T cast) {
                result = cast;
                return true;
            }

            bool rv = source.TryCastDelegate(typeof(T), out Delegate resultDel);
            result = resultDel as T;
            return rv;
        }

        public static bool TryCastDelegate(this Delegate source, Type type, out Delegate result) {
            result = null;
            if (source == null)
                return false;

            try {
                Delegate[] delegates = source.GetInvocationList();
                if (delegates.Length == 1) {
                    result = Delegate.CreateDelegate(type, delegates[0].Target, delegates[0].Method);
                    return true;
                }

                Delegate[] delegatesDest = new Delegate[delegates.Length];
                for (int i = 0; i < delegates.Length; i++)
                    delegatesDest[i] = delegates[i].CastDelegate(type);
                result = Delegate.Combine(delegatesDest);
                return true;

            } catch {
                return false;
            }
        }

        /// <summary>
        /// Print the exception to the console, including extended loading / reflection data useful for mods.
        /// </summary>
        public static void LogDetailed(this Exception e, string tag = null) {
            if (tag == null) {
                Console.WriteLine("--------------------------------");
                Console.WriteLine("Detailed exception log:");
            }
            for (Exception e_ = e; e_ != null; e_ = e_.InnerException) {
                Console.WriteLine("--------------------------------");
                Console.WriteLine(e_.GetType().FullName + ": " + e_.Message + "\n" + e_.StackTrace);
                if (e_ is ReflectionTypeLoadException) {
                    ReflectionTypeLoadException rtle = (ReflectionTypeLoadException) e_;
                    for (int i = 0; i < rtle.Types.Length; i++) {
                        Console.WriteLine("ReflectionTypeLoadException.Types[" + i + "]: " + rtle.Types[i]);
                    }
                    for (int i = 0; i < rtle.LoaderExceptions.Length; i++) {
                        LogDetailed(rtle.LoaderExceptions[i], tag + (tag == null ? "" : ", ") + "rtle:" + i);
                    }
                }
                if (e_ is TypeLoadException) {
                    Console.WriteLine("TypeLoadException.TypeName: " + ((TypeLoadException) e_).TypeName);
                }
                if (e_ is BadImageFormatException) {
                    Console.WriteLine("BadImageFormatException.FileName: " + ((BadImageFormatException) e_).FileName);
                }
            }
        }

        /// <summary>
        /// Creates a delegate of the specified type from this method.
        /// </summary>
        /// <param name="method">The method to create the delegate from.</param>
        /// <typeparam name="T">The type of the delegate to create.</typeparam>
        /// <returns>The delegate for this method.</returns>
        public static Delegate CreateDelegate<T>(this MethodBase method) where T : class
            => CreateDelegate(method, typeof(T), null);
        /// <summary>
        /// Creates a delegate of the specified type with the specified target from this method.
        /// </summary>
        /// <param name="method">The method to create the delegate from.</param>
        /// <typeparam name="T">The type of the delegate to create.</typeparam>
        /// <param name="target">The object targeted by the delegate.</param>
        /// <returns>The delegate for this method.</returns>
        public static Delegate CreateDelegate<T>(this MethodBase method, object target) where T : class
            => CreateDelegate(method, typeof(T), target);
        /// <summary>
        /// Creates a delegate of the specified type from this method.
        /// </summary>
        /// <param name="method">The method to create the delegate from.</param>
        /// <param name="delegateType">The type of the delegate to create.</param>
        /// <returns>The delegate for this method.</returns>
        public static Delegate CreateDelegate(this MethodBase method, Type delegateType)
            => CreateDelegate(method, delegateType, null);
        /// <summary>
        /// Creates a delegate of the specified type with the specified target from this method.
        /// </summary>
        /// <param name="method">The method to create the delegate from.</param>
        /// <param name="delegateType">The type of the delegate to create.</param>
        /// <param name="target">The object targeted by the delegate.</param>
        /// <returns>The delegate for this method.</returns>
        public static Delegate CreateDelegate(this MethodBase method, Type delegateType, object target) {
            if (!typeof(Delegate).IsAssignableFrom(delegateType))
                throw new ArgumentException("Type argument must be a delegate type!");
            if (method is DynamicMethod)
                return ((DynamicMethod) method).CreateDelegate(delegateType, target);

            // TODO: Check delegate Invoke parameters against method parameters.

            RuntimeMethodHandle handle = method.MethodHandle;
            RuntimeHelpers.PrepareMethod(handle);
            IntPtr ptr = handle.GetFunctionPointer();
            return (Delegate) Activator.CreateInstance(delegateType, target, ptr);
        }

    }
}