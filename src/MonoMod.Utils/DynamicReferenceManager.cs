using Mono.Cecil.Cil;
using MonoMod.Backports;
using MonoMod.Cil;
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using COpCodes = Mono.Cecil.Cil.OpCodes;
using ROpCodes = System.Reflection.Emit.OpCodes;

namespace MonoMod.Utils
{
    public record struct DynamicReferenceCell
    {
        public int Index { get; internal set; }
        public int Hash { get; internal set; }

        public DynamicReferenceCell(int idx, int hash)
        {
            Index = idx;
            Hash = hash;
        }
    }

    public static class DynamicReferenceManager
    {

        private const nuint RefValueCell = 0;
        private const nuint ValueTypeCell = 1;

        private abstract class Cell
        {
            public readonly nuint Type;
            protected Cell(nuint type) => Type = type;
        }

        private class RefCell : Cell
        {
            public RefCell() : base(RefValueCell) { }
            public object? Value;
        }

        private abstract class ValueCellBase : Cell
        {
            public ValueCellBase() : base(ValueTypeCell) { }
            public abstract object? BoxValue();
        }

        private class ValueCell<T> : ValueCellBase
        {
            public T? Value;
            public override object? BoxValue() => Value!;
        }

        private static SpinLock writeLock = new(false);
        // TODO: maybe move cells inline, or use blocks of 16 or 32 cells in one object
        private static volatile Cell?[] cells = new Cell?[16];
        private static volatile int firstEmptyCell;

        private static unsafe DataScope<DynamicReferenceCell> AllocReferenceCore(Cell cell, out DynamicReferenceCell cellRef)
        {
            cellRef = default;

            var lockTaken = false;
            try
            {
                writeLock.Enter(ref lockTaken);
                // while we have the lock, no other thread will be modifying this array

                var array = cells;
                var emptyCell = firstEmptyCell;

                if (emptyCell >= array.Length)
                {
                    // need to realloc
                    var newAlloc = new Cell?[array.Length * 2];
                    Array.Copy(array, newAlloc, array.Length);
                    // still want to atomically write the new array so readers always have a consistent view
                    cells = array = newAlloc;
                }

                // now we're safe to actually allocate a cell
                var idx = emptyCell++;
                while (emptyCell < array.Length && array[emptyCell] is not null)
                    emptyCell++;
                firstEmptyCell = emptyCell;

                // once we've decided on a cell to use, write it and continue on our way
                Volatile.Write(ref array[idx], cell);
                cellRef = new(idx, cell.GetHashCode());

                // the allocation is complete
            }
            finally
            {
                if (lockTaken)
                    writeLock.Exit();
            }

            return new(ScopeHandler.Instance, cellRef);
        }

        private static unsafe DataScope<DynamicReferenceCell> AllocReferenceClass(object? value, out DynamicReferenceCell cellRef)
        {
            return AllocReferenceCore(new RefCell { Value = value }, out cellRef);
        }

        private static unsafe DataScope<DynamicReferenceCell> AllocReferenceStruct<T>(in T value, out DynamicReferenceCell cellRef)
        {
            return AllocReferenceCore(new ValueCell<T> { Value = value }, out cellRef);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveOptimization)]
        public static DataScope<DynamicReferenceCell> AllocReference<T>(in T? value, out DynamicReferenceCell cellRef)
        {
            if (default(T) == null)
            {
                return AllocReferenceClass(Unsafe.As<T?, object?>(ref Unsafe.AsRef(in value)), out cellRef);
            }
            else
            {
                return AllocReferenceStruct(in value, out cellRef);
            }
        }

        private sealed class ScopeHandler : ScopeHandlerBase<DynamicReferenceCell>
        {
            public static readonly ScopeHandler Instance = new();
            public override unsafe void EndScope(DynamicReferenceCell data)
            {

                var lockTaken = false;
                try
                {
                    writeLock.Enter(ref lockTaken);

                    var array = cells;
                    var cell = Volatile.Read(ref array[data.Index]);
                    if (cell is null || cell.GetHashCode() != data.Hash)
                    {
                        // the cell is wrong, and this is somehow the second dispose... don't do anything
                        return;
                    }

                    // the cell is correct, zero it
                    Volatile.Write(ref array[data.Index], null);
                    // mark this cell as the first empty cell if there isn't one before it
                    firstEmptyCell = Math.Min(firstEmptyCell, data.Index);

                    // and we're done
                }
                finally
                {
                    if (lockTaken)
                        writeLock.Exit();
                }

            }
        }

        private static Cell GetCell(DynamicReferenceCell cellRef)
        {
            var cell = Volatile.Read(ref cells[cellRef.Index]);
            if (cell is null || cell.GetHashCode() != cellRef.Hash)
            {
                throw new ArgumentException("Referenced cell no longer exists", nameof(cellRef));
            }
            return cell;
        }

