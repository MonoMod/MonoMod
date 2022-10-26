using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;

namespace MonoMod.Utils {
    public sealed class DMDEmitDynamicMethodGenerator : DMDGenerator<DMDEmitDynamicMethodGenerator> {

        private static readonly FieldInfo _DynamicMethod_returnType =
            typeof(DynamicMethod).GetField("returnType", BindingFlags.NonPublic | BindingFlags.Instance) ??
            typeof(DynamicMethod).GetField("m_returnType", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Cannot find returnType fieeld on DynamicMethod");

        protected override MethodInfo GenerateCore(DynamicMethodDefinition dmd, object? context) {
            var orig = dmd.OriginalMethod;
            var def = dmd.Definition ?? throw new InvalidOperationException();

            Type[] argTypes;

            if (orig != null) {
                ParameterInfo[] args = orig.GetParameters();
                var offs = 0;
                if (!orig.IsStatic) {
                    offs++;
                    argTypes = new Type[args.Length + 1];
                    argTypes[0] = orig.GetThisParamType();
                } else {
                    argTypes = new Type[args.Length];
                }
                for (var i = 0; i < args.Length; i++)
                    argTypes[i + offs] = args[i].ParameterType;

            } else {
                var offs = 0;
                if (def.HasThis) {
                    offs++;
                    argTypes = new Type[def.Parameters.Count + 1];
                    Type type = def.DeclaringType.ResolveReflection();
                    if (type.IsValueType)
                        type = type.MakeByRefType();
                    argTypes[0] = type;
                } else {
                    argTypes = new Type[def.Parameters.Count];
                }
                for (var i = 0; i < def.Parameters.Count; i++)
                    argTypes[i + offs] = def.Parameters[i].ParameterType.ResolveReflection();
            }

            var name = dmd.Name ?? $"DMD<{orig?.GetID(simple: true) ?? def.GetID(simple: true)}>";
            Type retType = (orig as MethodInfo)?.ReturnType ?? def.ReturnType.ResolveReflection();

            MMDbgLog.Trace($"new DynamicMethod: {retType} {name}({string.Join(",", argTypes.Select(type => type?.ToString()).ToArray())})");
            if (orig != null)
                MMDbgLog.Trace($"orig: {(orig as MethodInfo)?.ReturnType?.ToString() ?? "NULL"} {orig.Name}({string.Join(",", orig.GetParameters().Select(arg => arg?.ParameterType?.ToString() ?? "NULL").ToArray())})");
            MMDbgLog.Trace($"mdef: {def.ReturnType?.ToString() ?? "NULL"} {name}({string.Join(",", def.Parameters.Select(arg => arg?.ParameterType?.ToString() ?? "NULL").ToArray())})");

            var dm = new DynamicMethod(
                name,
                typeof(void), argTypes,
                orig?.DeclaringType ?? dmd.OwnerType ?? typeof(DynamicMethodDefinition),
                true // If any random errors pop up, try setting this to false first.
            );

            // DynamicMethods don't officially "support" certain return types, such as ByRef types.
            _DynamicMethod_returnType.SetValue(dm, retType);

            ILGenerator il = dm.GetILGenerator();

            _DMDEmit.Generate(dmd, dm, il);

            return dm;
        }

    }
}
