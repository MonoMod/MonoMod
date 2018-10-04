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
    public sealed class HookILLabel {

        private readonly HookIL HookIL;
        internal int ID;
        internal bool Marked;

        public Instruction Target {
            get {
                if (HookIL._LabelMapIDTarget.TryGetValue(ID, out Instruction target))
                    return target;
                return null;
            }
            set {
                int idOld = ID;
                bool wasMarked = Marked;
                Marked = true;

                if (HookIL._LabelMapIDTarget.ContainsKey(ID)) {
                    // Remove the previous instruction -> ID mapping.
                    HookIL._LabelMapTargetID.Remove(Target);
                }

                if (value != null) {
                    // Find if there already is an ID pointing towards the new target.
                    if (HookIL._LabelMapTargetID.TryGetValue(value, out ID)) {
                        if (wasMarked) {
                            foreach (HookILLabel label in HookIL._Labels)
                                if (label.Marked && label.ID == idOld)
                                    label.ID = ID;
                        }
                        return;
                    }

                    // No existing mapping found. Map the instruction to a new ID.
                    ID = ++HookIL._LabelID;
                    HookIL._LabelMapTargetID[value] = ID;

                } else {
                    // End of body - map to 0 instead.
                    ID = 0;
                    if (wasMarked) {
                        foreach (HookILLabel label in HookIL._Labels)
                            if (label.ID == idOld)
                                label.ID = 0;
                    }
                    return;
                }

                // Remap all labels with the same ID to refer to the new instruction.
                HookIL._LabelMapIDTarget[ID] = value;
            }
        }
        
        public List<HookILCursor> Branches {
            get {
                List<HookILCursor> branches = new List<HookILCursor>();
                foreach (Instruction instr in HookIL.Instrs)
                    if (instr.Operand is HookILLabel label && label.ID == ID)
                        branches.Add(new HookILCursor(HookIL, instr));
                return branches;
            }
        }

        internal HookILLabel(HookIL hookil) {
            HookIL = hookil;
            HookIL._Labels.Add(this);
        }

        internal HookILLabel(HookIL hookil, Instruction target) {
            HookIL = hookil;
            HookIL._Labels.Add(this);
            Target = target;
        }

    }
}
