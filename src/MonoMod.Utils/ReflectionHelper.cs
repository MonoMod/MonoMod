#if NETFRAMEWORK
#define HAS_UNMANAGED_METHODSIGHELPER
#endif

using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using AssemblyHashAlgorithm = Mono.Cecil.AssemblyHashAlgorithm;
using ManagedCC = System.Reflection.CallingConventions;
using UnmanagedCC = System.Runtime.InteropServices.CallingConvention;

namespace MonoMod.Utils
{
    public static partial class ReflectionHelper
    {

        internal static readonly bool IsCoreBCL =
            typeof(object).Assembly.GetName().Name == "System.Private.CoreLib";

        internal static readonly Dictionary<string, WeakReference/*<Assembly>*/> AssemblyCache = new Dictionary<string, WeakReference>();
        internal static readonly Dictionary<string, WeakReference/*<Assembly>*/[]> AssembliesCache = new Dictionary<string, WeakReference[]>();
        internal static readonly Dictionary<string, WeakReference/*<MemberInfo>*/> ResolveReflectionCache = new Dictionary<string, WeakReference>();

        public readonly static byte[] AssemblyHashPrefix = new UTF8Encoding(false).GetBytes("MonoModRefl").Concat(new byte[1]).ToArray();
        public readonly static string AssemblyHashNameTag = "@#";

        private const BindingFlags _BindingFlagsAll = (BindingFlags)(-1);

        private static MemberInfo _Cache(string cacheKey, MemberInfo value)
        {
            if (cacheKey != null && value == null)
            {
                MMDbgLog.Error($"ResolveRefl failure: {cacheKey}");
            }
            if (cacheKey != null && value != null)
            {
                lock (ResolveReflectionCache)
                {
                    ResolveReflectionCache[cacheKey] = new WeakReference(value);
                }
            }
            return value!;
        }

        public static Assembly Load(ModuleDefinition module)
        {
            Helpers.ThrowIfArgumentNull(module);
            using (var stream = new MemoryStream())
            {
                module.Write(stream);
                stream.Seek(0, SeekOrigin.Begin);
                return Load(stream);
            }
        }

        public static Assembly Load(Stream stream)
        {
            Helpers.ThrowIfArgumentNull(stream);
            Assembly asm;

            if (stream is MemoryStream ms)
            {
                asm = Assembly.Load(ms.GetBuffer());
            }
            else
            {
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    copy.Seek(0, SeekOrigin.Begin);
                    asm = Assembly.Load(copy.GetBuffer());
                }
            }

            AppDomain.CurrentDomain.AssemblyResolve +=
                (s, e) => e.Name == asm.FullName ? asm : null;

            return asm;
        }

        public static Type? GetType(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var type = Type.GetType(name);
            if (type != null)
                return type;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(name);
                if (type != null)
                    return type;
            }

