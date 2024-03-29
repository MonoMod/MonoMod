﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace MonoMod.Utils.Cil
{
    public partial class ILGeneratorShim
    {

        /// <summary>
        /// Get a "real" ILGenerator for this ILGeneratorShim.
        /// </summary>
        /// <returns>A "real" ILGenerator.</returns>
        public System.Reflection.Emit.ILGenerator GetProxy()
        {
            return (System.Reflection.Emit.ILGenerator)ILGeneratorBuilder
                .GenerateProxy()
                .MakeGenericType(GetType())
                .GetConstructors()[0]
                .Invoke(new object[] { this });
        }

        /// <summary>
        /// Get the proxy type for a given ILGeneratorShim type. The proxy type implements ILGenerator.
        /// </summary>
        /// <typeparam name="TShim">The ILGeneratorShim type.</typeparam>
        /// <returns>The "real" ILGenerator type.</returns>
        public static Type GetProxyType<TShim>() where TShim : ILGeneratorShim => GetProxyType(typeof(TShim));
        /// <summary>
        /// Get the proxy type for a given ILGeneratorShim type. The proxy type implements ILGenerator.
        /// </summary>
        /// <param name="tShim">The ILGeneratorShim type.</param>
        /// <returns>The "real" ILGenerator type.</returns>
        public static Type GetProxyType(Type tShim) => GenericProxyType.MakeGenericType(tShim);
        /// <summary>
        /// Get the (open) generic proxy type implementing ILGenerator.
        /// </summary>
        /// <returns>The "real" ILGenerator type, non-generic.</returns>
        public static Type GenericProxyType => ILGeneratorBuilder.GenerateProxy();

        internal static class ILGeneratorBuilder
        {

            // NOTE: If you plan on changing this, keep in mind that any InternalsVisibleToAttributes need to be updated as well!
            public const string Namespace = "MonoMod.Utils.Cil";
            public const string Name = "ILGeneratorProxy";
            public const string FullName = Namespace + "." + Name;
            public const string TargetName = "Target";
            static Type? ProxyType;

            public static Type GenerateProxy()
            {
                if (ProxyType != null)
                    return ProxyType;
                Assembly asm;

                var t_ILGenerator = typeof(System.Reflection.Emit.ILGenerator);
                var t_ILGeneratorProxyTarget = typeof(ILGeneratorShim);

                using (var module = ModuleDefinition.CreateModule(FullName,
                        new ModuleParameters()
                        {
                            Kind = ModuleKind.Dll,
                            ReflectionImporterProvider = MMReflectionImporter.Provider
                        }
                    ))
                {
                    var ca_IACTA = new CustomAttribute(module.ImportReference(DynamicMethodDefinition.c_IgnoresAccessChecksToAttribute));
                    ca_IACTA.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, typeof(ILGeneratorShim).Assembly.GetName().Name));
                    module.Assembly.CustomAttributes.Add(ca_IACTA);

                    var type = new TypeDefinition(
                        Namespace,
                        Name,
                        TypeAttributes.Public
                    )
                    {
                        BaseType = module.ImportReference(t_ILGenerator)
                    };
                    module.Types.Add(type);

                    var tr_ILGeneratorProxyTarget = module.ImportReference(t_ILGeneratorProxyTarget);

                    var g_TTarget = new GenericParameter("TTarget", type);
#if CECIL0_10
                    g_TTarget.Constraints.Add(tr_ILGeneratorProxyTarget);
#else
                    g_TTarget.Constraints.Add(new GenericParameterConstraint(tr_ILGeneratorProxyTarget));
#endif
                    type.GenericParameters.Add(g_TTarget);

                    var fd_Target = new FieldDefinition(
                        TargetName,
                        FieldAttributes.Public,
                        g_TTarget
                    );
                    type.Fields.Add(fd_Target);


                    var git = new GenericInstanceType(type);
                    git.GenericArguments.Add(g_TTarget);

                    var fr_Target = new FieldReference(TargetName, g_TTarget, git);

                    var ctor = new MethodDefinition(".ctor",
                        MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                        module.TypeSystem.Void
                    );
                    ctor.Parameters.Add(new ParameterDefinition(g_TTarget));
                    type.Methods.Add(ctor);

                    var il = ctor.Body.GetILProcessor();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Stfld, fr_Target);
                    il.Emit(OpCodes.Ret);

                    foreach (var orig in t_ILGenerator.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var target = t_ILGeneratorProxyTarget.GetMethod(orig.Name, orig.GetParameters().Select(p => p.ParameterType).ToArray());
                        if (target == null)
                            continue;

                        var proxy = new MethodDefinition(
                            orig.Name,
                            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                            module.ImportReference(orig.ReturnType)
                        )
                        {
                            HasThis = true
                        };
                        foreach (var param in orig.GetParameters())
                            proxy.Parameters.Add(new ParameterDefinition(module.ImportReference(param.ParameterType)));
                        type.Methods.Add(proxy);

                        il = proxy.Body.GetILProcessor();
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, fr_Target);
                        foreach (var param in proxy.Parameters)
                            il.Emit(OpCodes.Ldarg, param);
                        il.Emit(target.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, il.Body.Method.Module.ImportReference(target));
                        il.Emit(OpCodes.Ret);
                    }

                    asm = ReflectionHelper.Load(module);
                    asm.SetMonoCorlibInternal(true);
                }

                // .NET hates to acknowledge manually loaded assemblies.
                // Luckily, ReflectionHelper already does the following for asm.
                // Sadly, we can't control how MonoMod.Common / MonoMod.Utils / ... gets loaded.
                ResolveEventHandler mmcResolver = (asmSender, asmArgs) =>
                {
                    var asmName = new AssemblyName(asmArgs.Name);
                    if (asmName.Name == typeof(ILGeneratorBuilder).Assembly.GetName().Name)
                        return typeof(ILGeneratorBuilder).Assembly;
                    return null;
                };

                AppDomain.CurrentDomain.AssemblyResolve += mmcResolver;
                try
                {
                    ProxyType = asm.GetType(FullName);
                }
                finally
                {
                    AppDomain.CurrentDomain.AssemblyResolve -= mmcResolver;
                }

                if (ProxyType == null)
                {
                    var builder = new StringBuilder();
                    builder.Append("Couldn't find ILGeneratorShim proxy \"").Append(FullName).Append("\" in autogenerated \"").Append(asm.FullName).AppendLine("\"");

                    Type?[] types;
                    Exception?[]? exceptions;
                    try
                    {
                        types = asm.GetTypes();
                        exceptions = null;

                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        types = e.Types;
                        exceptions = new Exception[e.LoaderExceptions.Length + 1];
                        exceptions[0] = e;
                        for (var i = 0; i < e.LoaderExceptions.Length; i++)
                            exceptions[i + 1] = e.LoaderExceptions[i];
                    }

                    builder.AppendLine("Listing all types in autogenerated assembly:");
                    foreach (var type in types)
                        builder.AppendLine(type?.FullName ?? "<NULL>");

                    if (exceptions is not null && exceptions.Length > 0)
                    {
                        builder.AppendLine("Listing all exceptions:");
                        for (var i = 0; i < exceptions.Length; i++)
                            builder.Append('#').Append(i).Append(": ").AppendLine(exceptions[i]?.ToString() ?? "NULL");
                    }

                    throw new InvalidOperationException(builder.ToString());
                }

                return ProxyType;
            }

        }

    }
    public static class ILGeneratorShimExt
    {

        private static readonly Dictionary<Type, MethodInfo> _Emitters = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> _EmittersShim = new Dictionary<Type, MethodInfo>();

        static ILGeneratorShimExt()
        {
            foreach (var method in typeof(System.Reflection.Emit.ILGenerator).GetMethods())
            {
                if (method.Name != "Emit")
                    continue;

                var args = method.GetParameters();
                if (args.Length != 2)
                    continue;

                if (args[0].ParameterType != typeof(System.Reflection.Emit.OpCode))
                    continue;
                _Emitters[args[1].ParameterType] = method;
            }

            foreach (var method in typeof(ILGeneratorShim).GetMethods())
            {
                if (method.Name != "Emit")
                    continue;

                var args = method.GetParameters();
                if (args.Length != 2)
                    continue;

                if (args[0].ParameterType != typeof(System.Reflection.Emit.OpCode))
                    continue;
                _EmittersShim[args[1].ParameterType] = method;
            }
        }

        public static ILGeneratorShim GetProxiedShim(this System.Reflection.Emit.ILGenerator il)
            => (ILGeneratorShim)Helpers.ThrowIfNull(il).GetType().GetField(
                ILGeneratorShim.ILGeneratorBuilder.TargetName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            )?.GetValue(il)!;

        public static T GetProxiedShim<T>(this System.Reflection.Emit.ILGenerator il) where T : ILGeneratorShim
            => (T)il.GetProxiedShim();

        public static object? DynEmit(this System.Reflection.Emit.ILGenerator il, System.Reflection.Emit.OpCode opcode, object operand)
            => il.DynEmit(new object[] { opcode, operand });

        public static object? DynEmit(this System.Reflection.Emit.ILGenerator il, object[] emitArgs)
        {
            Helpers.ThrowIfArgumentNull(emitArgs);
            var operandType = emitArgs[1].GetType();

            var target = il.GetProxiedShim() ?? (object)il;
            var emitters = target is ILGeneratorShim ? _EmittersShim : _Emitters;

            if (!emitters.TryGetValue(operandType, out var emit))
                emit = emitters.FirstOrDefault(kvp => kvp.Key.IsAssignableFrom(operandType)).Value;
            if (emit == null)
                throw new InvalidOperationException($"Unexpected unemittable operand type {operandType.FullName}");

            return emit.Invoke(target, emitArgs);
        }

    }
}
