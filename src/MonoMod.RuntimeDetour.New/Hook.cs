using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Core;
using MonoMod.Core.Utils;
using MonoMod.RuntimeDetour.Utils;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace MonoMod.RuntimeDetour {
    public class Hook : IDetour, IDisposable {

        private readonly IDetourFactory factory;
        IDetourFactory IDetour.Factory => factory;

        public DetourConfig? Config { get; }

        public MethodBase Source { get; }
        public MethodInfo Target { get; }

        private readonly MethodInfo realTarget;
        MethodInfo IDetour.InvokeTarget => realTarget;


        private readonly MethodInfo trampoline;
        MethodBase IDetour.NextTrampoline => trampoline;

        private object? managerData;
        object? IDetour.ManagerData { get => managerData; set => managerData = value; }

        private readonly DetourManager.DetourState state;

        private readonly DataScope<DynamicReferenceManager.CellRef> delegateObjectScope;

        public Hook(MethodBase source, MethodInfo target, object? targetObject, IDetourFactory factory, DetourConfig? config, bool applyByDefault) {
            Helpers.ThrowIfArgumentNull(source);
            Helpers.ThrowIfArgumentNull(target);
            Helpers.ThrowIfArgumentNull(factory);

            this.factory = factory;
            Config = config;
            Source = source;
            Target = target;

            trampoline = TrampolinePool.Rent(MethodSignature.ForMethod(source));

            realTarget = PrepareRealTarget(targetObject, out delegateObjectScope);

            state = DetourManager.GetDetourState(source);

            if (applyByDefault) {
                Apply();
            }
        }

        private sealed class HookData {
            public readonly object? Target;
            public readonly Delegate? InvokeNext;
            public HookData(object? target, Delegate? invokeNext) {
                Target = target;
                InvokeNext = invokeNext;
            }
        }

        private static readonly FieldInfo HookData_Target = typeof(HookData).GetField(nameof(HookData.Target))!;
        private static readonly FieldInfo HookData_InvokeNext = typeof(HookData).GetField(nameof(HookData.InvokeNext))!;

        private MethodInfo PrepareRealTarget(object? target, out DataScope<DynamicReferenceManager.CellRef> scope) {
            var srcSig = MethodSignature.ForMethod(Source);
            var trampSig = MethodSignature.ForMethod(trampoline);
            var dstSig = MethodSignature.ForMethod(Target, ignoreThis: true); // the dest sig we don't want to consider its this param

            if (target is null && !Target.IsStatic) {
                throw new ArgumentException("Target method is static, but the target object is not null");
            }

            Type? nextDelegateType = null;
            if (dstSig.ParameterCount == srcSig.ParameterCount + 1) {
                // the dest method has a delegate as its first parameter
                nextDelegateType = dstSig.FirstParameter;
                Helpers.DAssert(nextDelegateType is not null);
                dstSig = new MethodSignature(dstSig.ReturnType, dstSig.Parameters.Skip(1));
            }

            if (!srcSig.IsCompatibleWith(dstSig)) {
                throw new ArgumentException("Target method is not compatible with source method");
            }

            var delegateInvoke = nextDelegateType?.GetMethod("Invoke");
            if (delegateInvoke is not null) {
                // we want to check that the delegate invoke is also compatible with the source sig
                var invokeSig = MethodSignature.ForMethod(delegateInvoke, ignoreThis: true);
                if (!invokeSig.IsCompatibleWith(trampSig)) {
                    throw new ArgumentException("Target method's delegate parameter is not compatible with the source method");
                }
            }

            var hookData = new HookData(target,
                nextDelegateType is not null
                ? trampoline.CreateDelegate(nextDelegateType)
                : null);

            using (var dmd = srcSig.CreateDmd($"Hook<{Target.GetID()}>")) {
                var module = dmd.Module;
                var method = dmd.Definition;
                var il = dmd.GetILProcessor();

                var dataLoc = new VariableDefinition(module.ImportReference(typeof(HookData)));
                il.Body.Variables.Add(dataLoc);

                scope = il.EmitNewTypedReference(hookData, out _);
                il.Emit(OpCodes.Stloc, dataLoc);

                // first load the target object, if needed
                if (!Target.IsStatic) {
                    il.Emit(OpCodes.Ldloc, dataLoc);
                    il.Emit(OpCodes.Ldfld, module.ImportReference(HookData_Target));

                    var declType = Target.DeclaringType;
                    if (declType is not null) {
                        if (declType.IsValueType) {
                            il.Emit(OpCodes.Unbox, module.ImportReference(declType));
                        } else {
                            // the cast should be redundant
                            //il.Emit(OpCodes.Castclass, module.ImportReference(declType));
                        }
                    }
                }
                
                // then load the delegate, if needed
                if (nextDelegateType is not null) {
                    il.Emit(OpCodes.Ldloc, dataLoc);
                    il.Emit(OpCodes.Ldfld, module.ImportReference(HookData_InvokeNext));
                }

                // then load all of our arguments
                foreach (var p in method.Parameters) {
                    il.Emit(OpCodes.Ldarg, p.Index);
                }

                // then call our taret method
                il.Emit(OpCodes.Call, Target);
                il.Emit(OpCodes.Ret);

                return dmd.Generate();
            }
        }

        private void CheckDisposed() {
            if (disposedValue)
                throw new ObjectDisposedException(ToString());
        }

        public void Apply() {
            CheckDisposed();
            if (IsApplied)
                return;
            Volatile.Write(ref isApplied, true);
            state.AddDetour(this);
        }

        public void Undo() {
            CheckDisposed();
            if (!IsApplied)
                return;
            Volatile.Write(ref isApplied, value: false);
            state.RemoveDetour(this);
        }


        private bool disposedValue;
        public bool IsValid => !disposedValue;

        private bool isApplied;
        public bool IsApplied => Volatile.Read(ref isApplied);

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                Undo();
                delegateObjectScope.Dispose();

                if (disposing) {
                    TrampolinePool.Return(trampoline);
                }

                disposedValue = true;
            }
        }

        ~Hook()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