            return null;
        }

        public static void ApplyRuntimeHash(this AssemblyNameReference asmRef, Assembly asm)
        {
            Helpers.ThrowIfArgumentNull(asmRef);
            Helpers.ThrowIfArgumentNull(asm);
            // Mono.Cecil ignores the hash for the most part, allowing us to store whatever we want in it.
            var hash = new byte[AssemblyHashPrefix.Length + 4];
            Array.Copy(AssemblyHashPrefix, 0, hash, 0, AssemblyHashPrefix.Length);
            Array.Copy(BitConverter.GetBytes(asm.GetHashCode()), 0, hash, AssemblyHashPrefix.Length, 4);
            asmRef.HashAlgorithm = unchecked((AssemblyHashAlgorithm)(-1));
            asmRef.Hash = hash;
        }

        public static string GetRuntimeHashedFullName(this Assembly asm)
        {
            Helpers.ThrowIfArgumentNull(asm);
            return $"{asm.FullName}{AssemblyHashNameTag}{asm.GetHashCode()}";
        }

        public static string GetRuntimeHashedFullName(this AssemblyNameReference asm)
        {
            Helpers.ThrowIfArgumentNull(asm);
            if (asm.HashAlgorithm != unchecked((AssemblyHashAlgorithm)(-1)))
                return asm.FullName;

            var hash = asm.Hash;
            if (hash.Length != AssemblyHashPrefix.Length + 4)
                return asm.FullName;

            for (var i = 0; i < AssemblyHashPrefix.Length; i++)
                if (hash[i] != AssemblyHashPrefix[i])
                    return asm.FullName;

            return $"{asm.FullName}{AssemblyHashNameTag}{BitConverter.ToInt32(hash, AssemblyHashPrefix.Length)}";
        }

        public static Type ResolveReflection(this TypeReference mref)
            => (Type)_ResolveReflection(mref, null);
        public static MethodBase ResolveReflection(this MethodReference mref)
            => (MethodBase)_ResolveReflection(mref, null);
        public static FieldInfo ResolveReflection(this FieldReference mref)
            => (FieldInfo)_ResolveReflection(mref, null);
        public static PropertyInfo ResolveReflection(this PropertyReference mref)
            => (PropertyInfo)_ResolveReflection(mref, null);
        public static EventInfo ResolveReflection(this EventReference mref)
            => (EventInfo)_ResolveReflection(mref, null);

        public static MemberInfo ResolveReflection(this MemberReference mref)
            => _ResolveReflection(mref, null);

        [return: NotNullIfNotNull("mref")]
        [SuppressMessage("Performance", "CA1846:Prefer 'AsSpan' over 'Substring'",
            Justification = "Span overloads are not available on some targets this must compile for.")]
        private static MemberInfo? _ResolveReflection(MemberReference? mref, Module[]? modules)
        {
            if (mref == null)
                return null;

            if (mref is DynamicMethodReference dmref)
                return dmref.DynamicMethod;

            var cacheKey = (mref as MethodReference)?.GetID() ?? mref.FullName;

            /* Even though member references are not supposed to exist more
             * than once, some environments (f.e. tModLoader) reload updated
             * versions of assemblies with differing assembly names.
             * 
             * Adding the assembly name should take care of preventing any further
             * "accidental" collisions that would not occur "normally".
             * 
             * Ideally the mref hash code could be part of the cache key,
             * but that part changes with every new Cecil reference context, if not
             * even more often than that.
             */

            var tscope =
                mref.DeclaringType ??
                mref as TypeReference ??
                null;

            string? asmName;
            string? moduleName;

            switch (tscope?.Scope)
            {
                case AssemblyNameReference asmNameRef:
                    asmName = asmNameRef.GetRuntimeHashedFullName();
                    moduleName = null;
                    break;

                case ModuleDefinition moduleDef:
                    asmName = moduleDef.Assembly.Name.GetRuntimeHashedFullName();
                    moduleName = moduleDef.Name;
                    break;

                case ModuleReference moduleRef:
                    // TODO: Is this correct? It's what cecil itself is doing...
                    asmName = tscope.Module.Assembly.Name.GetRuntimeHashedFullName();
                    moduleName = tscope.Module.Name;
                    break;

                case null:
                default:
                    asmName = null;
                    moduleName = null;
                    break;
            }

            cacheKey = $"{cacheKey} | {asmName ?? "NOASSEMBLY"}, {moduleName ?? "NOMODULE"}";

            lock (ResolveReflectionCache)
            {
                if (ResolveReflectionCache.TryGetValue(cacheKey, out var cachedRef) &&
                    cachedRef != null && cachedRef.SafeGetTarget() is MemberInfo cached)
                    return cached;
            }

            Type? type;

            // Special cases.
            if (mref is GenericParameter genParam)
            {
                // TODO: Handle GenericParameter in ResolveReflection.
                throw new NotSupportedException("ResolveReflection on GenericParameter currently not supported");
            }

            if (mref is MethodReference method && mref.DeclaringType is ArrayType)
            {
                // ArrayType holds special methods.
                type = (Type)_ResolveReflection(mref.DeclaringType, modules);
                // ... but all of the methods have the same MetadataToken. We couldn't compare it anyway.

                var methodID = method.GetID(withType: false);
                var found =
                    type.GetMethods(_BindingFlagsAll).Cast<MethodBase>()
                    .Concat(type.GetConstructors(_BindingFlagsAll))
                    .FirstOrDefault(m => m.GetID(withType: false) == methodID);
                if (found != null)
                    return _Cache(cacheKey, found);
            }


            // Typeless references aren't supported.
            if (tscope == null)
                throw new ArgumentException("MemberReference hasn't got a DeclaringType / isn't a TypeReference in itself");
            if (asmName == null && moduleName == null)
                throw new NotSupportedException($"Unsupported scope type {tscope.Scope!.GetType().FullName}");

            var tryAssemblyCache = true;
            var refetchingModules = false;
            var nullifyModules = false;

            goto FetchModules;

            RefetchModules:
            refetchingModules = true;

            FetchModules:

            if (nullifyModules)
                modules = null;
            nullifyModules = true;

            if (modules == null)
            {
                Assembly[]? asms = null;

                if (tryAssemblyCache && refetchingModules)
                {
                    refetchingModules = false;
                    tryAssemblyCache = false;
                }

                if (tryAssemblyCache)
                    lock (AssemblyCache)
                        if (AssemblyCache.TryGetValue(asmName!, out var asmRef) &&
                            asmRef.SafeGetTarget() is Assembly asm)
                            asms = new Assembly[] { asm };

                if (asms == null)
                {
                    if (!refetchingModules)
                        lock (AssembliesCache)
                            if (AssembliesCache.TryGetValue(asmName!, out var asmRefs))
                                asms = asmRefs
                                    .Select(asmRef => asmRef.SafeGetTarget() as Assembly)
                                    .Where(asm => asm != null)
                                    .ToArray()!;
                }

                if (asms == null)
                {
                    /* Assembly load contexts are pain.
                     * Let's try things in the following order:
                     * - If a possible embedded hash code exists, check by hash code.
                     * - Check by full name.
                     * - Check by short name.
                     * - Try to load the assembly.
                     * - Give up.
                     *
                     * The hash code could be extracted from the cecil assembly name reference's Hash property,
                     * but maybe it'll be necessary to be passed via the name in the future once ^ gets used by Cecil.
                     * In addition to that, the hash must become part of the cache key string anyway.
                     * This can be microoptimized when necessary.
                     * - ade
                     */

                    var split = asmName!.IndexOf(AssemblyHashNameTag, StringComparison.Ordinal);
                    if (split != -1 && int.TryParse(asmName.Substring(split + 2), out var hash))
                    {
                        asms = AppDomain.CurrentDomain.GetAssemblies().Where(other => other.GetHashCode() == hash).ToArray();
                        if (asms.Length == 0)
                            asms = null;
                        asmName = asmName.Substring(0, split);
                    }

                    if (asms == null)
                    {
                        asms = AppDomain.CurrentDomain.GetAssemblies().Where(other => other.GetName().FullName == asmName).ToArray();
                        if (asms.Length == 0)
                            asms = AppDomain.CurrentDomain.GetAssemblies().Where(other => other.GetName().Name == asmName).ToArray();

                        if (asms.Length == 0 && Assembly.Load(new AssemblyName(asmName)) is Assembly loaded)
                            asms = new Assembly[] { loaded };
                    }

                    if (asms.Length != 0)
                        lock (AssembliesCache)
                            AssembliesCache[asmName] = asms.Select(asm => new WeakReference(asm)).ToArray();
                }

                modules =
                    (string.IsNullOrEmpty(moduleName) ?
                        asms.SelectMany(asm => asm.GetModules()) :
                        asms.Select(asm => asm.GetModule(moduleName))
                    ).Where(mod => mod != null).ToArray()!;

                if (modules.Length == 0)
                    throw new MissingMemberException($"Cannot resolve assembly / module {asmName} / {moduleName}");
            }

            if (mref is TypeReference tref)
            {
                if (tref.FullName == "<Module>")
                    throw new ArgumentException("Type <Module> cannot be resolved to a runtime reflection type");

                if (mref is TypeSpecification ts)
                {
                    type = (Type)_ResolveReflection(ts.ElementType, null);

                    if (ts.IsByReference)
                        return _Cache(cacheKey, type.MakeByRefType());

                    if (ts.IsPointer)
                        return _Cache(cacheKey, type.MakePointerType());

                    if (ts.IsArray)
                        return _Cache(cacheKey, ((ArrayType)ts).IsVector ? type.MakeArrayType() : type.MakeArrayType(((ArrayType)ts).Dimensions.Count));

                    if (ts.IsGenericInstance)
                        return _Cache(cacheKey, type.MakeGenericType(((GenericInstanceType)ts).GenericArguments.Select(arg => _ResolveReflection(arg, null) as Type).ToArray()!));

                }
                else
                {
                    type = modules
                        .Select(module => module.GetType(mref.FullName.Replace("/", "+", StringComparison.Ordinal), false, false))
                        .FirstOrDefault(m => m != null);
                    if (type == null)
                        type = modules
                            .Select(module => module.GetTypes().FirstOrDefault(m => mref.Is(m)))
                            .FirstOrDefault(m => m != null);
                    if (type == null && !refetchingModules)
                        goto RefetchModules;
                }

                return _Cache(cacheKey, type!);
            }

            var typeless = mref.DeclaringType?.FullName == "<Module>";

            MemberInfo? member;

            if (mref is GenericInstanceMethod mrefGenMethod)
            {
                member = _ResolveReflection(mrefGenMethod.ElementMethod, modules);
                member = (member as MethodInfo)?.MakeGenericMethod(mrefGenMethod.GenericArguments.Select(arg => _ResolveReflection(arg, null) as Type).ToArray()!);
            }
            else if (typeless)
            {
                if (mref is MethodReference)
                    member = modules
                        .Select(module => module.GetMethods(_BindingFlagsAll).FirstOrDefault(m => mref.Is(m)))
                        .FirstOrDefault(m => m != null);
                else if (mref is FieldReference)
                    member = modules
                        .Select(module => module.GetFields(_BindingFlagsAll).FirstOrDefault(m => mref.Is(m)))
                        .FirstOrDefault(m => m != null);
                else
                    throw new NotSupportedException($"Unsupported <Module> member type {mref.GetType().FullName}");

            }
            else
            {
                var declType = (Type?)_ResolveReflection(mref.DeclaringType, modules);

                if (mref is MethodReference)
                    member = declType!
                        .GetMethods(_BindingFlagsAll).Cast<MethodBase>()
                        .Concat(declType.GetConstructors(_BindingFlagsAll))
                        .FirstOrDefault(m => mref.Is(m));
                else if (mref is FieldReference)
                    member = declType!
                        .GetFields(_BindingFlagsAll)
                        .FirstOrDefault(m => mref.Is(m));
                else
                    member = declType!
                        .GetMembers(_BindingFlagsAll)
                        .FirstOrDefault(m => mref.Is(m));
            }

            if (member == null && !refetchingModules)
                goto RefetchModules;

            return _Cache(cacheKey, member!);
        }

        private delegate SignatureHelper GetUnmanagedSigHelperDelegate(Module? module, UnmanagedCC callConv, Type? returnType);
