using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MonoMod.RuntimeDetour.HookGen {
    public class HookGenerator {

        public MonoModder Modder;

        public ModuleDefinition OutputModule;

        public string Namespace;
        public bool HookOrig;

        public ModuleDefinition md_RuntimeDetour;

        public TypeReference t_MulticastDelegate;
        public TypeReference t_IAsyncResult;
        public TypeReference t_AsyncCallback;
        public TypeReference t_MethodBase;
        public TypeReference t_RuntimeMethodHandle;
        public TypeReference t_HookManager;

        public MethodReference m_GetMethodFromHandle;
        public MethodReference m_Add;
        public MethodReference m_Remove;

        public HookGenerator(MonoModder modder, string name) {
            Modder = modder;

            OutputModule = ModuleDefinition.CreateModule(name, new ModuleParameters {
                Architecture = modder.Module.Architecture,
                AssemblyResolver = modder.Module.AssemblyResolver,
                Kind = ModuleKind.Dll,
                Runtime = modder.Module.Runtime
            });

            Namespace = Environment.GetEnvironmentVariable("MONOMOD_HOOKGEN_NAMESPACE");
            if (string.IsNullOrEmpty(Namespace))
                Namespace = "On";
            HookOrig = Environment.GetEnvironmentVariable("MONOMOD_HOOKGEN_HOOKORIG") == "1";

            modder.MapDependency(modder.Module, "MonoMod.RuntimeDetour");
            if (!modder.DependencyCache.TryGetValue("MonoMod.RuntimeDetour", out md_RuntimeDetour))
                throw new FileNotFoundException("MonoMod.RuntimeDetour not found!");

            t_MulticastDelegate = OutputModule.ImportReference(modder.FindType("System.MulticastDelegate"));
            t_IAsyncResult = OutputModule.ImportReference(modder.FindType("System.IAsyncResult"));
            t_AsyncCallback = OutputModule.ImportReference(modder.FindType("System.AsyncCallback"));
            t_MethodBase = OutputModule.ImportReference(modder.FindType("System.Reflection.MethodBase"));
            t_RuntimeMethodHandle = OutputModule.ImportReference(modder.FindType("System.RuntimeMethodHandle"));
            TypeDefinition td_HookManager = md_RuntimeDetour.GetType("MonoMod.RuntimeDetour.HookManager");
            t_HookManager = OutputModule.ImportReference(td_HookManager);

            m_GetMethodFromHandle = OutputModule.ImportReference(
                new MethodReference("GetMethodFromHandle", t_MethodBase, t_MethodBase) {
                    Parameters = {
                        new ParameterDefinition(t_RuntimeMethodHandle)
                    }
                }
            );
            m_Add = OutputModule.ImportReference(td_HookManager.FindMethod("Add"));
            m_Remove = OutputModule.ImportReference(td_HookManager.FindMethod("Remove"));

        }

        public void Generate() {
            foreach (TypeDefinition type in Modder.Module.Types) {
                TypeDefinition hookType = GenerateFor(type);
                if (hookType == null)
                    continue;
                OutputModule.Types.Add(hookType);
            }
        }

        public TypeDefinition GenerateFor(TypeDefinition type) {
            if (type.HasGenericParameters ||
                type.IsRuntimeSpecialName ||
                type.Name.StartsWith("<"))
                return null;

            Modder.LogVerbose($"[HookGen] Generating for type {type.FullName}");

            TypeDefinition hookType = new TypeDefinition(
                type.IsNested ? null : (Namespace + (string.IsNullOrEmpty(type.Namespace) ? "" : ("." + type.Namespace))),
                type.Name,
                (type.IsNested ? TypeAttributes.NestedPublic : TypeAttributes.Public) | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class,
                OutputModule.TypeSystem.Object
            );

            bool add = false;

            foreach (MethodDefinition method in type.Methods)
                add |= GenerateFor(hookType, method);

            foreach (TypeDefinition nested in type.NestedTypes) {
                TypeDefinition hookNested = GenerateFor(nested);
                if (hookNested == null)
                    continue;
                add = true;
                hookType.NestedTypes.Add(hookNested);
            }

            if (!add)
                return null;
            return hookType;
        }

        public bool GenerateFor(TypeDefinition hookType, MethodDefinition method) {
            if (method.HasGenericParameters ||
                method.IsSpecialName)
                return false;

            if (!HookOrig && method.Name.StartsWith("orig_"))
                return false;

            int index = method.DeclaringType.Methods.Where(other => !other.HasGenericParameters && other.Name == method.Name).ToList().IndexOf(method);
            string suffix = "";
            if (index != 0) {
                suffix = index.ToString();
                do {
                    suffix = "_" + suffix;
                } while (method.DeclaringType.Methods.Any(other => !other.HasGenericParameters && other.Name == (method.Name + suffix)));
            }
            string name = method.Name + suffix;

            // TODO: Fix possible conflict when other members with the same names exist.

            TypeDefinition delOrig = GenerateDelegateFor(hookType, method);
            delOrig.Name = "orig_" + name;
            hookType.NestedTypes.Add(delOrig);

            TypeDefinition delHook = GenerateDelegateFor(hookType, method);
            delHook.Name = "hook_" + name;
            MethodDefinition delHookInvoke = delHook.FindMethod("Invoke");
            delHookInvoke.Parameters.Insert(0, new ParameterDefinition("orig", ParameterAttributes.None, delOrig));
            MethodDefinition delHookBeginInvoke = delHook.FindMethod("BeginInvoke");
            delHookBeginInvoke.Parameters.Insert(0, new ParameterDefinition("orig", ParameterAttributes.None, delOrig));
            hookType.NestedTypes.Add(delHook);

            hookType.Fields.Add(new FieldDefinition(
                "." + name,
                FieldAttributes.Private | FieldAttributes.Static,
                delHook
            ));

            ILProcessor il;

            MethodDefinition add = new MethodDefinition(
                "add_" + name,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.Static,
                OutputModule.TypeSystem.Void
            ) {
                IsIL = true,
                IsManaged = true
            };
            add.Parameters.Add(new ParameterDefinition(null, ParameterAttributes.None, delHook));
            add.Body = new MethodBody(add);
            il = add.Body.GetILProcessor();
            il.Emit(OpCodes.Ldtoken, OutputModule.ImportReference(method));
            il.Emit(OpCodes.Call, m_GetMethodFromHandle);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, m_Add);
            il.Emit(OpCodes.Ret);
            hookType.Methods.Add(add);

            MethodDefinition remove = new MethodDefinition(
                "remove_" + name,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.Static,
                OutputModule.TypeSystem.Void
            ) {
                IsIL = true,
                IsManaged = true
            };
            remove.Parameters.Add(new ParameterDefinition(null, ParameterAttributes.None, delHook));
            remove.Body = new MethodBody(remove);
            il = remove.Body.GetILProcessor();
            il.Emit(OpCodes.Ldtoken, OutputModule.ImportReference(method));
            il.Emit(OpCodes.Call, m_GetMethodFromHandle);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, m_Remove);
            il.Emit(OpCodes.Ret);
            hookType.Methods.Add(remove);

            EventDefinition ev = new EventDefinition(name, EventAttributes.None, delHook) {
                AddMethod = add,
                RemoveMethod = remove
            };
            hookType.Events.Add(ev);

            return true;
        }

        public TypeDefinition GenerateDelegateFor(TypeDefinition hookType, MethodDefinition method) {
            int index = method.DeclaringType.Methods.Where(other => !other.HasGenericParameters && other.Name == method.Name).ToList().IndexOf(method);
            string suffix = "";
            if (index != 0) {
                suffix = index.ToString();
                do {
                    suffix = "_" + suffix;
                } while (method.DeclaringType.Methods.Any(other => !other.HasGenericParameters && other.Name == (method.Name + suffix)));
            }
            string name = "d_" + method.Name + suffix;

            TypeDefinition del = new TypeDefinition(
                null, null,
                TypeAttributes.NestedPublic | TypeAttributes.Sealed | TypeAttributes.Class,
                t_MulticastDelegate
            );

            MethodDefinition ctor = new MethodDefinition(
                ".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                OutputModule.TypeSystem.Void
            ) {
                IsRuntime = true,
                IsManaged = true
            };
            ctor.Parameters.Add(new ParameterDefinition(OutputModule.TypeSystem.Object));
            ctor.Parameters.Add(new ParameterDefinition(OutputModule.TypeSystem.IntPtr));
            ctor.Body = new MethodBody(ctor);
            del.Methods.Add(ctor);

            MethodDefinition invoke = new MethodDefinition(
                "Invoke",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
                OutputModule.ImportReference(method.ReturnType)
            ) {
                IsRuntime = true,
                IsManaged = true
            };
            if (!method.IsStatic)
                invoke.Parameters.Add(new ParameterDefinition("self", ParameterAttributes.None, OutputModule.ImportReference(method.DeclaringType)));
            foreach (ParameterDefinition param in method.Parameters)
                invoke.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes & ~ParameterAttributes.Optional, OutputModule.ImportReference(param.ParameterType)));
            invoke.Body = new MethodBody(invoke);
            del.Methods.Add(invoke);

            MethodDefinition invokeBegin = new MethodDefinition(
                "BeginInvoke",
                MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
                t_IAsyncResult
            ) {
                IsRuntime = true,
                IsManaged = true
            };
            foreach (ParameterDefinition param in invoke.Parameters)
                invokeBegin.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, param.ParameterType));
            invokeBegin.Parameters.Add(new ParameterDefinition("callback", ParameterAttributes.None, t_AsyncCallback));
            invokeBegin.Parameters.Add(new ParameterDefinition(null, ParameterAttributes.None, OutputModule.TypeSystem.Object));
            invokeBegin.Body = new MethodBody(invokeBegin);
            del.Methods.Add(invokeBegin);

            MethodDefinition invokeEnd = new MethodDefinition(
                "BeginInvoke",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
                OutputModule.TypeSystem.Object
            ) {
                IsRuntime = true,
                IsManaged = true
            };
            invokeEnd.Parameters.Add(new ParameterDefinition("result", ParameterAttributes.None, t_IAsyncResult));
            invokeEnd.Body = new MethodBody(invokeEnd);
            del.Methods.Add(invokeEnd);

            return del;
        }

    }
}
