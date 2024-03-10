using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Security;
#if NETFRAMEWORK
using System.Linq;
#endif

namespace MonoMod.Utils
{
    public sealed partial class DynamicMethodDefinition : IDisposable
    {

        static DynamicMethodDefinition()
        {
            _InitCopier();
        }

        internal static readonly bool _IsNewMonoSRE = PlatformDetection.Runtime is RuntimeKind.Mono
            && typeof(DynamicMethod).GetField("il_info", BindingFlags.NonPublic | BindingFlags.Instance) != null;
        internal static readonly bool _IsOldMonoSRE = PlatformDetection.Runtime is RuntimeKind.Mono
            && !_IsNewMonoSRE && typeof(DynamicMethod).GetField("ilgen", BindingFlags.NonPublic | BindingFlags.Instance) != null;

        // If SRE has been stubbed out, prefer Cecil.
        private static bool _PreferCecil =
            (PlatformDetection.Runtime is RuntimeKind.Mono && (
                // Mono 4.X+
                !_IsNewMonoSRE &&
                // Unity pre 2018
                !_IsOldMonoSRE
            )) ||

            (PlatformDetection.Runtime is not RuntimeKind.Mono && (
                // .NET
                typeof(ILGenerator).Assembly
                .GetType("System.Reflection.Emit.DynamicILGenerator")
                ?.GetField("m_scope", BindingFlags.NonPublic | BindingFlags.Instance) == null
            )) ||

            false;

        public static bool IsDynamicILAvailable => !_PreferCecil;

        internal static readonly ConstructorInfo c_DebuggableAttribute = typeof(DebuggableAttribute).GetConstructor(new Type[] { typeof(DebuggableAttribute.DebuggingModes) })!;
        internal static readonly ConstructorInfo c_UnverifiableCodeAttribute = typeof(UnverifiableCodeAttribute).GetConstructor(ArrayEx.Empty<Type>())!;
        internal static readonly ConstructorInfo c_IgnoresAccessChecksToAttribute = typeof(System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute).GetConstructor(new[] { typeof(string) })!;

        internal static readonly Type t__IDMDGenerator = typeof(IDMDGenerator);
        internal static readonly ConcurrentDictionary<string, IDMDGenerator> _DMDGeneratorCache = new();

        public MethodBase? OriginalMethod { get; }
        public MethodDefinition Definition { get; }
        public ModuleDefinition Module { get; }

        public string? Name { get; }

        public bool Debug { get; init; }

        private Guid GUID = Guid.NewGuid();

        private bool isDisposed;

        private static bool GetDefaultDebugValue()
        {
            return Switches.TryGetSwitchEnabled(Switches.DMDDebug, out var value) && value;
        }

        public DynamicMethodDefinition(MethodBase method)
        {
            Helpers.ThrowIfArgumentNull(method);

            OriginalMethod = method;
            Debug = GetDefaultDebugValue();

            LoadFromMethod(method, out var module, out var definition);
            Module = module;
            Definition = definition;
        }

        public DynamicMethodDefinition(string name, Type? returnType, Type[] parameterTypes)
        {
            Helpers.ThrowIfArgumentNull(name);
            Helpers.ThrowIfArgumentNull(parameterTypes);

            Name = name;
            OriginalMethod = null;
            Debug = GetDefaultDebugValue();

            _CreateDynModule(name, returnType, parameterTypes, out var module, out var definition);
            Module = module;
            Definition = definition;
        }

        [MemberNotNull(nameof(Definition))]
        public ILProcessor GetILProcessor()
        {
            if (Definition is null)
                throw new InvalidOperationException();
            return Definition.Body.GetILProcessor();
        }

        [MemberNotNull(nameof(Definition))]
        public ILGenerator GetILGenerator()
        {
            if (Definition is null)
                throw new InvalidOperationException();
            return new Cil.CecilILGenerator(Definition.Body.GetILProcessor()).GetProxy();
        }