#if HAS_UNMANAGED_METHODSIGHELPER
        private static readonly GetUnmanagedSigHelperDelegate GetUnmanagedSigHelper = SignatureHelper.GetMethodSigHelper;
#else
        // on CoreCLR, the method is present, but not public
        private static readonly MethodInfo? GetUnmanagedSigHelperMethod
            = typeof(SignatureHelper).GetMethod(nameof(SignatureHelper.GetMethodSigHelper), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(Module), typeof(UnmanagedCC), typeof(Type) }, null);
        private static readonly GetUnmanagedSigHelperDelegate GetUnmanagedSigHelper
            = GetUnmanagedSigHelperMethod?.TryCreateDelegate<GetUnmanagedSigHelperDelegate>()
            ?? ((_, _, _) => throw new NotImplementedException("Unmanaged calling conventions are not supported"));
#endif

        public static SignatureHelper ResolveReflection(this CallSite csite, Module context)
            => ResolveReflectionSignature(csite, context);
        public static SignatureHelper ResolveReflectionSignature(this IMethodSignature csite, Module context)
        {
            Helpers.ThrowIfArgumentNull(csite);
            Helpers.ThrowIfArgumentNull(context);
            SignatureHelper shelper;
            switch (csite.CallingConvention)
            {
                case MethodCallingConvention.C:
                    shelper = GetUnmanagedSigHelper(context, UnmanagedCC.Cdecl, csite.ReturnType.ResolveReflection());
                    break;

                case MethodCallingConvention.StdCall:
                    shelper = GetUnmanagedSigHelper(context, UnmanagedCC.StdCall, csite.ReturnType.ResolveReflection());
                    break;

                case MethodCallingConvention.ThisCall:
                    shelper = GetUnmanagedSigHelper(context, UnmanagedCC.ThisCall, csite.ReturnType.ResolveReflection());
                    break;

                case MethodCallingConvention.FastCall:
                    shelper = GetUnmanagedSigHelper(context, UnmanagedCC.FastCall, csite.ReturnType.ResolveReflection());
                    break;

                case MethodCallingConvention.VarArg:
                    shelper = SignatureHelper.GetMethodSigHelper(context, ManagedCC.VarArgs, csite.ReturnType.ResolveReflection());
                    break;

                default:
                    if (csite.ExplicitThis)
                    {
                        shelper = SignatureHelper.GetMethodSigHelper(context, ManagedCC.ExplicitThis, csite.ReturnType.ResolveReflection());
                    }
                    else
                    {
                        shelper = SignatureHelper.GetMethodSigHelper(context, ManagedCC.Standard, csite.ReturnType.ResolveReflection());
                    }
                    break;
            }

            if (context != null)
            {
                var modReq = new List<Type>();
                var modOpt = new List<Type>();

                foreach (var param in csite.Parameters)
                {
                    if (param.ParameterType.IsSentinel)
                        shelper.AddSentinel();

                    if (param.ParameterType.IsPinned)
                    {
                        shelper.AddArgument(param.ParameterType.ResolveReflection(), true);
                        continue;
                    }

                    modOpt.Clear();
                    modReq.Clear();

                    for (
                        var paramTypeRef = param.ParameterType;
                        paramTypeRef is TypeSpecification paramTypeSpec;
                        paramTypeRef = paramTypeSpec.ElementType
                    )
                    {
                        switch (paramTypeRef)
                        {
                            case RequiredModifierType paramTypeModReq:
                                modReq.Add(paramTypeModReq.ModifierType.ResolveReflection());
                                break;

                            case OptionalModifierType paramTypeOptReq:
                                modOpt.Add(paramTypeOptReq.ModifierType.ResolveReflection());
                                break;
                        }
                    }

                    shelper.AddArgument(param.ParameterType.ResolveReflection(), modReq.ToArray(), modOpt.ToArray());
                }

            }
            else
            {
                foreach (var param in csite.Parameters)
                {
                    shelper.AddArgument(param.ParameterType.ResolveReflection());
                }
            }

            return shelper;
        }

    }
}
