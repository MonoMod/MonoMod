using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
#if !NETSTANDARD
using System.Security.Permissions;
using System.Security;
using System.Diagnostics.SymbolStore;
#endif

namespace MonoMod.Utils {
    public sealed partial class DynamicMethodDefinition {

#if NETSTANDARD1_X
        private static readonly Type t_AssemblyLoadContext =
            typeof(Assembly).GetTypeInfo().Assembly
            .GetType("System.Runtime.Loader.AssemblyLoadContext");
        private static readonly object _AssemblyLoadContext_Default =
            t_AssemblyLoadContext.GetProperty("Default").GetValue(null);
        private static readonly FastReflectionDelegate _AssemblyLoadContext_LoadFromStream =
            t_AssemblyLoadContext.GetMethod("LoadFromStream", new Type[] { typeof(Stream) })
            .CreateFastDelegate();
#endif

        public MethodInfo GenerateViaCecil(TypeDefinition typeDef) {
            bool moduleIsPrivate = false;
            ModuleDefinition module = typeDef?.Module;
            if (typeDef == null) {
                moduleIsPrivate = true;

                string name = $"DMDASM_{GetHashCode()}";
                module = ModuleDefinition.CreateModule(name, ModuleKind.Dll);
                typeDef = new TypeDefinition(
                    "",
                    $"DMD<{Method.GetFindableID(simple: true).Replace('.', '_')}>?{GetHashCode()}",
                    Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.Class
                ) {
                    BaseType = module.TypeSystem.Object
                };
                module.Types.Add(typeDef);
            }

            try {

#pragma warning disable IDE0039 // Use local function
                Relinker relinker = (mtp, ctx) => {
                    return module.ImportReference(mtp);
                };
#pragma warning restore IDE0039 // Use local function

                MethodDefinition method = Definition;
                MethodDefinition clone = new MethodDefinition(method.Name, method.Attributes, module.TypeSystem.Void) {
                    MethodReturnType = method.MethodReturnType,
                    Attributes = Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                    ImplAttributes = Mono.Cecil.MethodImplAttributes.IL | Mono.Cecil.MethodImplAttributes.Managed,
                    DeclaringType = typeDef,
                    NoInlining = true
                };

                foreach (ParameterDefinition param in method.Parameters)
                    clone.Parameters.Add(param.Clone().Relink(relinker, clone));

                clone.ReturnType = method.ReturnType.Relink(relinker, clone);

                typeDef.Methods.Add(clone);

                clone.HasThis = method.HasThis;
                Mono.Cecil.Cil.MethodBody body = clone.Body = method.Body.Clone(clone);

                foreach (VariableDefinition var in clone.Body.Variables)
                    var.VariableType = var.VariableType.Relink(relinker, clone);

                foreach (ExceptionHandler handler in clone.Body.ExceptionHandlers)
                    if (handler.CatchType != null)
                        handler.CatchType = handler.CatchType.Relink(relinker, clone);

                for (int instri = 0; instri < body.Instructions.Count; instri++) {
                    Instruction instr = body.Instructions[instri];
                    object operand = instr.Operand;

                    // Import references.
                    if (operand is ParameterDefinition param) {
                        operand = clone.Parameters[param.Index];
                    } else if (operand is IMetadataTokenProvider mtp) {
                        operand = mtp.Relink(relinker, clone);
                    }

                    // TODO: Fix up DynamicMethod inline refs.

                    instr.Operand = operand;
                }

                clone.HasThis = false;

                if (method.HasThis) {
                    TypeReference type = method.DeclaringType;
                    if (type.IsValueType)
                        type = new ByReferenceType(type);
                    clone.Parameters.Insert(0, new ParameterDefinition("<>_this", Mono.Cecil.ParameterAttributes.None, type.Relink(relinker, clone)));
                }

                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MONOMOD_DMD_DUMP"))) {
                    string dir = Path.GetFullPath(Environment.GetEnvironmentVariable("MONOMOD_DMD_DUMP"));
                    string name = module.Name + ".dll";
                    string path = Path.Combine(dir, name);
                    dir = Path.GetDirectoryName(path);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    if (File.Exists(path))
                        File.Delete(path);
                    using (Stream fileStream = File.OpenWrite(path))
                        module.Write(fileStream);
                }

                Assembly asm;
                using (MemoryStream asmStream = new MemoryStream()) {
                    module.Write(asmStream);
                    asmStream.Seek(0, SeekOrigin.Begin);
#if NETSTANDARD1_X
                    // System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(asmStream);
                    asm = (Assembly) _AssemblyLoadContext_LoadFromStream(_AssemblyLoadContext_Default, asmStream);
#else
                    asm = Assembly.Load(asmStream.GetBuffer());
#endif
                }

                return asm.GetType(typeDef.FullName.Replace("+", "\\+"), false, false).GetMethod(clone.Name);

            } finally {
                if (moduleIsPrivate)
                    module.Dispose();
            }
        }

    }
}
