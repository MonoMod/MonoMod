using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;

namespace MonoMod.Utils {
    public static class ReflectionHelper {

        public readonly static Dictionary<string, Assembly> AssemblyCache = new Dictionary<string, Assembly>();
        public readonly static Dictionary<MemberReference, MemberInfo> ResolveReflectionCache = new Dictionary<MemberReference, MemberInfo>();

        public static Type ResolveReflection(this TypeReference mref)
            => _ResolveReflection(mref, null) as Type;
        public static MethodBase ResolveReflection(this MethodReference mref)
            => _ResolveReflection(mref, null) as MethodBase;
        public static FieldInfo ResolveReflection(this FieldReference mref)
            => _ResolveReflection(mref, null) as FieldInfo;
        public static PropertyInfo ResolveReflection(this PropertyReference mref)
            => _ResolveReflection(mref, null) as PropertyInfo;
        public static EventInfo ResolveReflection(this EventReference mref)
            => _ResolveReflection(mref, null) as EventInfo;

        public static MemberInfo ResolveReflection(this MemberReference mref)
            => _ResolveReflection(mref, null);

        private static MemberInfo _ResolveReflection(MemberReference mref, Module module) {
            if (ResolveReflectionCache.TryGetValue(mref, out MemberInfo cached) && cached != null)
                return cached;

            Type type;

            // Special cases, f.e. multi-dimensional array type methods.
            if (mref is MethodReference method && mref.DeclaringType is ArrayType) {
                // ArrayType holds special methods.
                type = _ResolveReflection(mref.DeclaringType, module) as Type;
                // ... but all of the methods have the same MetadataToken. We couldn't compare it anyway.

                string methodID = method.GetFindableID(withType: false);
                MethodInfo found = type.GetMethods().First(m => m.GetFindableID(withType: false) == methodID);
                if (found != null)
                    return ResolveReflectionCache[mref] = found;
            }

            TypeReference tscope =
                mref.DeclaringType ??
                mref as TypeReference ??
                throw new NotSupportedException("MemberReference hasn't got a DeclaringType / isn't a TypeReference in itself");

            if (module == null) {
                string asmName;
                string moduleName;

                switch (tscope.Scope) {
                    case AssemblyNameReference asmNameRef:
                        asmName = asmNameRef.FullName;
                        moduleName = null;
                        break;

                    case ModuleDefinition moduleDef:
                        asmName = moduleDef.Assembly.FullName;
                        moduleName = moduleDef.Name;
                        break;

                    case ModuleReference moduleRef:
                        // TODO: Is this correct? It's what cecil itself is doing...
                        asmName = tscope.Module.Assembly.FullName;
                        moduleName = tscope.Module.Name;
                        break;

                    default:
                        throw new NotSupportedException($"Unsupported scope type {tscope.Scope.GetType().FullName}");
                }

                if (!AssemblyCache.TryGetValue(asmName, out Assembly asm))
                    AssemblyCache[asmName] = asm = Assembly.Load(asmName);
                module = string.IsNullOrEmpty(moduleName) ? asm.GetModules()[0] : asm.GetModule(moduleName);
            }

            if (mref is TypeReference tref) {
                if (mref is TypeSpecification ts) {
                    type = _ResolveReflection(ts.ElementType, module) as Type;

                    if (ts.IsByReference)
                        return ResolveReflectionCache[mref] = type.MakeByRefType();

                    if (ts.IsPointer)
                        return ResolveReflectionCache[mref] = type.MakePointerType();

                    if (ts.IsArray)
                        return ResolveReflectionCache[mref] = type.MakeArrayType((ts as ArrayType).Dimensions.Count);

                    if (ts.IsGenericInstance)
                        return ResolveReflectionCache[mref] = type.MakeGenericType((ts as GenericInstanceType).GenericArguments.Select(arg => _ResolveReflection(arg, null) as Type).ToArray());

                } else {
                    type = module.GetType(mref.FullName.Replace("/", "+"));
                }

                return ResolveReflectionCache[mref] = type;
            }

            type = _ResolveReflection(mref.DeclaringType, module) as Type;

            MemberInfo member;

            if (mref is GenericInstanceMethod mrefGenMethod) {
                member = _ResolveReflection(mrefGenMethod.ElementMethod, module);
                member = (member as MethodInfo).MakeGenericMethod(mrefGenMethod.GenericArguments.Select(arg => _ResolveReflection(arg, null) as Type).ToArray());

            } else {
                member = type.GetMembers((BindingFlags) int.MaxValue).FirstOrDefault(m => mref.Is(m));
            }

            return ResolveReflectionCache[mref] = member;
        }

    }
}
