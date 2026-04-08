#if NETFRAMEWORK || NET9_0_OR_GREATER
using MonoMod.Logs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

namespace MonoMod.Utils
{
    public sealed class DMDEmitMethodBuilderGenerator : DMDGenerator<DMDEmitMethodBuilderGenerator>
    {

        private static readonly bool _MBCanRunAndCollect = Enum.IsDefined(typeof(AssemblyBuilderAccess), "RunAndCollect");

        protected override MethodInfo GenerateCore(DynamicMethodDefinition dmd, object? context)
        {
            var typeBuilder = context as TypeBuilder;
            var method = GenerateMethodBuilder(dmd, typeBuilder);
            typeBuilder = (TypeBuilder)method.DeclaringType!;
            var type = typeBuilder.CreateType();
            var dumpPath = Switches.TryGetSwitchValue(Switches.DMDDumpTo, out var dumpToVal) ? dumpToVal as string : null;
            if (!string.IsNullOrEmpty(dumpPath))
            {
#if NETFRAMEWORK
                var path = method.Module.FullyQualifiedName;
                var name = Path.GetFileName(path);
                var dir = Path.GetDirectoryName(path);
#else
                var dir = Path.GetFullPath(dumpPath);
                var path = Path.Combine(dir, method.Module.ScopeName);
#endif
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                if (File.Exists(path))
                    File.Delete(path);

#if NETFRAMEWORK
                ((AssemblyBuilder)typeBuilder.Assembly).Save(name);
#elif NET9_0_OR_GREATER
                using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
                ((PersistedAssemblyBuilder)typeBuilder.Assembly).Save(stream);
                stream.Seek(0, SeekOrigin.Begin);
                type = ReflectionHelper.Load(stream).GetType(typeBuilder.FullName!, true, false)!;
#endif
            }
            return type.GetMethod(method.Name, BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find generated method");
        }

        public static MethodBuilder GenerateMethodBuilder(DynamicMethodDefinition dmd, TypeBuilder? typeBuilder)
        {
            Helpers.ThrowIfArgumentNull(dmd);
            var orig = dmd.OriginalMethod;
            var def = dmd.Definition;

            if (typeBuilder == null)
            {
                AssemblyBuilder ab;
                var dumpDir = Switches.TryGetSwitchValue(Switches.DMDDumpTo, out var dumpToVal) ? dumpToVal as string : null;
#if NETFRAMEWORK
                if (string.IsNullOrEmpty(dumpDir))
                {
                    dumpDir = null;
                }
                else
                {
                    dumpDir = Path.GetFullPath(dumpDir);
                }
                var collect = string.IsNullOrEmpty(dumpDir) && _MBCanRunAndCollect;
                ab = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName()
                    {
                        Name = dmd.GetDumpName("MethodBuilder")
                    },
                    collect ? (AssemblyBuilderAccess)9 : AssemblyBuilderAccess.RunAndSave,
                    dumpDir
                );
#elif NET9_0_OR_GREATER
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    ab = new PersistedAssemblyBuilder(
                        new AssemblyName()
                        {
                            Name = dmd.GetDumpName("MethodBuilder")
                        },
                        typeof(object).Assembly
                    );
                }
                else
                {
                    ab = AssemblyBuilder.DefineDynamicAssembly(
                        new AssemblyName()
                        {
                            Name = dmd.GetDumpName("MethodBuilder")
                        },
                        _MBCanRunAndCollect ? (AssemblyBuilderAccess)9 : AssemblyBuilderAccess.Run
                    );
                }
#endif

                ab.SetCustomAttribute(new CustomAttributeBuilder(DynamicMethodDefinition.c_UnverifiableCodeAttribute, []));

                if (dmd.Debug)
                {
                    ab.SetCustomAttribute(new CustomAttributeBuilder(DynamicMethodDefinition.c_DebuggableAttribute, [
                        DebuggableAttribute.DebuggingModes.DisableOptimizations | DebuggableAttribute.DebuggingModes.Default
                    ]));
                }

#if NETFRAMEWORK
                // Note: Debugging can fail on mono if Mono.CompilerServices.SymbolWriter.dll cannot be found,
                // or if Mono.CompilerServices.SymbolWriter.SymbolWriterImpl can't be found inside of that.
                // https://github.com/mono/mono/blob/f879e35e3ed7496d819bd766deb8be6992d068ed/mcs/class/corlib/System.Reflection.Emit/ModuleBuilder.cs#L146
                var module = ab.DefineDynamicModule($"{ab.GetName().Name}.dll", $"{ab.GetName().Name}.dll", dmd.Debug);
#else
                var module = ab.DefineDynamicModule($"{ab.GetName().Name}.dll");
#endif
                typeBuilder = module.DefineType(
                    DebugFormatter.Format($"DMD<{orig}>?{dmd.GetHashCode()}"),
                    System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Class
                );
            }

