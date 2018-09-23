using System;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using System.ComponentModel;
using Mono.Cecil.Cil;
using System.Linq;
using System.Reflection.Emit;
using SREmit = System.Reflection.Emit;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using OpCode = Mono.Cecil.Cil.OpCode;

namespace MonoMod.RuntimeDetour.HookGen {
    public class HookILCursor {

        private readonly static List<object> References = new List<object>();
        private readonly static Dictionary<int, DynamicMethod> DelegateInvokers = new Dictionary<int, DynamicMethod>();
        public static object GetReference(int id) => References[id];
        public static void SetReference(int id, object obj) => References[id] = obj;
        private static int AddReference(object obj) {
            lock (References) {
                References.Add(obj);
                return References.Count - 1;
            }
        }
        public static void FreeReference(int id) {
            References[id] = null;
            if (DelegateInvokers.ContainsKey(id))
                DelegateInvokers.Remove(id);
        }

        private readonly static MethodInfo _GetReference = typeof(HookILCursor).GetMethod("GetReference");

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
                Instr = value == Instrs.Count ? null : Instrs[value];
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

        public void Remove() {
            int index = Index;
            IL.Remove(Instr);
            Index = index;
        }

        public void MarkLabel(HookILLabel label) {
            label.Instr = Instr;
        }

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
        /// Emit a reference to an arbitrary object. Note that the references "leak" unless you use HookILCursor.FreeReference(id).
        /// </summary>
        public int EmitReference<T>(T obj) {
            Type t = typeof(T);
            int id = AddReference(obj);
            Emit(OpCodes.Ldc_I4, id);
            Emit(OpCodes.Call, _GetReference);
            if (t.IsValueType)
                Emit(OpCodes.Unbox_Any, t);
            return id;
        }

        /// <summary>
        /// Emit an inline delegate reference and invocation. Note that the delegates "leak" unless you use HookILCursor.FreeReference(id).
        /// </summary>
        public int EmitDelegate(Action cb)
            => EmitDelegateInvoke(EmitDelegatePush(cb));

        /// <summary>
        /// Emit an inline delegate reference and invocation. Note that the delegates "leak" unless you use HookILCursor.FreeReference(id).
        /// </summary>
        public int EmitDelegate<T>(T cb) where T : Delegate {
            Instruction instrPrev = Instr;
            int id = EmitDelegatePush(cb);

            // Create a DynamicMethod that shifts the stack around a little.

            Type delType = References[id].GetType();
            MethodInfo delInvokeOrig = delType.GetMethod("Invoke");

            ParameterInfo[] args = delInvokeOrig.GetParameters();
            Type[] argTypes = new Type[args.Length + 1];
            for (int i = 0; i < args.Length; i++)
                argTypes[i] = args[i].ParameterType;
            argTypes[args.Length] = delType;

            DynamicMethod dmInvoke = new DynamicMethod(
                "HookIL:Invoke:" + delInvokeOrig.DeclaringType.FullName,
                delInvokeOrig.ReturnType, argTypes,
                true // If any random errors pop up, try setting this to false first.
            );
            ILGenerator il = dmInvoke.GetILGenerator();

            // Load the delegate reference first.
            il.Emit(SREmit.OpCodes.Ldarg, args.Length);
            // Load any other arguments on top of that.
            for (int i = 0; i < args.Length; i++) 
                il.Emit(SREmit.OpCodes.Ldarg, i);
            // Invoke the delegate and return its result.
            il.Emit(SREmit.OpCodes.Callvirt, delInvokeOrig);
            il.Emit(SREmit.OpCodes.Ret);

            dmInvoke = dmInvoke.Pin();

            // Invoke the DynamicMethod.
            DelegateInvokers[id] = dmInvoke;
            Emit(OpCodes.Call, dmInvoke); // DynamicMethodDefinition should pass it through.

            return id;
        }

        /// <summary>
        /// Emit an inline delegate reference. Note that the delegates "leak" unless you use HookILCursor.FreeReference(id).
        /// </summary>
        public int EmitDelegatePush<T>(T cb) where T : Delegate
            => EmitReference(cb);

        /// <summary>
        /// Emit a delegate invocation.
        /// </summary>
        public int EmitDelegateInvoke(int id) {
            Emit(OpCodes.Callvirt, References[id].GetType().GetMethod("Invoke"));
            return id;
        }

        #endregion

    }
}
