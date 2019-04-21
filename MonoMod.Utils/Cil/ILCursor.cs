using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using InstrList = Mono.Collections.Generic.Collection<Mono.Cecil.Cil.Instruction>;
using System.ComponentModel;

namespace MonoMod.Cil {
    /// <summary>
    /// Specifies where a MMILCursor should be positioned in relation to the target of a search function
    /// </summary>
    public enum MoveType {
        /// <summary>
        ///  Move the cursor before the first instruction in the match
        /// </summary>
        Before,

        /// <summary>
        /// Equivalent to Before with `cursor.MoveAfterLabels()` causing emitted instructions to become the target of incoming labels
        /// </summary>
        AfterLabel,

        /// <summary>
        ///  Move the cursor after the last instruction in the match
        /// </summary>
        After
    }

    /// <summary>
    /// Indicates whether the position of a MMILCursor is the result of a search function and 
    /// if the next search should ignore the instruction preceeding or following this cursor.
    /// <para />
    /// SearchTarget.Next is the result of searching with MoveType.Before, and SearchTarget.Prev from MoveType.After 
    /// </summary>
    public enum SearchTarget {
        None,

        /// <summary>
        /// A foward searching function cannot match the Next instruction and must move the cursor forward
        /// </summary>
        Next,

        /// <summary>
        /// A reverse searching function cannot match the Next instruction and must move the cursor backward
        /// </summary>
        Prev
    }

    public class ILCursor {
        public ILContext Context { get; }

        // private state
        private Instruction _next;
        private ILLabel[] _afterLabels;
        private SearchTarget _searchTarget;

        /// <summary>
        /// The instruction immediately following the cursor position or null if the cursor is at the end of the instruction list.
        /// </summary>
        public Instruction Next {
            get => _next;
            set => Goto(value);
        }

        /// <summary>
        /// The instruction immediately preceding the cursor position or null if the cursor is at the start of the instruction list.
        /// </summary>
        public Instruction Prev {
            get => Next == null ? Instrs[Instrs.Count - 1] : Next.Previous;
            set => Goto(value, MoveType.After);
        }

        /// <summary>
        /// The instruction immediately preceding the cursor position or null if the cursor is at the start of the instruction list.
        /// </summary>
        public Instruction Previous {
            get => Prev;
            set => Prev = value;
        }

        /// <summary>
        /// The index of the instruction immediately following the cursor position. Range: 0 to <c>Instrs.Count</c>
        /// Setter accepts negative indexing by adding <c>Instrs.Count</c> to the operand
        /// </summary>
        public int Index {
            get => Context.IndexOf(Next);
            set => Goto(value);
        }

        /// <summary>
        /// Indicates whether the position of a MMILCursor is the result of a search function and 
        /// if the next search should ignore the instruction preceeding or following this cursor.
        /// 
        /// See <see cref="Utils.SearchTarget"/>
        /// </summary>
        public SearchTarget SearchTarget {
            get => _searchTarget;
            set {
                if (value == SearchTarget.Next && Next == null || value == SearchTarget.Prev && Prev == null)
                    value = SearchTarget.None;

                _searchTarget = value;
            }
        }

        /// <summary>
        /// Enumerates all labels which point to the current instruction (<c>label.Target == Next</c>)
        /// </summary>
        public IEnumerable<ILLabel> IncomingLabels => Context.GetIncomingLabels(Next);

        // Context convenience accessors
        public MethodDefinition Method => Context.Method;
        public ILProcessor IL => Context.IL;
        public MethodBody Body => Context.Body;
        public ModuleDefinition Module => Context.Module;
        public InstrList Instrs => Context.Instrs;

        public ILCursor(ILContext context) {
            Context = context;
            Index = 0;
        }

        public ILCursor(ILCursor c) {
            Context = c.Context;
            _next = c._next;
            _searchTarget = c._searchTarget;
            _afterLabels = c._afterLabels;
        }

        public ILCursor Clone()
            => new ILCursor(this);

        public bool IsBefore(Instruction instr) => Index <= Context.IndexOf(instr);

        public bool IsAfter(Instruction instr) => Index > Context.IndexOf(instr);

        public override string ToString() {
            StringBuilder builder = new StringBuilder();

            builder.AppendLine($"// ILCursor: {Method}, {Index}, {SearchTarget}");
            ILContext.ToString(builder, Prev);
            ILContext.ToString(builder, Next);

            return builder.ToString();
        }

        #region Movement functions

