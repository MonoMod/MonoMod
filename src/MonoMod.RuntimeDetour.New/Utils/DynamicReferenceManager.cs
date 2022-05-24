using Mono.Cecil.Cil;
using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace MonoMod.RuntimeDetour.Utils {
    internal static class DynamicReferenceManager {

        public struct CellRef {
            public int Index { get; internal set; }
            public int Hash { get; internal set; }

            public CellRef(int idx, int hash) {
                Index = idx;
                Hash = hash;
            }
        }

        private class Cell {
            public object? Value;
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

        public static DataScope<CellRef> AllocReference(object? value, out CellRef cellRef) {
            Holder holder;
            Cell?[] arr;
            int nextEmpty;
            Cell cell;

            cellRef = default;

            do {
                do {
                    holder = Volatile.Read(ref CellHolder);

                    arr = holder.Cells;
                    nextEmpty = holder.FirstEmpty;
                    if (nextEmpty >= arr.Length) {
                        Array.Resize(ref arr, arr.Length * 2);
                    }

                    cellRef.Index = nextEmpty++;
                    while (nextEmpty < arr.Length && arr[nextEmpty] is not null)
                        nextEmpty++;

                    cell = new Cell() { Value = value };
                    cellRef.Hash = cell.GetHashCode();
                } while (Interlocked.CompareExchange(ref arr[cellRef.Index], cell, null) is not null);
            } while (Interlocked.CompareExchange(ref CellHolder, new Holder(arr, nextEmpty), holder) != holder);

            return new(ScopeHandler.Instance, cellRef);
        }

        private sealed class ScopeHandler : ScopeHandlerBase<CellRef> {
            public static readonly ScopeHandler Instance = new();
            public override void EndScope(CellRef data) {
                Holder holder;
                Cell?[] arr;
                int nextEmpty;
                Cell? cell;

                do {
                    do {
                        holder = Volatile.Read(ref CellHolder);

                        arr = holder.Cells;
                        cell = Volatile.Read(ref arr[data.Index]);
                        if (cell?.GetHashCode() != data.Hash) {
                            return;
                        }

                        nextEmpty = Math.Min(holder.FirstEmpty, data.Index);
                    } while (Interlocked.CompareExchange(ref arr[data.Index], null, cell) != cell);
                } while (Interlocked.CompareExchange(ref CellHolder, new Holder(arr, nextEmpty), holder) != holder);
            }
        }

        private static ref object? GetCellRef(CellRef cellRef) {
            var holder = Volatile.Read(ref CellHolder);
            var cell = Volatile.Read(ref holder.Cells[cellRef.Index]);
            if (cell is null || cell.GetHashCode() != cellRef.Hash) {
                throw new ArgumentException("Referenced cell no longer exists", nameof(cellRef));
            }
            return ref cell.Value;
        }

        public static object? GetValue(CellRef cellRef) => GetCellRef(cellRef);
        public static T? GetValue<T>(CellRef cellRef) => (T?) GetCellRef(cellRef);

        private static readonly MethodInfo Self_GetValue_ii
            = typeof(DynamicReferenceManager).GetMethod(nameof(GetValue), BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null)
            ?? throw new InvalidOperationException("GetValue doesn't exist?!?!?!?");
        private static readonly MethodInfo Self_GetValueT_ii
            = typeof(DynamicReferenceManager).GetMethod(nameof(GetValueT), BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null)
            ?? throw new InvalidOperationException("GetValueT doesn't exist?!?!?!?");
        internal static object? GetValue(int index, int hash) => GetValue(new(index, hash));
        internal static T? GetValueT<T>(int index, int hash) => GetValue<T>(new(index, hash));

        public static void SetValue(CellRef cellRef, object? value) => GetCellRef(cellRef) = value;
        public static void SetValue<T>(CellRef cellRef, T? value) {
            ref var cell = ref GetCellRef(cellRef);
            if (cell is null) {
                // cell doesn't have an existing object
                // so we assign and auto-box, as needed
                cell = value;
            } else {
                // otherwise, we get a ref to the instance already on the heap, and update
                ref var tcell = ref ILHelpers.UnboxAnyUnsafe<T>(ref cell);
                tcell = value;
            }
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
    }

    internal static class NullTest {
        public static ref T? GetRef<T>(ref object? obj) {
            throw new NotImplementedException();
        }
    }
}
