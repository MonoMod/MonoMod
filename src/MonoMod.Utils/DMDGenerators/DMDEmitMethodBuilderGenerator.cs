﻿#if NETFRAMEWORK
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MonoMod.Logs;

namespace MonoMod.Utils {
    public sealed class DMDEmitMethodBuilderGenerator : DMDGenerator<DMDEmitMethodBuilderGenerator> {

        private static readonly bool _MBCanRunAndCollect = Enum.IsDefined(typeof(AssemblyBuilderAccess), "RunAndCollect");

        protected override MethodInfo GenerateCore(DynamicMethodDefinition dmd, object? context) {
            var typeBuilder = context as TypeBuilder;
            var method = GenerateMethodBuilder(dmd, typeBuilder);
            typeBuilder = (TypeBuilder) method.DeclaringType;
            var type = typeBuilder.CreateType();
            var dumpPath = Switches.TryGetSwitchValue(Switches.DMDDumpTo, out var dumpToVal) ? dumpToVal as string : null;
            if (!string.IsNullOrEmpty(dumpPath)) {
                var path = method.Module.FullyQualifiedName;
                var name = Path.GetFileName(path);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                if (File.Exists(path))
                    File.Delete(path);
                ((AssemblyBuilder) typeBuilder.Assembly).Save(name);
            }
            return type.GetMethod(method.Name, BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        }

        public static MethodBuilder GenerateMethodBuilder(DynamicMethodDefinition dmd, TypeBuilder? typeBuilder) {
            Helpers.ThrowIfArgumentNull(dmd);
            var orig = dmd.OriginalMethod;
            var def = dmd.Definition;

            if (typeBuilder == null) {
                var dumpDir = Switches.TryGetSwitchValue(Switches.DMDDumpTo, out var dumpToVal) ? dumpToVal as string : null;
                if (string.IsNullOrEmpty(dumpDir)) {
                    dumpDir = null;
                } else {
                    dumpDir = Path.GetFullPath(dumpDir);
                }
                var collect = string.IsNullOrEmpty(dumpDir) && _MBCanRunAndCollect;
                var ab = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName() {
                        Name = dmd.GetDumpName("MethodBuilder")
                    },
                    collect ? (AssemblyBuilderAccess) 9 : AssemblyBuilderAccess.RunAndSave,
                    dumpDir
                );

                ab.SetCustomAttribute(new CustomAttributeBuilder(DynamicMethodDefinition.c_UnverifiableCodeAttribute, []));

                if (dmd.Debug) {
                    ab.SetCustomAttribute(new CustomAttributeBuilder(DynamicMethodDefinition.c_DebuggableAttribute, [
                        DebuggableAttribute.DebuggingModes.DisableOptimizations | DebuggableAttribute.DebuggingModes.Default
                    ]));
                }

                // Note: Debugging can fail on mono if Mono.CompilerServices.SymbolWriter.dll cannot be found,
                // or if Mono.CompilerServices.SymbolWriter.SymbolWriterImpl can't be found inside of that.
                // https://github.com/mono/mono/blob/f879e35e3ed7496d819bd766deb8be6992d068ed/mcs/class/corlib/System.Reflection.Emit/ModuleBuilder.cs#L146
                var module = ab.DefineDynamicModule($"{ab.GetName().Name}.dll", $"{ab.GetName().Name}.dll", dmd.Debug);
                typeBuilder = module.DefineType(
                    DebugFormatter.Format($"DMD<{orig}>?{dmd.GetHashCode()}"),
                    System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Class
                );
            }

            Type[] argTypes;
            Type[][] argTypesModReq;
            Type[][] argTypesModOpt;

            if (orig != null) {
                var args = orig.GetParameters();
                var offs = 0;
                if (!orig.IsStatic) {
                    offs++;
                    argTypes = new Type[args.Length + 1];
                    argTypesModReq = new Type[args.Length + 1][];
                    argTypesModOpt = new Type[args.Length + 1][];
                    argTypes[0] = orig.GetThisParamType();
                    argTypesModReq[0] = Type.EmptyTypes;
                    argTypesModOpt[0] = Type.EmptyTypes;
                } else {
                    argTypes = new Type[args.Length];
                    argTypesModReq = new Type[args.Length][];
                    argTypesModOpt = new Type[args.Length][];
                }

                for (var i = 0; i < args.Length; i++) {
                    argTypes[i + offs] = args[i].ParameterType;
                    argTypesModReq[i + offs] = args[i].GetRequiredCustomModifiers();
                    argTypesModOpt[i + offs] = args[i].GetOptionalCustomModifiers();
                }

            } else {
                var offs = 0;
                if (def.HasThis) {
                    offs++;
                    argTypes = new Type[def.Parameters.Count + 1];
                    argTypesModReq = new Type[def.Parameters.Count + 1][];
                    argTypesModOpt = new Type[def.Parameters.Count + 1][];
                    var type = def.DeclaringType.ResolveReflection();
                    if (type.IsValueType)
                        type = type.MakeByRefType();
                    argTypes[0] = type;
                    argTypesModReq[0] = Type.EmptyTypes;
                    argTypesModOpt[0] = Type.EmptyTypes;
                } else {
                    argTypes = new Type[def.Parameters.Count];
                    argTypesModReq = new Type[def.Parameters.Count][];
                    argTypesModOpt = new Type[def.Parameters.Count][];
                }

                var modReq = new List<Type>();
                var modOpt = new List<Type>();

                for (var i = 0; i < def.Parameters.Count; i++) {
                    _DMDEmit.ResolveWithModifiers(def.Parameters[i].ParameterType, out var paramType, out var paramTypeModReq, out var paramTypeModOpt, modReq, modOpt);
                    argTypes[i + offs] = paramType;
                    argTypesModReq[i + offs] = paramTypeModReq;
                    argTypesModOpt[i + offs] = paramTypeModOpt;
                }
            }

            // Required because the return type modifiers aren't easily accessible via reflection.
            _DMDEmit.ResolveWithModifiers(def.ReturnType, out var returnType, out var returnTypeModReq, out var returnTypeModOpt);

            var mb = typeBuilder.DefineMethod(
                dmd.Name ?? (orig?.Name ?? def.Name).Replace('.', '_'),
                System.Reflection.MethodAttributes.HideBySig | System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
                CallingConventions.Standard,
                returnType, returnTypeModReq, returnTypeModOpt,
                argTypes, argTypesModReq, argTypesModOpt
            );
            var il = mb.GetILGenerator();

            _DMDEmit.Generate(dmd, mb, il);

            return mb;
        }

    }
}
#endif