        /// <summary>
        /// Moves the cursor to a target instruction. All other movements go through this.
        /// </summary>
        /// <param name="insn">The target instruction</param>
        /// <param name="moveType">Where to move in relation to the target instruction and incoming labels (branches)</param>
        /// <param name="setTarget">Whether to set the `SearchTarget` and skip the target instruction with the next search function</param>
        /// <returns>this</returns>
        public ILCursor Goto(Instruction insn, MoveType moveType = MoveType.Before, bool setTarget = false) {
            if (moveType == MoveType.After)
                // Moving past the end of the method shouldn't move any further, nor wrap around.
                _next = insn?.Next;
            else
                _next = insn;

            if (setTarget)
                _searchTarget = moveType == MoveType.After ? SearchTarget.Prev : SearchTarget.Next;
            else
                _searchTarget = SearchTarget.None;

            if (moveType == MoveType.AfterLabel)
                MoveAfterLabels();
            else
                MoveBeforeLabels();

            return this;
        }

        /// <summary>
        /// Move the cursor after incoming labels (branches). If an instruction is emitted, all labels which currently point to Next, will point to the newly emitted instruction.
        /// </summary>
        /// <returns>this</returns>
        public ILCursor MoveAfterLabels() {
            _afterLabels = IncomingLabels.ToArray();
            return this;
        }

        /// <summary>
        /// Move the cursor before incoming labels (branches). This is the default behaviour. Emitted instructions will not cause labels to change targets.
        /// </summary>
        /// <returns>this</returns>
        public ILCursor MoveBeforeLabels() {
            _afterLabels = null;
            return this;
        }

        /// <summary>
        /// Moves the cursor to a target index. Supports negative indexing. See <see cref="Goto(Instruction, MoveType, bool)"/>
        /// </summary>
        /// <returns>this</returns>
        public ILCursor Goto(int index, MoveType moveType = MoveType.Before, bool setTarget = false) {
            if (index < 0)
                index += Instrs.Count;

            return Goto(index == Instrs.Count ? null : Instrs[index], moveType, setTarget);
        }

        /// <summary>
        /// Overload for <c>Goto(label.Target)</c>. <paramref name="moveType"/> defaults to MoveType.AfterLabel
        /// </summary>
        /// <returns>this</returns>
        public ILCursor GotoLabel(ILLabel label, MoveType moveType = MoveType.AfterLabel, bool setTarget = false) =>
        Goto(label.Target, moveType, setTarget);

        /// <summary>
        /// Searches forward and moves the cursor to the next sequence of instructions matching the corresponding predicates. See also <seealso cref="TryGotoNext"/>
        /// </summary>
        /// <returns>this</returns>
        /// <exception cref="KeyNotFoundException">If no match is found</exception>
        public ILCursor GotoNext(MoveType moveType = MoveType.Before, params Func<Instruction, bool>[] predicates) {
            if (!TryGotoNext(moveType, predicates))
                throw new KeyNotFoundException();

            return this;
        }

        /// <summary>
        /// Searches forward and moves the cursor to the next sequence of instructions matching the corresponding predicates.
        /// </summary>
        /// <returns>True if a match was found</returns>
        public bool TryGotoNext(MoveType moveType = MoveType.Before, params Func<Instruction, bool>[] predicates) {
            InstrList instrs = Instrs;
            int i = Index;
            if (SearchTarget == SearchTarget.Next)
                i++;

            for (; i + predicates.Length <= instrs.Count; i++) {
                for (int j = 0; j < predicates.Length; j++)
                    if (!(predicates[j]?.Invoke(instrs[i + j]) ?? true))
                        goto Next;

                Goto(moveType == MoveType.After ? i + predicates.Length - 1 : i, moveType, true);
                return true;

                Next:
                continue;
            }
            return false;
        }

        /// <summary>
        /// Searches backward and moves the cursor to the next sequence of instructions matching the corresponding predicates. See also <seealso cref="TryGotoPrev"/>
        /// </summary>
        /// <returns>this</returns>
        /// <exception cref="KeyNotFoundException">If no match is found</exception>
        public ILCursor GotoPrev(MoveType moveType = MoveType.Before, params Func<Instruction, bool>[] predicates) {
            if (!TryGotoPrev(moveType, predicates))
                throw new KeyNotFoundException();

            return this;
        }

        /// <summary>
        /// Searches backward and moves the cursor to the next sequence of instructions matching the corresponding predicates.
        /// </summary>
        /// <returns>True if a match was found</returns>
        public bool TryGotoPrev(MoveType moveType = MoveType.Before, params Func<Instruction, bool>[] predicates) {
            InstrList instrs = Instrs;
            int i = Index - 1;
            if (SearchTarget == SearchTarget.Prev)
                i--;
            i = Math.Min(i, instrs.Count - predicates.Length);

            for (; i >= 0; i--) {
                for (int j = 0; j < predicates.Length; j++)
                    if (!(predicates[j]?.Invoke(instrs[i + j]) ?? true))
                        goto Next;

                Goto(moveType == MoveType.After ? i + predicates.Length - 1 : i, moveType, true);
                return true;

                Next:
                continue;
            }
            return false;
        }

