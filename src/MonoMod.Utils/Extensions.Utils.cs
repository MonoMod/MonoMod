using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace MonoMod.Utils
{
    public static partial class Extensions
    {

        /// <summary>
        /// Create a hexadecimal string for the given bytes.
        /// </summary>
        /// <param name="data">The input bytes.</param>
        /// <returns>The output hexadecimal string.</returns>
        public static string ToHexadecimalString(this byte[] data)
            => BitConverter.ToString(data).Replace("-", string.Empty, StringComparison.Ordinal);

        /// <summary>
        /// Invokes all delegates in the invocation list, passing on the result to the next.
        /// </summary>
        /// <typeparam name="T">Type of the result.</typeparam>
        /// <param name="md">The multicast delegate.</param>
        /// <param name="val">The initial value and first parameter.</param>
        /// <param name="args">Any other arguments that may be passed.</param>
        /// <returns>The result of all delegates.</returns>
        public static T? InvokePassing<T>(this MulticastDelegate md, T val, params object?[] args)
        {
            if (md == null)
                return val;

            Helpers.ThrowIfArgumentNull(args);
            var args_ = new object?[args.Length + 1];
            args_[0] = val;
            Array.Copy(args, 0, args_, 1, args.Length);

            var ds = md.GetInvocationList();
            for (var i = 0; i < ds.Length; i++)
                args_[0] = ds[i].DynamicInvoke(args_);

            return (T?)args_[0];
        }

        /// <summary>
        /// Invokes all delegates in the invocation list, as long as the previously invoked delegate returns true.
        /// </summary>
        public static bool InvokeWhileTrue(this MulticastDelegate md, params object[] args)
        {
            if (md == null)
                return true;

            Helpers.ThrowIfArgumentNull(args);
            var ds = md.GetInvocationList();
            for (var i = 0; i < ds.Length; i++)
                if (!(bool)ds[i].DynamicInvoke(args)!)
                    return false;

            return true;
        }

        /// <summary>
        /// Invokes all delegates in the invocation list, as long as the previously invoked delegate returns false.
        /// </summary>
        public static bool InvokeWhileFalse(this MulticastDelegate md, params object[] args)
        {
            if (md == null)
                return false;

            Helpers.ThrowIfArgumentNull(args);
            var ds = md.GetInvocationList();
            for (var i = 0; i < ds.Length; i++)
                if ((bool)ds[i].DynamicInvoke(args)!)
                    return true;

            return false;
        }

        /// <summary>
        /// Invokes all delegates in the invocation list, as long as the previously invoked delegate returns null.
        /// </summary>
        public static T? InvokeWhileNull<T>(this MulticastDelegate? md, params object[] args) where T : class
        {
            if (md == null)
                return null;

            Helpers.ThrowIfArgumentNull(args);
            var ds = md.GetInvocationList();
            for (var i = 0; i < ds.Length; i++)
            {
                var result = (T?)ds[i].DynamicInvoke(args);
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
        public static string SpacedPascalCase(this string input)
        {
            Helpers.ThrowIfArgumentNull(input);
            var builder = new StringBuilder();
            for (var i = 0; i < input.Length; i++)
            {
                var c = input[i];
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
        public static string ReadNullTerminatedString(this BinaryReader stream)
        {
            Helpers.ThrowIfArgumentNull(stream);
            var text = "";
            char c;
            while ((c = stream.ReadChar()) != '\0')
            {
                text += c.ToString();
            }
            return text;
        }

        /// <summary>
        /// Write the string to the BinaryWriter in a C-friendly format.
        /// </summary>
        /// <param name="stream">The output which the method writes to.</param>
        /// <param name="text">The input string.</param>
        public static void WriteNullTerminatedString(this BinaryWriter stream, string text)
        {
            Helpers.ThrowIfArgumentNull(stream);
            Helpers.ThrowIfArgumentNull(text);
            if (text != null)
            {
                for (var i = 0; i < text.Length; i++)
                {
                    var c = text[i];
                    stream.Write(c);
                }
            }
            stream.Write('\0');
        }

        // This is effectively just method identification, as performed fore FX/CoreCLR runtimes. Unfortunately, that only exists
        // in Core, and is not available down here in Utils. TODO: figure out hot to use that
        private static readonly Type? RTDynamicMethod =
            typeof(DynamicMethod).GetNestedType("RTDynamicMethod", BindingFlags.NonPublic);
        private static readonly FieldInfo? RTDynamicMethod_m_owner =
            RTDynamicMethod?.GetField("m_owner", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static MethodBase GetRealMethod(MethodBase method)
        {
            if (RTDynamicMethod_m_owner is not null && method.GetType() == RTDynamicMethod)
                return (MethodBase)RTDynamicMethod_m_owner.GetValue(method)!;
            return method;
        }

        /// <summary>
        /// Cast a delegate from one type to another. Compatible with delegates holding an invocation list (combined delegates).
        /// </summary>
        /// <param name="source">The input delegate.</param>
        /// <returns>The output delegate.</returns>
        public static T CastDelegate<T>(this Delegate source) where T : Delegate => (T)Helpers.ThrowIfNull(source).CastDelegate(typeof(T));

        /// <summary>
        /// Cast a delegate from one type to another. Compatible with delegates holding an invocation list (combined delegates).
        /// </summary>
        /// <param name="source">The input delegate.</param>
        /// <param name="type">The wanted output delegate type.</param>
        /// <returns>The output delegate.</returns>
        [return: NotNullIfNotNull("source")]
        public static Delegate? CastDelegate(this Delegate? source, Type type)
        {
            if (source == null)
                return null;

            Helpers.ThrowIfArgumentNull(type);

            if (type.IsAssignableFrom(source.GetType()))
                return source;

            // We *must* use GetRealMethod, which performs masic method identification, as is present in Core
            var delegates = source.GetInvocationList();
            if (delegates.Length == 1)
                return CreateDelegate(GetRealMethod(delegates[0].Method), type, delegates[0].Target);

            var delegatesDest = new Delegate?[delegates.Length];
            for (var i = 0; i < delegates.Length; i++)
                delegatesDest[i] = CreateDelegate(GetRealMethod(delegates[i].Method), type, delegates[i].Target);
            return Delegate.Combine(delegatesDest)!;
        }

        public static bool TryCastDelegate<T>(this Delegate source, [MaybeNullWhen(false)] out T result) where T : Delegate
        {
            if (source is null)
            {
                result = default;
                return false;
            }

            if (source is T cast)
            {
                result = cast;
                return true;
            }


            var rv = source.TryCastDelegate(typeof(T), out var resultDel);
            result = (T?)resultDel;
            return rv;
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "The whole point of this method is to swallow an exception and return false")]
        public static bool TryCastDelegate(this Delegate source, Type type, [MaybeNullWhen(false)] out Delegate? result)
        {
            result = null;
            if (source is null)
                return false;

            try
            {
                result = CastDelegate(source, type);
                return true;
            }
            catch (Exception e)
            {
                MMDbgLog.Warning($"Exception thrown in TryCastDelegate({source.GetType()} -> {type}): {e}");
                return false;
            }
        }

        // This only exists in .NET Framework 4.5+ and .NET Standard 1.0+,
        // but it's scientifically proven that .NET Framework 4.0 doesn't really exist.
        private static readonly Type? t_StateMachineAttribute =
            typeof(object).Assembly
            .GetType("System.Runtime.CompilerServices.StateMachineAttribute");
        private static readonly PropertyInfo? p_StateMachineType =
            t_StateMachineAttribute?.GetProperty("StateMachineType");

        /// <summary>
        /// Get the method of interest for a given state machine method.
        /// </summary>
        /// <param name="method">The method creating the state machine.</param>
        /// <returns>The "main" method in the state machine.</returns>
        public static MethodInfo? GetStateMachineTarget(this MethodInfo method)
        {
            if (p_StateMachineType is null || t_StateMachineAttribute is null)
                return null;

            Helpers.ThrowIfArgumentNull(method);

            foreach (Attribute attrib in method.GetCustomAttributes(false))
                if (t_StateMachineAttribute.IsCompatible(attrib.GetType()))
                    return (p_StateMachineType.GetValue(attrib, null) as Type)?.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            return null;
        }

        /// <summary>
        /// Gets the <i>actual</i> generic method definition of a method, as defined on the fully open type.
        /// </summary>
        /// <param name="method">The potentially instantiated method to find the definition of.</param>
        /// <returns>The original method definition, with no generic arguments filled in.</returns>
        public static MethodBase GetActualGenericMethodDefinition(this MethodInfo method)
        {
            Helpers.ThrowIfArgumentNull(method);
            var genericDefinition = method.IsGenericMethod ? method.GetGenericMethodDefinition()
                                                                  : method;
            return genericDefinition.GetUnfilledMethodOnGenericType();
        }

        public static MethodBase GetUnfilledMethodOnGenericType(this MethodBase method)
        {
            Helpers.ThrowIfArgumentNull(method);
            if (method.DeclaringType != null && method.DeclaringType.IsGenericType)
            {
                var type = method.DeclaringType.GetGenericTypeDefinition();
                var handle = method.MethodHandle;
                method = MethodBase.GetMethodFromHandle(handle, type.TypeHandle)!;
            }

            return method;
        }

    }
}