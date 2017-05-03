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
using System.Reflection.Emit;

namespace MonoMod.Detour {
    public sealed class MonoModDetourerLevel {

        private readonly static MethodInfo m_DynamicMethod_Finalize =
            // Mono
            typeof(DynamicMethod).GetMethod("Finalize", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        private readonly static DynamicMethodDelegate dmd_DynamicMethod_Finalize =
            m_DynamicMethod_Finalize?.CreateDelegate();

        internal readonly MonoModDetourer _MMD;
        internal readonly ModuleDefinition _Mod;

        public readonly string Name;

        internal readonly HashSet<Tuple<int, int>> _Detours = new HashSet<Tuple<int, int>>();
        internal readonly HashSet<Tuple<int, int>> _Trampolines = new HashSet<Tuple<int, int>>();
        internal readonly LongDictionary<int> _DetourLevels = new LongDictionary<int>();
        internal readonly LongDictionary<DynamicMethod> _TrampolineDMs = new LongDictionary<DynamicMethod>();

        internal Assembly _Assembly;
        internal Module _Module;

        internal MonoModDetourerLevel(MonoModDetourer mmd, ModuleDefinition mod) {
            _MMD = mmd;
            _Mod = mod;
        }

        public void RegisterTrampoline(MethodDefinition targetMethod, MethodDefinition method) {
            _MMD.LogVerbose($"[RegisterTrampoline] {method.GetFindableID()} ({method.MetadataToken.ToInt32()}) -> {targetMethod.GetFindableID()} ({targetMethod.MetadataToken.ToInt32()})");
            _Trampolines.Add(Tuple.Create(targetMethod.MetadataToken.ToInt32(), method.MetadataToken.ToInt32()));
        }

        public void RegisterDetour(MethodDefinition targetMethod, MethodDefinition method) {
            _MMD.LogVerbose($"[RegisterDetour] {targetMethod.GetFindableID()} ({targetMethod.MetadataToken.ToInt32()}) -> {method.GetFindableID()} ({method.MetadataToken.ToInt32()})");
            _Detours.Add(Tuple.Create(targetMethod.MetadataToken.ToInt32(), method.MetadataToken.ToInt32()));
        }

        public void Apply() {
            using (MemoryStream asmStream = new MemoryStream()) {
                _Mod.Write(asmStream);
                _Assembly = Assembly.Load(asmStream.GetBuffer());
            }

            /**//*
            using (FileStream debugStream = File.OpenWrite(Path.Combine(
                _MMD.DependencyDirs[0], $"{_Mod.Name.Substring(0, _Mod.Name.Length - 4)}.MonoModDetourer.dll")))
                _Mod.Write(debugStream);
            /**/

            _Module = _Assembly.GetModule(_Mod.Name);

            foreach (Tuple<int, int> tuple in _Detours)
                ApplyDetour(_MMD.RuntimeTargetModule.ResolveMethod(tuple.Item1), _Module.ResolveMethod(tuple.Item2));

            foreach (Tuple<int, int> tuple in _Trampolines)
                ApplyTrampoline(_Module.ResolveMethod(tuple.Item2), _MMD.RuntimeTargetModule.ResolveMethod(tuple.Item1));
        }

        public void ApplyDetour(MethodBase from, MethodBase to) {
            _MMD.LogVerbose($"[ApplyDetour] {from} -> {to}");
            from.Detour(to);
            _DetourLevels[(long)
                ((ulong) from.MetadataToken) << 32 |
                ((uint) to.MetadataToken)
            ] = from.GetDetourLevel();
        }

        public unsafe void ApplyTrampoline(MethodBase from, MethodBase to) {
            DynamicMethod trampoline = to.CreateTrampoline();
            _MMD.LogVerbose($"[ApplyTrampoline] {from} -> {to} ({trampoline})");
            _MMD.LogVerbose($"[ApplyTrampoline] 0x{((ulong) RuntimeDetour.GetMethodStart(from)).ToString("X16")} -> 0x{((ulong) RuntimeDetour.GetMethodStart(trampoline)).ToString("X16")}");

            RuntimeDetour.Detour(
                RuntimeDetour.GetMethodStart(from),
                RuntimeDetour.GetMethodStart(trampoline),
            false);
            _TrampolineDMs[(long)
                ((ulong) from.MetadataToken) << 32 |
                ((uint ) to.MetadataToken)
            ] = trampoline;
        }

        public void Revert() {
            foreach (Tuple<int, int> tuple in _Detours)
                RevertDetour(_MMD.RuntimeTargetModule.ResolveMethod(tuple.Item1), _Module.ResolveMethod(tuple.Item2));

            foreach (Tuple<int, int> tuple in _Trampolines)
                RevertTrampoline(_Module.ResolveMethod(tuple.Item2), _MMD.RuntimeTargetModule.ResolveMethod(tuple.Item1));
        }

        public void RevertDetour(MethodBase from, MethodBase to) {
            _MMD.LogVerbose($"[RevertDetour] {from} -> {to}");
            from.Undetour(_DetourLevels[(long)
                ((ulong) from.MetadataToken) << 32 |
                ((uint) to.MetadataToken)
            ]);
        }

        public void RevertTrampoline(MethodBase from, MethodBase to) {
            _MMD.LogVerbose($"[RevertTrampoline] {from} -> {to}");
            if (dmd_DynamicMethod_Finalize == null)
                return;
            dmd_DynamicMethod_Finalize(_TrampolineDMs[(long)
                ((ulong) from.MetadataToken) << 32 |
                ((uint ) to.MetadataToken)
            ]);
        }

    }
}