        // manual overloads for params + default args
        public ILCursor GotoNext(params Func<Instruction, bool>[] predicates) => GotoNext(MoveType.Before, predicates);
        public bool TryGotoNext(params Func<Instruction, bool>[] predicates) => TryGotoNext(MoveType.Before, predicates);
        public ILCursor GotoPrev(params Func<Instruction, bool>[] predicates) => GotoPrev(MoveType.Before, predicates);
        public bool TryGotoPrev(params Func<Instruction, bool>[] predicates) => TryGotoPrev(MoveType.Before, predicates);

        #endregion

        #region Movement functions

        /// <summary>
        /// Find the next occurences of a series of instructions matching the given set of predicates with gaps permitted.
        /// </summary>
        /// <param name="cursors">An array of cursors corresponding to each found instruction (MoveType.Before)</param>
        /// <exception cref="KeyNotFoundException">If no match is found</exception>
        public void FindNext(out ILCursor[] cursors, params Func<Instruction, bool>[] predicates) {
            if (!TryFindNext(out cursors, predicates))
                throw new KeyNotFoundException();
        }

        /// <summary>
        /// Find the next occurences of a series of instructions matching the given set of predicates with gaps permitted.
        /// </summary>
        /// <param name="cursors">An array of cursors corresponding to each found instruction (MoveType.Before)</param>
        /// <returns>True if a match was found</returns>
        public bool TryFindNext(out ILCursor[] cursors, params Func<Instruction, bool>[] predicates) {
            cursors = new ILCursor[predicates.Length];
            ILCursor c = this;
            for (int i = 0; i < predicates.Length; i++) {
                c = c.Clone();
                if (!c.TryGotoNext(predicates[i]))
                    return false;

                cursors[i] = c;
            }
            return true;
        }

        /// <summary>
        /// Searches backwards for occurences of a series of instructions matching the given set of predicates with gaps permitted.
        /// </summary>
        /// <param name="cursors">An array of cursors corresponding to each found instruction (MoveType.Before)</param>
        /// <exception cref="KeyNotFoundException">If no match is found</exception>
        public void FindPrev(out ILCursor[] cursors, params Func<Instruction, bool>[] predicates) {
            if (!TryFindPrev(out cursors, predicates))
                throw new KeyNotFoundException();
        }

        /// <summary>
        /// Searches backwards for occurences of a series of instructions matching the given set of predicates with gaps permitted.
        /// </summary>
        /// <param name="cursors">An array of cursors corresponding to each found instruction (MoveType.Before)</param>
        /// <returns>True if a match was found</returns>
        public bool TryFindPrev(out ILCursor[] cursors, params Func<Instruction, bool>[] predicates) {
            cursors = new ILCursor[predicates.Length];
            ILCursor c = this;
            for (int i = predicates.Length - 1; i >= 0; i--) {
                c = c.Clone();
                if (!c.TryGotoPrev(predicates[i]))
                    return false;

                cursors[i] = c;
            }
            return true;
        }

        #endregion

        #region IL Manipulation

        /// <summary>
        /// Sets the target of a label to the current position (<c>label.Target = Next</c>) and moves after it.
        /// </summary>
        /// <param name="label">The label to mark</param>
        public void MarkLabel(ILLabel label) {
            if (label == null)
                label = new ILLabel(Context);

            label.Target = Next;
            if (_afterLabels != null) {
                Array.Resize(ref _afterLabels, _afterLabels.Length+1);
                _afterLabels[_afterLabels.Length-1] = label;
            }
            else {
                _afterLabels = new[] { label };
            }
        }

        /// <summary>
        /// Creates a new label targetting the current position (<c>label.Target = Next</c>) and moves after it.
        /// </summary>
        /// <returns>The newly created label</returns>
        public ILLabel MarkLabel() {
            ILLabel label = DefineLabel();
            MarkLabel(label);
            return label;
        }

        /// <summary>
        /// Create a new label for use with <see cref="MarkLabel"/>
        /// </summary>
        /// <returns>A new label with no target</returns>
        public ILLabel DefineLabel() => Context.DefineLabel();

        private ILCursor _Insert(Instruction instr) {
            Instrs.Insert(Index, instr);
            _Retarget(instr, MoveType.After);
            return this;
        }

        /// <summary>
        /// Removes the Next instruction
        /// </summary>
        public ILCursor Remove() {
            int index = Index;
            _Retarget(Next.Next, MoveType.Before);
            Instrs.RemoveAt(index);
            return this;
        }

        /// <summary>
        /// Removes several instructions
        /// </summary>
        public ILCursor RemoveRange(int num) {
            int index = Index;
            _Retarget(Instrs[index+num], MoveType.Before);
            while (num-- > 0) // TODO: currently requires O(n) removals, shifting the backing array each time
                Instrs.RemoveAt(index);
            return this;
        }

