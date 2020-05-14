using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Utils {
    public static partial class Extensions {

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
                return delegates[0].Method.CreateDelegate(type, delegates[0].Target);

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
                    result = delegates[0].Method.CreateDelegate(type, delegates[0].Target);
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
                if (e_ is ReflectionTypeLoadException rtle) {
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

        // This only exists in .NET Framework 4.5+ and .NET Standard 1.0+,
        // but it's scientifically proven that .NET Framework 4.0 doesn't really exist.
        private static readonly Type t_StateMachineAttribute =
            typeof(object).Assembly
            .GetType("System.Runtime.CompilerServices.StateMachineAttribute");
        private static readonly PropertyInfo p_StateMachineType =
            t_StateMachineAttribute?.GetProperty("StateMachineType");

        /// <summary>
        /// Get the method of interest for a given state machine method.
        /// </summary>
        /// <param name="method">The method creating the state machine.</param>
        /// <returns>The "main" method in the state machine.</returns>
        public static MethodInfo GetStateMachineTarget(this MethodInfo method) {
            if (p_StateMachineType == null)
                return null;

            foreach (Attribute attrib in method.GetCustomAttributes(false))
                if (t_StateMachineAttribute.IsCompatible(attrib.GetType()))
                    return (p_StateMachineType.GetValue(attrib, null) as Type)?.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            return null;
        }

    }
}