        public static object? GetValue(DynamicReferenceCell cellRef)
        {
            var cell = GetCell(cellRef);
            switch (cell.Type)
            {
                case RefValueCell:
                    {
                        var c = Unsafe.As<RefCell>(cell);
                        return c.Value;
                    }
                case ValueTypeCell:
                    {
                        var c = Unsafe.As<ValueCellBase>(cell);
                        return c.BoxValue();
                    }
                default:
                    throw new InvalidOperationException("Cell is not of valid type");
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveOptimization)]
        private static ref T? GetValueRef<T>(DynamicReferenceCell cellRef)
        {
            var cell = GetCell(cellRef);
            switch (cell.Type)
            {
                case RefValueCell:
                    {
                        Helpers.Assert(default(T) == null);
                        var c = Unsafe.As<RefCell>(cell);
                        Helpers.Assert(c.Value is null or T);
                        return ref Unsafe.As<object?, T?>(ref c.Value!);
                    }
                case ValueTypeCell:
                    {
                        Helpers.Assert(default(T) != null);
                        var c = (ValueCell<T>)cell;
                        return ref c.Value;
                    }
                default:
                    throw new InvalidOperationException("Cell is not of valid type");
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveOptimization)]
        private static ref T? GetValueRefUnsafe<T>(DynamicReferenceCell cellRef)
        {
            var cell = GetCell(cellRef);
            // here, we're assuming that our T is correct, hence Unsafe
            if (default(T) == null)
            {
                // this is a reference type
                Helpers.DAssert(cell.Type == RefValueCell);
                var c = Unsafe.As<RefCell>(cell);
                return ref Unsafe.As<object?, T?>(ref c.Value);
            }
            else
            {
                // this is a value type
                Helpers.DAssert(cell.Type == ValueTypeCell);
                var c = Unsafe.As<ValueCell<T>>(cell);
                return ref c.Value;
            }
        }

        public static T? GetValue<T>(DynamicReferenceCell cellRef) => GetValueRef<T>(cellRef);

        private static readonly MethodInfo Self_GetValue_ii
            = typeof(DynamicReferenceManager).GetMethod(nameof(GetValue), BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null)
            ?? throw new InvalidOperationException("GetValue doesn't exist?!?!?!?");
        private static readonly MethodInfo Self_GetValueT_ii
            = typeof(DynamicReferenceManager).GetMethod(nameof(GetValueT), BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null)
            ?? throw new InvalidOperationException("GetValueT doesn't exist?!?!?!?");
        private static readonly MethodInfo Self_GetValueTUnsafe_ii
            = typeof(DynamicReferenceManager).GetMethod(nameof(GetValueTUnsafe), BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null)
            ?? throw new InvalidOperationException("GetValueTUnsafe doesn't exist?!?!?!?");

        internal static object? GetValue(int index, int hash) => GetValue(new(index, hash));
        internal static T? GetValueT<T>(int index, int hash) => GetValue<T>(new(index, hash));
        internal static T? GetValueTUnsafe<T>(int index, int hash) => GetValueRefUnsafe<T>(new(index, hash));

        public static void SetValue<T>(DynamicReferenceCell cellRef, in T? value)
        {
            ref var cell = ref GetValueRef<T>(cellRef);
            cell = value;
        }

        public static void EmitLoadReference(this ILProcessor il, DynamicReferenceCell cellRef)
        {
            Helpers.ThrowIfArgumentNull(il);

            il.Emit(COpCodes.Ldc_I4, cellRef.Index);
            il.Emit(COpCodes.Ldc_I4, cellRef.Hash);
            il.Emit(COpCodes.Call, il.Body.Method.Module.ImportReference(Self_GetValue_ii));
        }

        public static void EmitLoadReference(this ILCursor il, DynamicReferenceCell cellRef)
        {
            Helpers.ThrowIfArgumentNull(il);

            il.Emit(COpCodes.Ldc_I4, cellRef.Index);
            il.Emit(COpCodes.Ldc_I4, cellRef.Hash);
            il.Emit(COpCodes.Call, il.Body.Method.Module.ImportReference(Self_GetValue_ii));
        }

        public static void EmitLoadReference(this ILGenerator il, DynamicReferenceCell cellRef)
        {
            Helpers.ThrowIfArgumentNull(il);

            il.Emit(ROpCodes.Ldc_I4, cellRef.Index);
            il.Emit(ROpCodes.Ldc_I4, cellRef.Hash);
            il.Emit(ROpCodes.Call, Self_GetValue_ii);
        }

