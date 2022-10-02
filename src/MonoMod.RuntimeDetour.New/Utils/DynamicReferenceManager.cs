using Mono.Cecil.Cil;
using MonoMod.Backports;
using MonoMod.Core.Utils;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MonoMod.RuntimeDetour.Utils {
    // TODO: move this into MonoMod.Utils
    public static class DynamicReferenceManager {

        public struct CellRef {
            public int Index { get; internal set; }
            public int Hash { get; internal set; }

            public CellRef(int idx, int hash) {
                Index = idx;
                Hash = hash;
            }
        }

        private const nuint RefValueCell = 0;
        private const nuint ValueTypeCell = 1;

        private abstract class Cell {
            public readonly nuint Type;
            protected Cell(nuint type) => Type = type;
        }

        private class RefCell : Cell {
            public RefCell() : base(RefValueCell) { }
            public object? Value;
        }

        private abstract class ValueCellBase : Cell {
            public ValueCellBase() : base(ValueTypeCell) { }
            public abstract object? BoxValue();
        }

        private class ValueCell<T> : ValueCellBase {
            public T? Value;
            public override object? BoxValue() => Value!;
        }

        private sealed class Holder {
            public readonly Cell?[] Cells;
            public readonly int FirstEmpty;

            public Holder(Cell?[] cells, int firstEmpty) {
                Cells = cells;
                FirstEmpty = firstEmpty;
            }
        }

        private static Holder CellHolder = new(new Cell?[16], 0); // default to holding 16 cells
        private static readonly object lockObj = new();
        private static int useLock;

        private const int IterLimit = 16;

        private static unsafe void DoUpdateCellList(delegate*<void*, ref Cell?[], ref int, out Cell?, out Cell?, out bool, int> doUpdate, void* data) {
            Holder holder;
            Cell?[] arr;
            int nextEmpty;
            int updateIndex;
            var lockTaken = false;
            var triedTakingLock = false;

            Cell? newVal, comparand;

            try {
                var iters = 0;
                do {
                    do {
                        if (++iters > IterLimit || Volatile.Read(ref useLock) > 0) {
                            triedTakingLock = true;
                            // first increment useLock to tell other threads to use the lock
                            _ = Interlocked.Increment(ref useLock);
                            // then take the lock, possibly waiting for other threads
                            MonitorEx.Enter(lockObj, ref lockTaken);
                        }

                        holder = Volatile.Read(ref CellHolder);
                        arr = holder.Cells;
                        nextEmpty = holder.FirstEmpty;

                        updateIndex = doUpdate(data, ref arr, ref nextEmpty, out newVal, out comparand, out var brk);
                        if (brk)
                            return;
                    } while (Interlocked.CompareExchange(ref arr[updateIndex], newVal, comparand) != comparand);
                } while (Interlocked.CompareExchange(ref CellHolder, new Holder(arr, nextEmpty), holder) != holder);
            } finally {
                if (lockTaken) {
                    Monitor.Exit(lockObj);
                }
                // ensure that we decrement useLock if we incremented it
                if (triedTakingLock) {
                    _ = Interlocked.Decrement(ref useLock);
                }
            }
        }

        private unsafe struct AllocReferenceCoreData {
            public void* CreateCell;
            public void* CreateCellData;
            public void* CellRef;
        }

        // TODO: through sheer bad luck this can loop infinitely, fix it, posisbly with a SpinWait, or falling back to a lock
        private static unsafe DataScope<CellRef> AllocReferenceCore(delegate*<void*, Cell> createCell, void* data, out CellRef cellRef) {
            cellRef = default;

            var arcd = new AllocReferenceCoreData {
                CreateCell = createCell,
                CreateCellData = data,
                CellRef = Unsafe.AsPointer(ref cellRef)
            };

            DoUpdateCellList(&DoUpdate, Unsafe.AsPointer(ref arcd));

            static int DoUpdate(void* pdata, ref Cell?[] arr, ref int nextEmpty, out Cell? cell, out Cell? comparand, out bool brk) {
                brk = false;
                ref var data = ref Unsafe.AsRef<AllocReferenceCoreData>(pdata);
                ref var cellRef = ref Unsafe.AsRef<CellRef>(data.CellRef);

                if (nextEmpty >= arr.Length) {
                    Array.Resize(ref arr, arr.Length * 2);
                }

                cellRef.Index = nextEmpty++;
                while (nextEmpty < arr.Length && arr[nextEmpty] is not null)
                    nextEmpty++;

                cell = ((delegate*<void*, Cell>)data.CreateCell)(data.CreateCellData);
                cellRef.Hash = cell.GetHashCode();
                comparand = null;
                return cellRef.Index;
            }

            return new(ScopeHandler.Instance, cellRef);
        }

        private static unsafe DataScope<CellRef> AllocReferenceClass(object? value, out CellRef cellRef) {
            static Cell Create(void* data) => new RefCell { Value = Unsafe.AsRef<object?>(data) };
            return AllocReferenceCore(&Create, Unsafe.AsPointer(ref value), out cellRef);
        }

        private static unsafe DataScope<CellRef> AllocReferenceStruct<T>(in T value, out CellRef cellRef) {
            static Cell Create(void* data) => new ValueCell<T> { Value = Unsafe.AsRef<T>(data) };
            return AllocReferenceCore(&Create, Unsafe.AsPointer(ref Unsafe.AsRef(in value)), out cellRef);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveOptimization)]
        public static DataScope<CellRef> AllocReference<T>(in T? value, out CellRef cellRef) {
            if (default(T) == null) {
                return AllocReferenceClass(Unsafe.As<T?, object?>(ref Unsafe.AsRef(in value)), out cellRef);
            } else {
                return AllocReferenceStruct(in value, out cellRef);
            }
        }

        private sealed class ScopeHandler : ScopeHandlerBase<CellRef> {
            public static readonly ScopeHandler Instance = new();
            public override unsafe void EndScope(CellRef data) {
                DoUpdateCellList(&DoUpdate, Unsafe.AsPointer(ref data));

                static int DoUpdate(void* pdata, ref Cell?[] arr, ref int nextEmpty, out Cell? newVal, out Cell? comparand, out bool brk) {
                    brk = false;
                    ref var data = ref Unsafe.AsRef<CellRef>(pdata);

                    var cell = Volatile.Read(ref arr[data.Index]);
                    if (cell?.GetHashCode() != data.Hash) {
                        brk = true;
                        newVal = null;
                        comparand = null;
                        return 0;
                    }

                    nextEmpty = Math.Min(nextEmpty, data.Index);

                    newVal = null;
                    comparand = cell;
                    return data.Index;
                }
            }
        }

        private static Cell GetCell(CellRef cellRef) {
            var holder = Volatile.Read(ref CellHolder);
            var cell = Volatile.Read(ref holder.Cells[cellRef.Index]);
            if (cell is null || cell.GetHashCode() != cellRef.Hash) {
                throw new ArgumentException("Referenced cell no longer exists", nameof(cellRef));
            }
            return cell;
        }

        public static object? GetValue(CellRef cellRef) {
            var cell = GetCell(cellRef);
            switch (cell.Type) {
                case RefValueCell: {
                        var c = Unsafe.As<RefCell>(cell);
                        return c.Value;
                    }
                case ValueTypeCell: {
                        var c = Unsafe.As<ValueCellBase>(cell);
                        return c.BoxValue();
                    }
                default:
                    throw new InvalidOperationException("Cell is not of valid type");
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveOptimization)]
        private static ref T? GetValueRef<T>(CellRef cellRef) {
            var cell = GetCell(cellRef);
            switch (cell.Type) {
                case RefValueCell: {
                        Helpers.Assert(default(T) == null);
                        var c = Unsafe.As<RefCell>(cell);
                        return ref Unsafe.As<object?, T?>(ref c.Value!);
                    }
                case ValueTypeCell: {
                        Helpers.Assert(default(T) != null);
                        var c = (ValueCell<T>) cell;
                        return ref c.Value;
                    }
                default:
                    throw new InvalidOperationException("Cell is not of valid type");
            }
        }

        public static T? GetValue<T>(CellRef cellRef) => GetValueRef<T>(cellRef);

        private static readonly MethodInfo Self_GetValue_ii
            = typeof(DynamicReferenceManager).GetMethod(nameof(GetValue), BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null)
            ?? throw new InvalidOperationException("GetValue doesn't exist?!?!?!?");
        private static readonly MethodInfo Self_GetValueT_ii
            = typeof(DynamicReferenceManager).GetMethod(nameof(GetValueT), BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null)
            ?? throw new InvalidOperationException("GetValueT doesn't exist?!?!?!?");
        internal static object? GetValue(int index, int hash) => GetValue(new(index, hash));
        internal static T? GetValueT<T>(int index, int hash) => GetValue<T>(new(index, hash));

        public static void SetValue<T>(CellRef cellRef, in T? value) {
            ref var cell = ref GetValueRef<T>(cellRef);
            cell = value;
        }

        public static void EmitLoadReference(this ILProcessor il, CellRef cellRef) {
            Helpers.ThrowIfArgumentNull(il);

            il.Emit(OpCodes.Ldc_I4, cellRef.Index);
            il.Emit(OpCodes.Ldc_I4, cellRef.Hash);
            il.Emit(OpCodes.Call, il.Body.Method.Module.ImportReference(Self_GetValue_ii));
        }

        public static void EmitLoadTypedReference(this ILProcessor il, CellRef cellRef, Type type) {
            Helpers.ThrowIfArgumentNull(il);

            il.Emit(OpCodes.Ldc_I4, cellRef.Index);
            il.Emit(OpCodes.Ldc_I4, cellRef.Hash);
            il.Emit(OpCodes.Call, il.Body.Method.Module.ImportReference(Self_GetValueT_ii.MakeGenericMethod(type)));
        }

        public static DataScope<CellRef> EmitNewReference(this ILProcessor il, object? value, out CellRef cellRef) {
            var scope = AllocReference(value, out cellRef);
            EmitLoadReference(il, cellRef);
            return scope;
        }

        public static DataScope<CellRef> EmitNewTypedReference<T>(this ILProcessor il, T? value, out CellRef cellRef) {
            var scope = AllocReference(value, out cellRef);
            EmitLoadTypedReference(il, cellRef, typeof(T));
            return scope;
        }
        // TODO: make EmitNewTypedReference do an 'unsafe' load which bypasses most of the checks in GetValueRef
    }
}
