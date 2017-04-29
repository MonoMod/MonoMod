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
    public sealed class MonoModDetourerLevel {

        internal readonly MonoModDetourer _MMD;
        internal readonly ModuleDefinition _Mod;

        public readonly string Name;

        internal readonly HashSet<Tuple<int, int>> _Trampolines = new HashSet<Tuple<int, int>>();
        internal readonly HashSet<Tuple<int, int>> _Detours = new HashSet<Tuple<int, int>>();

        internal Assembly _Assembly;
        internal Module _Module;

        internal MonoModDetourerLevel(MonoModDetourer mmd, ModuleDefinition mod) {
            _MMD = mmd;
            _Mod = mod;
        }

        public void RegisterTrampoline(MethodDefinition targetMethod, MethodDefinition method) {
            _MMD.LogVerbose($"[RegisterTrampoline] {method.GetFindableID()} ({method.MetadataToken.RID}) -> {targetMethod.GetFindableID()} ({targetMethod.MetadataToken.RID})");
            _Trampolines.Add(Tuple.Create(targetMethod.MetadataToken.ToInt32(), method.MetadataToken.ToInt32()));
        }

        public void RegisterDetour(MethodDefinition targetMethod, MethodDefinition method) {
            _MMD.LogVerbose($"[RegisterDetour] {targetMethod.GetFindableID()} ({targetMethod.MetadataToken.RID}) -> {method.GetFindableID()} ({method.MetadataToken.RID})");
            _Detours.Add(Tuple.Create(targetMethod.MetadataToken.ToInt32(), method.MetadataToken.ToInt32()));
        }

        public void Apply() {
            using (MemoryStream asmStream = new MemoryStream()) {
                _Mod.Write(asmStream);
                _Assembly = Assembly.Load(asmStream.GetBuffer());
            }
            _Module = _Assembly.GetModule(_Mod.Name);

            foreach (Tuple<int, int> tuple in _Detours) {
                _MMD.LogVerbose($"[Apply] [Detour] {tuple.Item1} -> {tuple.Item2}");
                _MMD.RuntimeTargetModule.ResolveMethod(tuple.Item1)
                    .Detour(_Module.ResolveMethod(tuple.Item2));
            }

            foreach (Tuple<int, int> tuple in _Trampolines) {
                _MMD.LogVerbose($"[Apply] [Trampoline] {tuple.Item2} -> {tuple.Item1}");
                _Module.ResolveMethod(tuple.Item2)
                    .Detour(RuntimeDetour.CreateTrampoline(_MMD.RuntimeTargetModule.ResolveMethod(tuple.Item1)));
            }
        }

        public void Revert() {
            // TODO: [MMDetourer] Implement undetouring
        }

    }
}