        /// <summary>
        /// Moves the cursor and all labels the cursor is positioned after to a target instruction
        /// </summary>
        private void _Retarget(Instruction next, MoveType moveType) {
            if (_afterLabels != null)
                foreach (ILLabel label in _afterLabels)
                    label.Target = next;
            Goto(next, moveType);
        }

        public ILCursor Emit(OpCode opcode, ParameterDefinition parameter)
            => _Insert(IL.Create(opcode, parameter));
        public ILCursor Emit(OpCode opcode, VariableDefinition variable)
            => _Insert(IL.Create(opcode, variable));
        public ILCursor Emit(OpCode opcode, Instruction[] targets)
            => _Insert(IL.Create(opcode, targets));
        public ILCursor Emit(OpCode opcode, Instruction target)
            => _Insert(IL.Create(opcode, target));
        public ILCursor Emit(OpCode opcode, double value)
            => _Insert(IL.Create(opcode, value));
        public ILCursor Emit(OpCode opcode, float value)
            => _Insert(IL.Create(opcode, value));
        public ILCursor Emit(OpCode opcode, long value)
            => _Insert(IL.Create(opcode, value));
        public ILCursor Emit(OpCode opcode, sbyte value)
            => _Insert(IL.Create(opcode, value));
        public ILCursor Emit(OpCode opcode, byte value)
            => _Insert(IL.Create(opcode, value));
        public ILCursor Emit(OpCode opcode, string value)
            => _Insert(IL.Create(opcode, value));
        public ILCursor Emit(OpCode opcode, FieldReference field)
            => _Insert(IL.Create(opcode, field));
        public ILCursor Emit(OpCode opcode, CallSite site)
            => _Insert(IL.Create(opcode, site));
        public ILCursor Emit(OpCode opcode, TypeReference type)
            => _Insert(IL.Create(opcode, type));
        public ILCursor Emit(OpCode opcode)
            => _Insert(IL.Create(opcode));
        public ILCursor Emit(OpCode opcode, int value)
            => _Insert(IL.Create(opcode, value));
        public ILCursor Emit(OpCode opcode, MethodReference method)
            => _Insert(IL.Create(opcode, method));
        public ILCursor Emit(OpCode opcode, FieldInfo field)
            => _Insert(IL.Create(opcode, field));
        public ILCursor Emit(OpCode opcode, MethodBase method)
            => _Insert(IL.Create(opcode, method));
        public ILCursor Emit(OpCode opcode, Type type)
            => _Insert(IL.Create(opcode, type));
        public ILCursor Emit(OpCode opcode, object operand)
            => _Insert(IL.Create(opcode, operand));

        public ILCursor Emit<T>(OpCode opcode, string memberName)
            => _Insert(IL.Create(opcode, typeof(T).GetMember(memberName, (BindingFlags) (-1)).FirstOrDefault()));

        #endregion

        #region Reference-oriented Emit Helpers

        /// <summary>
        /// Bind an arbitary object to an ILContext for static retrieval. See <see cref="ILContext.AddReference{T}(T)"/>
        /// </summary>
        public int AddReference<T>(T t) => Context.AddReference(t);

        /// <summary>
        /// Emits the IL to retrieve a stored reference of type <typeparamref name="T"/> with the given <paramref name="id"/> and place it on the stack.
        /// </summary>
        public void EmitGetReference<T>(int id) {
            Emit(OpCodes.Ldc_I4, id);
            Emit(OpCodes.Call, Context.ReferenceBag.GetGetter<T>());
        }

        /// <summary>
        /// Store an object in the reference store, and emit the IL to retrieve it and place it on the stack.
        /// </summary>
        public int EmitReference<T>(T t) {
            int id = AddReference(t);
            EmitGetReference<T>(id);
            return id;
        }

        /// <summary>
        /// Emit the IL to invoke a delegate as if it were a method. Stack behaviour matches OpCodes.Call
        /// </summary>
        public int EmitDelegate<T>(T cb) where T : Delegate {
            if (cb.GetInvocationList().Length == 1 && cb.Target == null) { // optimisation for static delegates
                Emit(OpCodes.Call, cb.GetMethodInfo());
                return -1;
            }

            int id = EmitReference(cb);

            MethodInfo delInvoke = typeof(T).GetMethod("Invoke");
            MethodInfo invoker = Context.ReferenceBag.GetDelegateInvoker<T>();
            if (invoker != null) {
                // Prevent the invoker from getting GC'd early, f.e. when it's a DynamicMethod.
                AddReference(invoker);
                Emit(OpCodes.Call, invoker);
            } else {
                Emit(OpCodes.Callvirt, delInvoke);
            }

            return id;
        }

        #endregion

    }
}