            Type[] argTypes;
            Type[][] argTypesModReq;
            Type[][] argTypesModOpt;

            /* In case of differing parameters, this branch causes https://github.com/MonoMod/MonoMod/issues/282
            if (orig != null)
            {
                var args = orig.GetParameters();
                var offs = 0;
                if (!orig.IsStatic)
                {
                    offs++;
                    argTypes = new Type[args.Length + 1];
                    argTypesModReq = new Type[args.Length + 1][];
                    argTypesModOpt = new Type[args.Length + 1][];
                    argTypes[0] = orig.GetThisParamType();
                    argTypesModReq[0] = Type.EmptyTypes;
                    argTypesModOpt[0] = Type.EmptyTypes;
                }
                else
                {
                    argTypes = new Type[args.Length];
                    argTypesModReq = new Type[args.Length][];
                    argTypesModOpt = new Type[args.Length][];
                }

                for (var i = 0; i < args.Length; i++)
                {
                    argTypes[i + offs] = args[i].ParameterType;
                    argTypesModReq[i + offs] = args[i].GetRequiredCustomModifiers();
                    argTypesModOpt[i + offs] = args[i].GetOptionalCustomModifiers();
                }

            }
            else
            {*/
            var offs = 0;
            if (def.HasThis)
            {
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
            }
            else
            {
                argTypes = new Type[def.Parameters.Count];
                argTypesModReq = new Type[def.Parameters.Count][];
                argTypesModOpt = new Type[def.Parameters.Count][];
            }

            var modReq = new List<Type>();
            var modOpt = new List<Type>();

            for (var i = 0; i < def.Parameters.Count; i++)
            {
                _DMDEmit.ResolveWithModifiers(def.Parameters[i].ParameterType, out var paramType, out var paramTypeModReq, out var paramTypeModOpt, modReq, modOpt);
                argTypes[i + offs] = paramType;
                argTypesModReq[i + offs] = paramTypeModReq;
                argTypesModOpt[i + offs] = paramTypeModOpt;
            }
            //}

            // Required because the return type modifiers aren't easily accessible via reflection.
            _DMDEmit.ResolveWithModifiers(def.ReturnType, out var returnType, out var returnTypeModReq, out var returnTypeModOpt);


#if NETFRAMEWORK
            // https://github.com/MonoMod/MonoMod/issues/299
            // https://github.com/mono/mono/blob/0f53e9e151d92944cacab3e24ac359410c606df6/mono/metadata/sre-encode.c#L290
            if (PlatformDetection.Runtime == RuntimeKind.Mono)
            {
                SanitizeForMono(ref returnTypeModReq);
                SanitizeForMono(ref returnTypeModOpt);
                SanitizeForMono(ref argTypesModReq);
                SanitizeForMono(ref argTypesModOpt);
            }
#endif

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
#if NETFRAMEWORK
        private static void SanitizeForMono(ref Type[] toSanitize)
        {
            if (toSanitize.Length == 0)
            {
                toSanitize = null!;
            }
        }
        private static void SanitizeForMono(ref Type[][] toSanitize)
        {
            if (toSanitize.Length == 0)
            {
                toSanitize = null!;
            } 
            else
            {
                for (var i = 0; i < toSanitize.Length; i++)
                {
                    if (toSanitize[i].Length == 0)
                    {
                        toSanitize[i] = null!;
                    }
                }
            }
        }
#endif
    }
}
#endif