        public static void EmitLoadTypedReference(this ILProcessor il, DynamicReferenceCell cellRef, Type type)
        {
            Helpers.ThrowIfArgumentNull(il);

            il.Emit(COpCodes.Ldc_I4, cellRef.Index);
            il.Emit(COpCodes.Ldc_I4, cellRef.Hash);
            il.Emit(COpCodes.Call, il.Body.Method.Module.ImportReference(Self_GetValueT_ii.MakeGenericMethod(type)));
        }

        public static void EmitLoadTypedReference(this ILCursor il, DynamicReferenceCell cellRef, Type type)
        {
            Helpers.ThrowIfArgumentNull(il);

            il.Emit(COpCodes.Ldc_I4, cellRef.Index);
            il.Emit(COpCodes.Ldc_I4, cellRef.Hash);
            il.Emit(COpCodes.Call, il.Body.Method.Module.ImportReference(Self_GetValueT_ii.MakeGenericMethod(type)));
        }

        public static void EmitLoadTypedReference(this ILGenerator il, DynamicReferenceCell cellRef, Type type)
        {
            Helpers.ThrowIfArgumentNull(il);

            il.Emit(ROpCodes.Ldc_I4, cellRef.Index);
            il.Emit(ROpCodes.Ldc_I4, cellRef.Hash);
            il.Emit(ROpCodes.Call, Self_GetValueT_ii.MakeGenericMethod(type));
        }

        internal static void EmitLoadTypedReferenceUnsafe(this ILProcessor il, DynamicReferenceCell cellRef, Type type)
        {
            Helpers.ThrowIfArgumentNull(il);

            il.Emit(COpCodes.Ldc_I4, cellRef.Index);
            il.Emit(COpCodes.Ldc_I4, cellRef.Hash);
            il.Emit(COpCodes.Call, il.Body.Method.Module.ImportReference(Self_GetValueTUnsafe_ii.MakeGenericMethod(type)));
        }

        internal static void EmitLoadTypedReferenceUnsafe(this ILCursor il, DynamicReferenceCell cellRef, Type type)
        {
            Helpers.ThrowIfArgumentNull(il);

            il.Emit(COpCodes.Ldc_I4, cellRef.Index);
            il.Emit(COpCodes.Ldc_I4, cellRef.Hash);
            il.Emit(COpCodes.Call, il.Body.Method.Module.ImportReference(Self_GetValueTUnsafe_ii.MakeGenericMethod(type)));
        }

        internal static void EmitLoadTypedReferenceUnsafe(this ILGenerator il, DynamicReferenceCell cellRef, Type type)
        {
            Helpers.ThrowIfArgumentNull(il);

            il.Emit(ROpCodes.Ldc_I4, cellRef.Index);
            il.Emit(ROpCodes.Ldc_I4, cellRef.Hash);
            il.Emit(ROpCodes.Call, Self_GetValueTUnsafe_ii.MakeGenericMethod(type));
        }

        public static DataScope<DynamicReferenceCell> EmitNewReference(this ILProcessor il, object? value, out DynamicReferenceCell cellRef)
        {
            var scope = AllocReference(value, out cellRef);
            EmitLoadReference(il, cellRef);
            return scope;
        }

        public static DataScope<DynamicReferenceCell> EmitNewReference(this ILCursor il, object? value, out DynamicReferenceCell cellRef)
        {
            var scope = AllocReference(value, out cellRef);
            EmitLoadReference(il, cellRef);
            return scope;
        }

        public static DataScope<DynamicReferenceCell> EmitNewReference(this ILGenerator il, object? value, out DynamicReferenceCell cellRef)
        {
            var scope = AllocReference(value, out cellRef);
            EmitLoadReference(il, cellRef);
            return scope;
        }

        public static DataScope<DynamicReferenceCell> EmitNewTypedReference<T>(this ILProcessor il, T? value, out DynamicReferenceCell cellRef)
        {
            var scope = AllocReference(value, out cellRef);
            EmitLoadTypedReferenceUnsafe(il, cellRef, typeof(T));
            return scope;
        }

        public static DataScope<DynamicReferenceCell> EmitNewTypedReference<T>(this ILCursor il, T? value, out DynamicReferenceCell cellRef)
        {
            var scope = AllocReference(value, out cellRef);
            EmitLoadTypedReferenceUnsafe(il, cellRef, typeof(T));
            return scope;
        }

        public static DataScope<DynamicReferenceCell> EmitNewTypedReference<T>(this ILGenerator il, T? value, out DynamicReferenceCell cellRef)
        {
            var scope = AllocReference(value, out cellRef);
            EmitLoadTypedReferenceUnsafe(il, cellRef, typeof(T));
            return scope;
        }
    }
}
