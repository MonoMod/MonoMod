using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Collections;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace MonoMod.Packer.Utilities {
    internal sealed class ConstructorScanner {
        private readonly TypeEntityMap map;
        private readonly TypeDefinition type;
        private readonly Dictionary<FieldDefinition, FieldInitializer?> fieldInitializers = new();
        private readonly bool hasStaticFields;
        private readonly bool hasInstanceFields;

        private bool hasScannedStatic;
        private bool hasScannedInstance;

        public ConstructorScanner(TypeEntityMap map, TypeDefinition type) {
            this.map = map;
            this.type = type;

            foreach (var field in type.Fields) {
                if (field.IsStatic) {
                    hasStaticFields = true;
                } else {
                    hasInstanceFields = true;
                }

                if (hasStaticFields && hasInstanceFields)
                    break;
            }
        }

        // if true but null, then we found what look like multiple conflicting initializers
        public bool TryGetInitializer(FieldDefinition field, out FieldInitializer? initializer) {
            Helpers.DAssert(field.DeclaringType == type);

            if (field.IsStatic) {
                ScanStatic();
            } else {
                ScanInstance();
            }

            var lockTaken = false;
            try {
                if ((!hasScannedInstance && hasInstanceFields) || (!hasScannedStatic && hasStaticFields)) {
                    // we need to lock, because the dict might be modified
                    Monitor.Enter(fieldInitializers, ref lockTaken);
                }

                if (fieldInitializers.TryGetValue(field, out initializer)) {
                    return true;
                } else {
                    return false;
                }

            } finally {
                if (lockTaken) {
                    Monitor.Exit(fieldInitializers);
                }
            }
        }

        private void ScanStatic() {
            if (hasScannedStatic) {
                return;
            }

            lock (fieldInitializers) {
                if (hasScannedStatic) {
                    return;
                }

                ScanStaticCore();
                hasScannedStatic = true;
            }
        }

        private void ScanInstance() {
            if (hasScannedInstance) {
                return;
            }

            lock (fieldInitializers) {
                if (hasScannedInstance) {
                    return;
                }

                ScanInstanceCore();
                hasScannedInstance = true;
            }
        }

        private void ScanStaticCore() {
            var cctor = type.GetStaticConstructor();
            if (cctor is null) {
                // no cctor, no more work to do
                return;
            }

            var body = cctor.CilMethodBody;
            if (body is null) {
                // cctor has no body??
                map.Diagnostics.ReportDiagnostic(Diagnostics.ErrorCode.WRN_CtorHasNoIlBody, cctor);
                return;
            }

            ScanCtorForFieldInitializers(body, CilOpCodes.Stsfld, allowDuplicate: false);
        }

        private void ScanInstanceCore() {
            // note: we actually want to scan all ctors, because Roslyn will duplicate initialization code into all declared ctors
            var ctors = type.Methods.Where(m => !m.IsStatic && m.IsConstructor);

            foreach (var ctor in ctors) {
                var body = ctor.CilMethodBody;
                if (body is null) {
                    map.Diagnostics.ReportDiagnostic(Diagnostics.ErrorCode.WRN_CtorHasNoIlBody, ctor);
                    continue;
                }

                // allowDuplicate: true will attempt to merge identical intializers
                ScanCtorForFieldInitializers(body, CilOpCodes.Stfld, allowDuplicate: true);
            }
        }

        private void ScanCtorForFieldInitializers(CilMethodBody body, CilOpCode stfldOp, bool allowDuplicate) {
            Helpers.DAssert(stfldOp == CilOpCodes.Stfld || stfldOp == CilOpCodes.Stsfld);

            // When we scan, we bascially want to scan for the value to store to a stfld opcode (i.e. we're scanning stack ops in a fairly simple manner)
            // If we see an empty-stack ctor call, we skip, because that call is always placed after field initializers

            // instruction *offset* -> list of incoming stacks
            var stackDict = new Dictionary<int, List<ImmutableStack<int>>>();
            // this stack holds the index of the first instruction in the "chain" that produced the value it represents
            var currentStack = ImmutableStack.Create<int>();

            for (var i = 0; i < body.Instructions.Count; i++) {
                var instr = body.Instructions[i];
                if (stackDict.TryGetValue(instr.Offset, out var incoming)) {
                    if (currentStack is not null) {
                        incoming.Add(currentStack);
                    }

                    if (incoming.Count == 0) {
                        Helpers.Assert(false, $"empty incoming? {instr}, {body.Owner.FullName}");
                    } else if (incoming.Count == 1) {
                        currentStack = incoming[0];
                    } else {
                        // incoming.Count > 1
                        var arrs = incoming.Select(s => s.ToArray()).ToArray();
                        Helpers.Assert(arrs.All(a => a.Length == arrs[0].Length));
                        var resultStack = ImmutableStack.Create<int>();
                        for (var j = arrs[0].Length - 1; j >= 0; j--) {
                            var min = int.MaxValue;
                            foreach (var a in arrs) {
                                min = int.Min(a[j], min);
                            }
                            resultStack = resultStack.Push(min);
                        }
                        currentStack = resultStack;
                    }
                }

                Helpers.Assert(currentStack is not null);

                if (instr.OpCode == CilOpCodes.Call && instr.Operand is IMethodDescriptor method) {
                    if (map.ExternalMdResolver.ResolveMethod(method) is { IsConstructor: true }) {
                        // this is a 'call' ins to a ctor; we're done with the currently scanned ctor
                        return;
                    }
                }

                if (instr.OpCode == stfldOp) {
                    Helpers.DAssert(instr.OpCode.StackBehaviourPop is CilStackBehaviour.Pop1 or CilStackBehaviour.PopRef_Pop1);
                    // top of stack is what we actually care about, always
                    // if this is a stfld instead of a stsfld, the next op is the ldarg.0, but we *probably* don't need to care about that
                    ProcessInitializer(body, currentStack.Peek(), currentStack.Pop() is { IsEmpty: false } popped ? popped.Peek() : -1, i, allowDuplicate);
                }

                int? leastChainedPush = null;
                // handle stack pops
                switch (instr.OpCode.StackBehaviourPop) {
                    case CilStackBehaviour.Pop0:
                        break;
                    // pop 1
                    case CilStackBehaviour.Pop1:
                    case CilStackBehaviour.PopI:
                    case CilStackBehaviour.PopRef:
                        leastChainedPush = currentStack.Peek();
                        currentStack = currentStack.Pop();
                        break;
                    // pop 2
                    case CilStackBehaviour.Pop1_Pop1:
                    case CilStackBehaviour.PopI_Pop1:
                    case CilStackBehaviour.PopI_PopI:
                    case CilStackBehaviour.PopI_PopI8:
                    case CilStackBehaviour.PopI_PopR4:
                    case CilStackBehaviour.PopI_PopR8:
                    case CilStackBehaviour.PopRef_Pop1:
                    case CilStackBehaviour.PopRef_PopI:
                        currentStack = currentStack.Pop(out var a).Pop(out var b);
                        leastChainedPush = int.Min(a, b);
                        break;
                    // pop 3
                    case CilStackBehaviour.PopI_PopI_PopI:
                    case CilStackBehaviour.PopRef_PopI_PopI:
                    case CilStackBehaviour.PopRef_PopI_PopI8:
                    case CilStackBehaviour.PopRef_PopI_PopR4:
                    case CilStackBehaviour.PopRef_PopI_PopR8:
                    case CilStackBehaviour.PopRef_PopI_PopRef:
                    case CilStackBehaviour.PopRef_PopI_Pop1:
                        currentStack = currentStack.Pop(out a).Pop(out b).Pop(out var c);
                        leastChainedPush = int.Min(int.Min(a, b), c);
                        break;
                    // pop all
                    case CilStackBehaviour.PopAll:
                        while (!currentStack.IsEmpty) {
                            currentStack = currentStack.Pop(out var v);
                            leastChainedPush = int.Min(leastChainedPush ?? int.MaxValue, v);
                        }
                        break;
                    // pop var amount
                    case CilStackBehaviour.VarPop:
                        var popAmount = instr.GetStackPopCount(body);
                        for (; popAmount > 0; popAmount--) {
                            currentStack = currentStack.Pop(out var v);
                            leastChainedPush = int.Min(leastChainedPush ?? int.MaxValue, v);
                        }
                        break;

                    default:
                        throw new InvalidOperationException($"unknown pop behavior {instr.OpCode.StackBehaviourPop}");
                }

                var pushAddr = leastChainedPush ?? i;
                for (var j = instr.GetStackPushCount(); j > 0; j--) {
                    currentStack = currentStack.Push(pushAddr);
                }

                switch (instr.OpCode.FlowControl) {
                    case CilFlowControl.Branch:
                        var keepStack = false;
                        goto HandleBranch;
                    case CilFlowControl.ConditionalBranch:
                        keepStack = true;
                        goto HandleBranch;

                        HandleBranch:
                        var targetOffsets = instr.Operand switch {
                            ICilLabel label => new[] { label.Offset },
                            IList<ICilLabel> labels => labels.Select(l => l.Offset),
                            int offs => new[] { offs },
                            sbyte offs => new[] { (int) offs },
                            _ => throw new InvalidOperationException()
                        };

                        foreach (var offs in targetOffsets) {
                            if (offs <= instr.Offset) {
                                // this is a backward jump; fail out
                                map.Diagnostics.ReportDiagnostic(Diagnostics.ErrorCode.WRN_BackwardJumpInFieldInitializer, body.Owner);
                                return; // TODO: is there some way to recover from this? there should be, right?
                            }
                            if (!stackDict.TryGetValue(offs, out var list)) {
                                stackDict.Add(offs, list = new());
                            }
                            list.Add(currentStack);
                        }
                        if (!keepStack) {
                            currentStack = null;
                        }
                        break;

                    case CilFlowControl.Call:
                    case CilFlowControl.Break:
                    case CilFlowControl.Meta:
                    case CilFlowControl.Phi:
                    case CilFlowControl.Next:
                    case CilFlowControl.Return:
                    case CilFlowControl.Throw:
                        break;
                }
            }
        }

        private object? TranslateOperand(CilInstruction ins) {
            var operand = ins.Operand;
            if (operand is null) {
                return null;
            }

            if (operand is MetadataToken) {
                throw new InvalidOperationException("the fuck am I supposed to do with a MetadataToken here?");
            }

            if (operand is IMemberDescriptor md) {
                return ComparableSignature.CreateComparableInstance(map, md);
            }

            int offset;

            if (operand is ICilLabel label) {
                offset = label.Offset;
                goto ResolveLabel;
            }

            if (operand is int i && ins.OpCode.OperandType is CilOperandType.InlineBrTarget) {
                offset = i;
                goto ResolveLabel;
            }

            if (operand is sbyte s && ins.OpCode.OperandType is CilOperandType.ShortInlineBrTarget) {
                offset = s;
                goto ResolveLabel;
            }

            // TODO: special case other token types?

            // we don't support these in field initializers
            Helpers.DAssert(operand is not Parameter and not ICilLabel and not CilLocalVariable);

            return operand;

        ResolveLabel:
            // translate labels to relative offsets
            // TODO: validate that this offset lies within the copied initializer region?
            return offset - ins.Offset;
        }

        private void ProcessInitializer(CilMethodBody body, int initializerStart, int ldThisHint, int initializerEnd, bool allowDuplicate) {
            var targetFld = body.Instructions[initializerEnd].Operand as IFieldDescriptor;
            var resolvedField = map.MdResolver.ResolveField(targetFld);
            Helpers.Assert(resolvedField is not null);
            Helpers.DAssert(resolvedField.DeclaringType == type);

            var numInsns = initializerEnd - initializerStart; // note: initializerStart is inclusive
            var builder = ImmutableArray.CreateBuilder<(CilOpCode, object?)>(numInsns);
            for (var i = initializerStart; i < initializerEnd; i++) {
                var ins = body.Instructions[i];
                builder.Add((ins.OpCode, TranslateOperand(ins)));
            }

            var initializer = new FieldInitializer(map, builder.ToImmutable());

            // TODO: make this completely ignore duplicates found in the same ctor
            // that probably means that we're looking at an explicit cctor, and the second isn't actually an initializer
            if (fieldInitializers.TryGetValue(resolvedField, out var existing)) {
                // there is an existing; what do we do?
                if (allowDuplicate) {
                    // we allow duplicate initializers, but must unify them
                    if (initializer.Equals(existing)) {
                        // the initializers are equivalent, we're good
                    } else {
                        // the initializers are not equivalent; we must explicitly set to null
                        fieldInitializers[resolvedField] = null;
                    }
                } else {
                    // we don't allow duplicates here, but found a duplicate; explicitly set it to null
                    fieldInitializers[resolvedField] = null;
                }
            } else {
                // no existing, just add it
                fieldInitializers.Add(resolvedField, initializer);
            }
        }
    }
}
