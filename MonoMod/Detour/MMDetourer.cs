using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Mdb;
using Mono.Cecil.Pdb;
using Mono.Collections.Generic;
using MonoMod.InlineRT;
using StringInject;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MonoMod.NET40Shim;
using MonoMod.Helpers;
using System.Reflection;
using System.Runtime.InteropServices;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace MonoMod.Detour {
    public class MonoModDetourer : MonoModder {

        public static ConstructorInfo c_ExtensionAttribute =
            typeof(System.Runtime.CompilerServices.ExtensionAttribute).GetConstructor(new Type[0]);

        public Assembly RuntimeTarget;
        private Module _RuntimeTargetModule;
        public Module RuntimeTargetModule {
            get {
                if (RuntimeTarget == null)
                    RuntimeTarget = Assembly.Load(Module.Assembly.Name.FullName);

                if (_RuntimeTargetModule != null && (
                        RuntimeTarget == null ||
                        Module == null ||
                        _RuntimeTargetModule.Name == Module.Name
                    ))
                    return _RuntimeTargetModule;
                return _RuntimeTargetModule = RuntimeTarget.GetModule(Module.Name);
            }
        }

        public Stack<MonoModDetourerLevel> Levels = new Stack<MonoModDetourerLevel>();
        public MonoModDetourerLevel Level => Levels.Count == 0 ? null : Levels.Peek();

        public MonoModDetourer()
            : base() {

            OnReadMod += ReadDetours;
        }

        public override void Log(string str)
            => base.Log("[Detourer] " + str);

        public virtual MonoModDetourerLevel NextLevel(ModuleDefinition mod) {
            MonoModDetourerLevel level = new MonoModDetourerLevel(this, mod);
            Levels.Push(level);
            return level;
        }

        public virtual void ReadDetours(ModuleDefinition mod) {
            // Could be extended in the future.
            MapDependencies(mod);
            NextLevel(mod);
            PatchModule(mod);
            ModuleDefinition target = Module;
            Module = mod;
            PatchRefs(mod);
            Module = target;
            Apply();
        }

        public virtual void Apply()
            => Level.Apply();

        public override void PatchProperty(TypeDefinition targetType, PropertyDefinition prop, HashSet<MethodDefinition> propMethods = null) {
            // no-op
        }

        public override void PatchField(TypeDefinition targetType, FieldDefinition field) {
            // no-op
        }

        public override MethodDefinition PatchMethod(TypeDefinition targetType, MethodDefinition method) {
            bool isOriginal = false;
            if (method.Name.StartsWith("orig_") || method.HasMMAttribute("Original"))
                isOriginal = true;

            if (!AllowedSpecialName(method, targetType) || !method.MatchingConditionals(Module))
                // Ignore ignored methods
                return null;

            string typeName = targetType.GetPatchFullName();

            MethodDefinition existingMethod = targetType.FindMethod(method.GetFindableID(withType: false));

            if (method.HasMMAttribute("Ignore"))
                // Custom attribute carrying doesn't apply here.
                return null;

            if (SkipList.Contains(method.GetFindableID(type: typeName)))
                return null;

            if (existingMethod == null) {
                LogVerbose($"[PatchMethod] Method {method.GetFindableID(withType: false)} not found in target type {typeName}");
                return null;
            }

            TransformMethodToExtension(targetType, method);

            // If the method's a MonoModConstructor method, just update its attributes to make it look like one.
            if (method.HasMMAttribute("Constructor")) {
                method.Name = method.IsStatic ? ".cctor" : ".ctor";
                method.IsSpecialName = true;
                method.IsRuntimeSpecialName = true;
            }

            // .cctor without [MonoModConstructor] should be ignored.
            if (method.IsConstructor && method.IsStatic && method.HasBody && !method.HasMMAttribute("Constructor"))
                return null;

            if (isOriginal) {
                PatchTrampoline(existingMethod, method);
            } else {
                PatchDetour(existingMethod, method);
            }

            return method;
        }

        public virtual void PatchTrampoline(MethodDefinition targetMethod, MethodDefinition method) {
            method.IsManaged = true;
            method.IsIL = true;
            method.IsNative = false;
            method.PInvokeInfo = null;
            method.IsPreserveSig = true;
            method.IsInternalCall = false;
            method.IsPInvokeImpl = false;
            method.NoInlining = true;

            MethodBody body;
            if (!method.HasBody)
                body = method.Body = new MethodBody(method);
            else
                body = method.Body;

            ILProcessor il = body.GetILProcessor();
            for (int i = 64; i > -1; --i)
                il.Emit(OpCodes.Nop);
            if (method.ReturnType.MetadataType != MetadataType.Void) {
                il.Emit(OpCodes.Ldnull);
                if (method.ReturnType.IsValueType)
                    il.Emit(OpCodes.Box, method.ReturnType);
            }
            il.Emit(OpCodes.Ret);

            RegisterTrampoline(targetMethod, method);
        }

        public virtual void PatchDetour(MethodDefinition targetMethod, MethodDefinition method) {
            if (method.HasBody) {
                MethodBody body = method.Body;
                for (int instri = 0; instri < body.Instructions.Count; instri++) {
                    Instruction instr = body.Instructions[instri];
                    
                    if (instr.OpCode == OpCodes.Callvirt &&
                        ((MethodReference) method).DeclaringType.Scope == method.Module) {
                        instr.OpCode = OpCodes.Call;
                    }
                }
            }

            RegisterDetour(targetMethod, method);
        }

        public virtual void TransformMethodToExtension(TypeDefinition targetType, MethodDefinition method) {
            if (method.IsStatic)
                return;

            method.IsStatic = true;
            method.IsVirtual = false;
            method.IsNewSlot = false;
            method.IsReuseSlot = true;
            method.HasThis = false;
            method.ExplicitThis = false;

            method.AddAttribute(method.Module.ImportReference(c_ExtensionAttribute));

            method.Parameters.Insert(0, new ParameterDefinition("self", ParameterAttributes.None, targetType));
        }

        public virtual void RegisterTrampoline(MethodDefinition targetMethod, MethodDefinition method)
            => Level.RegisterTrampoline(targetMethod, method);

        public virtual void RegisterDetour(MethodDefinition targetMethod, MethodDefinition method)
            => Level.RegisterDetour(targetMethod, method);

    }
}
