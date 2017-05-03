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

        private readonly static ConstructorInfo c_ExtensionAttribute =
            typeof(System.Runtime.CompilerServices.ExtensionAttribute).GetConstructor(new Type[0]);
        private readonly Type t_SecurityPermissionAttribute =
            typeof(System.Security.Permissions.SecurityPermissionAttribute);

        private readonly static MethodInfo m_Console_WriteLine_string =
            typeof(Console).GetMethod("WriteLine", new Type[] { typeof(string) });

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

        public virtual MonoModDetourerLevel FindLevel(string modName) {
            foreach (MonoModDetourerLevel level in Levels)
                if (level._Mod.Name == modName ||
                    level._Mod.Assembly.Name.Name == modName ||
                    level._Mod.Assembly.Name.FullName == modName)
                    return level;
            return null;
        }

        public virtual MonoModDetourerLevel FindLevel(ModuleDefinition mod) {
            foreach (MonoModDetourerLevel level in Levels)
                if (level._Mod == mod)
                    return level;
            return null;
        }

        public virtual void ReadDetours(ModuleDefinition mod) {
            // Could be extended in the future.
            MapDependencies();
            NextLevel(mod);
            PatchModule(mod);
            ModuleDefinition target = Module;
            Module = mod;
            PatchRefs(mod);
            Module = target;
            Apply();
            ClearCaches(moduleSpecific: true);
        }

        public virtual void Apply()
            => Level.Apply();

        public override void PatchType(TypeDefinition type) {
            base.PatchType(type);

            CustomAttribute patchName = type.GetMMAttribute("Patch");
            CustomAttributeArgument patchNameArg = new CustomAttributeArgument(Module.TypeSystem.String, type.FullName);
            if (patchName != null) {
                patchName.ConstructorArguments[0] = patchNameArg;
            } else {
                patchName = new CustomAttribute(GetMonoModPatchCtor());
                patchName.ConstructorArguments.Add(patchNameArg);
                type.AddAttribute(patchName);
            }
        }

        public override void PatchProperty(TypeDefinition targetType, PropertyDefinition prop, HashSet<MethodDefinition> propMethods = null) {
            // no-op
        }

        public override void PatchField(TypeDefinition targetType, FieldDefinition field) {
            // no-op
        }

        public override MethodDefinition PatchMethod(TypeDefinition targetType, MethodDefinition method) {
            if (method.Name.StartsWith("orig_") || method.HasMMAttribute("Original"))
                return null;

            string id = method.GetFindableID(withType: false);
            string idOrig = method.GetFindableID(name: method.GetOriginalName(), withType: false);

            MethodDefinition targetMethod = targetType.FindMethod(id);

            MethodDefinition ret = _PatchMethod(targetMethod, method, false);
            if (ret == null)
                return null;

            MethodDefinition orig = method.DeclaringType.FindMethod(idOrig);
            if (orig != null)
                _PatchMethod(targetMethod, orig, true);

            return ret;
        }

        private MethodDefinition _PatchMethod(MethodDefinition targetMethod, MethodDefinition method, bool isOriginal) {
            if (!AllowedSpecialName(method, targetMethod.DeclaringType) || !method.MatchingConditionals(Module))
                // Ignore ignored methods
                return null;

            string typeName = targetMethod.DeclaringType.Name;
            string idFull = method.GetFindableID(type: typeName);
            string idFullDirect = method.GetFindableID();
            string id = method.GetFindableID(withType: false);

            if (method.HasMMAttribute("Ignore"))
                // Custom attribute carrying doesn't apply here.
                return null;

            if (SkipList.Contains(id))
                return null;

            TransformMethodToExtension(targetMethod.DeclaringType, method);
            Tuple<string, string> relinkTo = Tuple.Create(method.DeclaringType.Name, method.GetFindableID(withType: false));
            RelinkMap[method.GetFindableID(type: typeName)] = relinkTo;
            RelinkMap[method.GetFindableID()] = relinkTo;

            if (targetMethod == null) {
                LogVerbose($"[PatchMethod] Method {id} not found in target type {typeName}");
                return null;
            }

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
                PatchTrampoline(targetMethod, method);
            } else {
                PatchDetour(targetMethod, method);
            }

            return method;
        }

        public virtual void PatchTrampoline(MethodDefinition targetMethod, MethodDefinition method) {
            method.IsManaged = true;
            method.IsIL = true;
            method.IsNative = false;
            method.PInvokeInfo = null;
            method.IsInternalCall = false;
            method.IsPInvokeImpl = false;
            method.NoInlining = true;

            MethodBody body = method.Body = new MethodBody(method);
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
