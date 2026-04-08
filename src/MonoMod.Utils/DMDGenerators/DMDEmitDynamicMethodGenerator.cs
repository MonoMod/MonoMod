using MonoMod.Logs;
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace MonoMod.Utils
{
    public sealed class DMDEmitDynamicMethodGenerator : DMDGenerator<DMDEmitDynamicMethodGenerator>
    {

        private static readonly FieldInfo _DynamicMethod_returnType =
            typeof(DynamicMethod).GetField("returnType", BindingFlags.NonPublic | BindingFlags.Instance) ??
            typeof(DynamicMethod).GetField("_returnType", BindingFlags.NonPublic | BindingFlags.Instance) ??
            typeof(DynamicMethod).GetField("m_returnType", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Cannot find returnType field on DynamicMethod");

        protected override MethodInfo GenerateCore(DynamicMethodDefinition dmd, object? context)
        {
            var orig = dmd.OriginalMethod;
            var def = dmd.Definition ?? throw new InvalidOperationException();

            Type[] argTypes;

            /* In case of differing parameters, this branch causes https://github.com/MonoMod/MonoMod/issues/282
            if (orig != null)
            {
                var args = orig.GetParameters();
                var offs = 0;
                if (!orig.IsStatic)
                {
                    offs++;
                    argTypes = new Type[args.Length + 1];
                    argTypes[0] = orig.GetThisParamType();
                }
                else
                {
                    argTypes = new Type[args.Length];
                }
                for (var i = 0; i < args.Length; i++)
                    argTypes[i + offs] = args[i].ParameterType;

            }
            else
            {*/
            var offs = 0;
            if (def.HasThis)
            {
                offs++;
                argTypes = new Type[def.Parameters.Count + 1];
                var type = def.DeclaringType.ResolveReflection();
                if (type.IsValueType)
                    type = type.MakeByRefType();
                argTypes[0] = type;
            }
            else
            {
                argTypes = new Type[def.Parameters.Count];
            }
            for (var i = 0; i < def.Parameters.Count; i++)
                argTypes[i + offs] = def.Parameters[i].ParameterType.ResolveReflection();
            //}

            // we do the (object?) dance using DebugFormatter to avoid internal StringBuilders in the ToString (and GetID) implementations which may cause problems
            var name = dmd.Name ?? DebugFormatter.Format($"DMD<{(object?)orig ?? def.GetID(simple: true)}>");
            var retType = (orig as MethodInfo)?.ReturnType ?? def.ReturnType.ResolveReflection();

            MMDbgLog.Trace($"new DynamicMethod: {retType} {name}({string.Join(",", argTypes.Select(type => type?.ToString()).ToArray())})");
            if (orig != null)
                MMDbgLog.Trace($"orig: {orig}");
            MMDbgLog.Trace($"mdef: {def.ReturnType?.ToString() ?? "NULL"} {name}({string.Join(",", def.Parameters.Select(arg => arg?.ParameterType?.ToString() ?? "NULL").ToArray())})");

            DynamicMethod dm;

            // The runtime only allows certain types to own DynamicMethods; e.g. Mono does not allow Interface- and Array types as owner
            // The only case where this currently causes issues is default implementations for Interface Methods (t.IsInterface is true)
            // Check on Mono: https://github.com/mono/mono/blob/main/mcs/class/corlib/System.Reflection.Emit/DynamicMethod.cs#L116
            // Check on CoreCLR: https://github.com/dotnet/runtime/blob/a6590591ef32ab28632d9bc14efaf0044be728df/src/libraries/System.Private.CoreLib/src/System/Reflection/Emit/DynamicMethod.cs#L271
            if (orig?.DeclaringType?.UnderlyingSystemType is Type t && (t.HasElementType || t.ContainsGenericParameters || t.IsGenericParameter || t.IsInterface))
            {
                dm = new DynamicMethod(
                    name,
                    typeof(void), argTypes,
                    t.Module,
                    true
                );
            }
            else
            {
                dm = new DynamicMethod(
                    name,
                    typeof(void), argTypes,
                    orig?.DeclaringType ?? typeof(DynamicMethodDefinition),
                    true // If any random errors pop up, try setting this to false first.
                );
            }

            // DynamicMethods don't officially "support" certain return types, such as ByRef types.
            _DynamicMethod_returnType.SetValue(dm, retType);

            var il = dm.GetILGenerator();

            _DMDEmit.Generate(dmd, dm, il);

            return dm;
        }

    }
}