        private void _CreateDynModule(string name, Type? returnType, Type[] parameterTypes, out ModuleDefinition Module, out MethodDefinition Definition)
        {
            var module = Module = ModuleDefinition.CreateModule($"DMD:DynModule<{name}>?{GetHashCode()}", new ModuleParameters()
            {
                Kind = ModuleKind.Dll,
                ReflectionImporterProvider = MMReflectionImporter.ProviderNoDefault
            });

            var type = new TypeDefinition(
                "",
                $"DMD<{name}>?{GetHashCode()}",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class
            );
            module.Types.Add(type);

            var def = Definition = new MethodDefinition(
                name,
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                returnType != null ? module.ImportReference(returnType) : module.TypeSystem.Void
            );
            foreach (var paramType in parameterTypes)
                def.Parameters.Add(new ParameterDefinition(module.ImportReference(paramType)));
            type.Methods.Add(def);
        }

        private void LoadFromMethod(MethodBase orig, out ModuleDefinition Module, out MethodDefinition def)
        {
            Type[] argTypes;
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

            _CreateDynModule(orig.GetID(simple: true), (orig as MethodInfo)?.ReturnType, argTypes, out Module, out def);

            _CopyMethodToDefinition(orig, def);

            if (!orig.IsStatic)
            {
                def.Parameters[0].Name = "this";
            }
            for (var i = 0; i < args.Length; i++)
                def.Parameters[i + offs].Name = args[i].Name;
        }

        public MethodInfo Generate()
            => Generate(null);
        public MethodInfo Generate(object? context)
        {
            var dmdType = Switches.TryGetSwitchValue(Switches.DMDType, out var swValue) ? swValue as string : null;

            if (dmdType is not null)
            {
                if (dmdType.Equals("dynamicmethod", StringComparison.OrdinalIgnoreCase)
                    || dmdType.Equals("dm", StringComparison.OrdinalIgnoreCase))
                {
                    return DMDEmitDynamicMethodGenerator.Generate(this, context);
                }
                if (dmdType.Equals("cecil", StringComparison.OrdinalIgnoreCase)
                    || dmdType.Equals("md", StringComparison.OrdinalIgnoreCase))
                {
                    return DMDCecilGenerator.Generate(this, context);
                }
#if NETFRAMEWORK
                if (dmdType.Equals("methodbuilder", StringComparison.OrdinalIgnoreCase)
                    || dmdType.Equals("mb", StringComparison.OrdinalIgnoreCase)) {
                    return DMDEmitMethodBuilderGenerator.Generate(this, context);
                }
#endif
            }

            if (dmdType is not null)
            {
                var type = ReflectionHelper.GetType(dmdType);
                if (type != null)
                {
                    if (!t__IDMDGenerator.IsCompatible(type))
                        throw new ArgumentException($"Invalid DMDGenerator type: {dmdType}");
                    var gen = _DMDGeneratorCache.GetOrAdd(dmdType, _ => (IDMDGenerator)Activator.CreateInstance(type)!);
                    return gen.Generate(this, context);
                }
            }

            if (_PreferCecil)
                return DMDCecilGenerator.Generate(this, context);

            if (Debug)
#if !NETFRAMEWORK
                return DMDCecilGenerator.Generate(this, context);
#else
                return DMDEmitMethodBuilderGenerator.Generate(this, context);
#endif

            // In .NET Framework, DynamicILGenerator doesn't support fault and filter blocks.
            // This is a non-issue in .NET Core, yet it could still be an issue in mono.
            // https://github.com/dotnet/coreclr/issues/1764
#if NETFRAMEWORK
            if (Definition!.Body.ExceptionHandlers.Any(eh =>
                eh.HandlerType == ExceptionHandlerType.Fault ||
                eh.HandlerType == ExceptionHandlerType.Filter
            ))
                return DMDEmitMethodBuilderGenerator.Generate(this, context);
#endif

            return DMDEmitDynamicMethodGenerator.Generate(this, context);
        }

        public void Dispose()
        {
            if (isDisposed)
                return;
            isDisposed = true;
            Module?.Dispose();
        }

        public string GetDumpName(string type)
        {
            // TODO: Add {Definition.GetID(withType: false)} without killing MethodBuilder
            return $"DMDASM.{GUID.GetHashCode():X8}{(string.IsNullOrEmpty(type) ? "" : $".{type}")}";
        }

    }
}
