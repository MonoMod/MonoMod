using Mono.Cecil;
using MonoMod.Logs;
using MonoMod.Utils.DMDGenerators;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace MonoMod.Utils
{
    /// <summary>
    /// A DMDGenerator implementation using Mono.Cecil to build an in-memory assembly.
    /// </summary>
    public sealed class DMDCecilGenerator : DMDGenerator<DMDCecilGenerator>
    {
        protected override bool SupportsAsyncMethods => true;

        protected override MethodInfo GenerateCore(DynamicMethodDefinition dmd, object? context)
        {
            var refsHolder = new GeneratedAssemblyReferenceHolder();

            var def = dmd.Definition ?? throw new InvalidOperationException();
            var typeDef = context as TypeDefinition;

            var moduleIsTemporary = false;
            var module = typeDef?.Module;
            HashSet<string>? accessChecksIgnored = null;

            try
            {
                if (typeDef is null || module is null)
                {
                    moduleIsTemporary = true;

                    var name = dmd.GetDumpName("Cecil");
#pragma warning disable CA2000 // Dispose objects before losing scope
                    // This is disposed correctly, just not in a way the analyzer recognizes.
                    module = ModuleDefinition.CreateModule(name, new ModuleParameters()
                    {
                        Kind = ModuleKind.Dll,
                        ReflectionImporterProvider = MMReflectionImporter.ProviderNoDefault
                    });
#pragma warning restore CA2000 // Dispose objects before losing scope

                    accessChecksIgnored = new HashSet<string>();

                    module.Assembly.CustomAttributes.Add(new CustomAttribute(module.ImportReference(DynamicMethodDefinition.c_UnverifiableCodeAttribute)));

                    if (dmd.Debug)
                    {
                        var caDebug = new CustomAttribute(module.ImportReference(DynamicMethodDefinition.c_DebuggableAttribute));
                        caDebug.ConstructorArguments.Add(new CustomAttributeArgument(
                            module.ImportReference(typeof(DebuggableAttribute.DebuggingModes)),
                            DebuggableAttribute.DebuggingModes.DisableOptimizations | DebuggableAttribute.DebuggingModes.Default
                        ));
                        module.Assembly.CustomAttributes.Add(caDebug);
                    }

                    typeDef = new TypeDefinition(
                        "",
                        $"DMD<{dmd.OriginalMethod?.Name?.Replace('.', '_')}>?{GetHashCode()}",
                        Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.Class
                    )
                    {
                        BaseType = module.TypeSystem.Object
                    };

                    module.Types.Add(typeDef);
                }

                MethodDefinition? clone = null;

                var tr_IsVolatile = new TypeReference("System.Runtime.CompilerServices", "IsVolatile", module, module.TypeSystem.CoreLibrary);

#pragma warning disable IDE0039 // Use local function
                Relinker relinker = (mtp, ctx) =>
                {
                    if (mtp == def)
                        return clone!;
                    if (mtp is MethodReference mr)
                    {
                        if (mr.FullName == def.FullName
                         && mr.DeclaringType.FullName == def.DeclaringType.FullName
                         && mr.DeclaringType.Scope.Name == def.DeclaringType.Scope.Name)
                            return clone!;
                    }
                    return module.ImportReference(mtp);
                };
#pragma warning restore IDE0039 // Use local function

                clone = new MethodDefinition(dmd.Name ?? "_" + def.Name.Replace('.', '_'), def.Attributes, module.TypeSystem.Void)
                {
                    MethodReturnType = def.MethodReturnType,
                    Attributes = Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                    ImplAttributes = Mono.Cecil.MethodImplAttributes.IL | Mono.Cecil.MethodImplAttributes.Managed,
                    DeclaringType = typeDef,
                    NoInlining = true
                };

                if (dmd.Definition.ImplAttributes.Has(Mono.Cecil.MethodImplAttributes.NoInlining))
                {
                    clone.ImplAttributes |= Mono.Cecil.MethodImplAttributes.NoInlining;
                }

                // AggressiveOptimization
                if (dmd.Definition.ImplAttributes.Has((Mono.Cecil.MethodImplAttributes)512))
                {
                    clone.ImplAttributes |= (Mono.Cecil.MethodImplAttributes)512;
                }

                if (dmd.Definition.ImplAttributes.Has((Mono.Cecil.MethodImplAttributes)RuntimeAsync.AsyncIl))
                {
                    clone.ImplAttributes |= (Mono.Cecil.MethodImplAttributes)RuntimeAsync.AsyncIl;
                }

                foreach (var param in def.Parameters)
                    clone.Parameters.Add(param.Clone().Relink(relinker, clone));

                clone.ReturnType = def.ReturnType.Relink(relinker, clone);

                typeDef.Methods.Add(clone);

                clone.HasThis = def.HasThis;
                var body = clone.Body = def.Body.Clone(clone);

                foreach (var var in clone.Body.Variables)
                    var.VariableType = var.VariableType.Relink(relinker, clone);

                foreach (var handler in clone.Body.ExceptionHandlers)
                    if (handler.CatchType != null)
                        handler.CatchType = handler.CatchType.Relink(relinker, clone);

                for (var instri = 0; instri < body.Instructions.Count; instri++)
                {
                    var instr = body.Instructions[instri];
                    var operand = instr.Operand;

                    // Import references.
                    if (operand is ParameterDefinition param)
                    {
                        operand = clone.Parameters[param.Index];

                    }
                    else if (operand is DynamicMethodReference dmref)
                    {
                        if (instr.OpCode != Mono.Cecil.Cil.OpCodes.Call)
                        {
                            throw new InvalidOperationException(DebugFormatter.Format($"Invalid opcode {instr.OpCode} given dynamic method operand {dmref}"));
                        }

                        var dm = dmref.DynamicMethod;

                        // we need to convert to a delegate, store that in a static field, and load the field before calling the invoker
                        var (delTy, invoker) = DelegateTypeGenerator.GetDelegateType(MethodSignature.ForMethod(dm));

                        var field = new FieldDefinition($"f{typeDef.Fields.Count}", Mono.Cecil.FieldAttributes.Static, module.ImportReference(delTy));
                        typeDef.Fields.Add(field);

                        var del = dm.CreateDelegate(delTy);
                        refsHolder.AddStaticField(field.Name, del);

                        body.Instructions.Insert(instri, Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldsfld, field));
                        instri++;

                        operand = module.ImportReference(invoker);
                    }
                    else if (operand is IMetadataTokenProvider mtp)
                    {
                        operand = mtp.Relink(relinker, clone);

                    }

                    // System.Reflection doesn't contain any volatility info.
                    // System.Reflection.Emit presumably does something similar to this.
                    // Mono.Cecil thus isn't aware of the volatility as part of the imported field reference.
                    // The modifier is still necessary though.
                    // This is done here instead of the copier as Harmony and other users can't track modreqs

                    // This isn't actually a valid transformation though. A ldfld or stfld can have the volatile
                    // prefix, without having modreq(IsVolatile) on the field. Adding the modreq() causes the runtime
                    // to not be able to find the field.
                    /*if (instr.Previous?.OpCode == OpCodes.Volatile &&
                        operand is FieldReference fref &&
                        (fref.FieldType as RequiredModifierType)?.ModifierType != tr_IsVolatile) {
                        fref.FieldType = new RequiredModifierType(tr_IsVolatile, fref.FieldType);
                    }*/

                    if (accessChecksIgnored != null && operand is MemberReference mref)
                    {
                        var asmRef = (mref as TypeReference)?.Scope ?? mref.DeclaringType.Scope;
                        if (!accessChecksIgnored.Contains(asmRef.Name))
                        {
                            var caAccess = new CustomAttribute(module.ImportReference(DynamicMethodDefinition.c_IgnoresAccessChecksToAttribute));
                            caAccess.ConstructorArguments.Add(new CustomAttributeArgument(
                                module.ImportReference(typeof(DebuggableAttribute.DebuggingModes)),
                                asmRef.Name
                            ));
                            module.Assembly.CustomAttributes.Add(caAccess);
                            accessChecksIgnored.Add(asmRef.Name);
                        }
                    }

                    instr.Operand = operand;
                }

                clone.HasThis = false;

                if (def.HasThis)
                {
                    TypeReference declType = def.DeclaringType;
                    if (declType.IsValueType)
                        declType = new ByReferenceType(declType);
                    clone.Parameters.Insert(0, new ParameterDefinition("<>_this", Mono.Cecil.ParameterAttributes.None, declType.Relink(relinker, clone)));
                }

                var envDmdDump = Switches.TryGetSwitchValue(Switches.DMDDumpTo, out var dumpToVal) ? dumpToVal as string : null;
                if (!string.IsNullOrEmpty(envDmdDump))
                {
                    var dir = Path.GetFullPath(envDmdDump);
                    var name = module.Name + ".dll";
                    var path = Path.Combine(dir, name);
                    dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    if (File.Exists(path))
                        File.Delete(path);
                    using (Stream fileStream = File.OpenWrite(path))
                        module.Write(fileStream);
                }

                if (refsHolder.NeedsReferencesField)
                {
                    var field = new FieldDefinition("references", Mono.Cecil.FieldAttributes.Static, module.TypeSystem.Object);
                    typeDef.Fields.Add(field);
                    refsHolder.SetReferencesFieldName(field.Name);
                }

                // TODO: load these in unloadable ALCs when possible?
                var asm = ReflectionHelper.Load(module);

                var type = asm.GetType(typeDef.FullName.Replace("+", "\\+", StringComparison.Ordinal), false, false);
                Helpers.Assert(type is not null);

                refsHolder.SetFields(type);

                return type
                    .GetMethod(clone.Name, BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                    ?? throw new InvalidOperationException("Could not find generated method");

            }
            finally
            {
                if (moduleIsTemporary)
                    module!.Dispose();
                module = null;
            }
        }

    }
}
