using System;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using System.ComponentModel;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using System.Linq;

namespace MonoMod.RuntimeDetour.HookGen {
    public class HookILCursor {

        public HookIL HookIL { get; private set; }
        public Instruction Instr { get; set; }

        public MethodBody Body => HookIL.Body;
        public ILProcessor IL => HookIL.IL;

        public MethodDefinition Method => HookIL.Method;
        public ModuleDefinition Module => HookIL.Module;
        public Mono.Collections.Generic.Collection<Instruction> Instrs => HookIL.Instrs;

        public int Index {
            get {
                int index = Instrs.IndexOf(Instr);
                if (index == -1)
                    index = Instrs.Count;
                return index;
            }
            set {
                Instr = Instrs[value];
            }
        }

        internal HookILCursor(HookIL hookil, Instruction instr) {
            HookIL = hookil;
            Instr = instr;
        }

        public HookILCursor(HookILCursor old) {
            HookIL = old.HookIL;
            Instr = old.Instr;
        }

        public HookILCursor Clone()
            => new HookILCursor(this);

        #region Misc IL Helpers

        public bool GotoNext(Func<Mono.Collections.Generic.Collection<Instruction>, int, bool> predicate) {
            Mono.Collections.Generic.Collection<Instruction> instrs = Instrs;
            try {
                for (int i = Index + 1; i < instrs.Count; i++) {
                    if (predicate(instrs, i)) {
                        Index = i;
                        return true;
                    }
                }
            } catch {
                // Fail silently.
            }
            return false;
        }

        public bool GotoPrev(Func<Mono.Collections.Generic.Collection<Instruction>, int, bool> predicate) {
            Mono.Collections.Generic.Collection<Instruction> instrs = Instrs;
            try {
                for (int i = Index - 1; i > -1; i--) {
                    if (predicate(instrs, i)) {
                        Index = i;
                        return true;
                    }
                }
            } catch {
                // Fail silently.
            }
            return false;
        }

        public bool GotoNext(params Func<Instruction, bool>[] predicates) {
            Mono.Collections.Generic.Collection<Instruction> instrs = Instrs;
            try {
                for (int i = Index + 1; i + predicates.Length - 1 < instrs.Count; i++) {
                    bool match = true;
                    for (int j = 0; j < predicates.Length; j++) {
                        if (!(predicates[j]?.Invoke(instrs[i + j]) ?? true)) {
                            match = false;
                            break;
                        }
                    }
                    if (match) {
                        Index = i;
                        return true;
                    }
                }
            } catch {
                // Fail silently.
            }
            return false;
        }

        public bool GotoPrev(params Func<Instruction, bool>[] predicates) {
            Mono.Collections.Generic.Collection<Instruction> instrs = Instrs;
            try {
                int i = Index - 1;
                int overhang = i + predicates.Length - 1 - instrs.Count;
                if (overhang > 0)
                    i -= overhang;
                for (; i > -1; i--) {
                    bool match = true;
                    for (int j = 0; j < predicates.Length; j++) {
                        if (!(predicates[j]?.Invoke(instrs[i + j]) ?? true)) {
                            match = false;
                            break;
                        }
                    }
                    if (match) {
                        Index = i;
                        return true;
                    }
                }
            } catch {
                // Fail silently.
            }
            return false;
        }

        public bool IsBefore(Instruction instr) {
            int indexOther = Instrs.IndexOf(instr);
            if (indexOther == -1)
                indexOther = Instrs.Count;
            return Index < indexOther;
        }

        public bool IsAfter(Instruction instr) {
            int indexOther = Instrs.IndexOf(instr);
            if (indexOther == -1)
                indexOther = Instrs.Count;
            return indexOther < Index;
        }

        public void ReplaceOperands(object from, object to) {
            foreach (Instruction instr in Instrs)
                if (instr.Operand?.Equals(from) ?? from == null)
                    instr.Operand = to;
        }

        #endregion

        #region Base Create / Emit Helpers

        private void _Insert(Instruction instr) {
            Instrs.Insert(Index, instr);
        }

        public void Emit(OpCode opcode, ParameterDefinition parameter)
            => _Insert(IL.Create(opcode, parameter));
        public void Emit(OpCode opcode, VariableDefinition variable)
            => _Insert(IL.Create(opcode, variable));
        public void Emit(OpCode opcode, Instruction[] targets)
            => _Insert(IL.Create(opcode, targets));
        public void Emit(OpCode opcode, Instruction target)
            => _Insert(IL.Create(opcode, target));
        public void Emit(OpCode opcode, double value)
            => _Insert(IL.Create(opcode, value));
        public void Emit(OpCode opcode, float value)
            => _Insert(IL.Create(opcode, value));
        public void Emit(OpCode opcode, long value)
            => _Insert(IL.Create(opcode, value));
        public void Emit(OpCode opcode, sbyte value)
            => _Insert(IL.Create(opcode, value));
        public void Emit(OpCode opcode, byte value)
            => _Insert(IL.Create(opcode, value));
        public void Emit(OpCode opcode, string value)
            => _Insert(IL.Create(opcode, value));
        public void Emit(OpCode opcode, FieldReference field)
            => _Insert(IL.Create(opcode, field));
        public void Emit(OpCode opcode, CallSite site)
            => _Insert(IL.Create(opcode, site));
        public void Emit(OpCode opcode, TypeReference type)
            => _Insert(IL.Create(opcode, type));
        public void Emit(OpCode opcode)
            => _Insert(IL.Create(opcode));
        public void Emit(OpCode opcode, int value)
            => _Insert(IL.Create(opcode, value));
        public void Emit(OpCode opcode, MethodReference method)
            => _Insert(IL.Create(opcode, method));
        public void Emit(OpCode opcode, FieldInfo field)
            => _Insert(IL.Create(opcode, field));
        public void Emit(OpCode opcode, MethodBase method)
            => _Insert(IL.Create(opcode, method));
        public void Emit(OpCode opcode, Type type)
            => _Insert(IL.Create(opcode, type));
        public void Emit(OpCode opcode, object operand)
            => _Insert(IL.Create(opcode, operand));

        #endregion

        #region Reference-oriented Emit Helpers

        /// <summary>
        /// Emit a reference to an arbitrary object. Note that the references "leak."
        /// </summary>
        public int EmitReference<T>(T obj) {
            Type t = typeof(T);
            int id = HookExtensions.AddReference(obj);
            Emit(OpCodes.Ldc_I4, id);
            Emit(OpCodes.Call, HookExtensions._GetReference);
            if (t.IsValueType)
                Emit(OpCodes.Unbox_Any, t);
            return id;
        }

        /// <summary>
        /// Emit an inline delegate reference and invocation.
        /// </summary>
        public int EmitDelegateCall(Action cb)
            => EmitDelegateInvoke(EmitDelegatePush(cb));

        /// <summary>
        /// Emit an inline delegate reference.
        /// </summary>
        public int EmitDelegatePush<T>(T cb) where T : Delegate
            => EmitReference(cb);

        /// <summary>
        /// Emit a delegate invocation.
        /// </summary>
        public int EmitDelegateInvoke(int id) {
            Emit(OpCodes.Callvirt, HookExtensions.References[id].GetType().GetMethod("Invoke"));
            return id;
        }

        #endregion

    }
}
