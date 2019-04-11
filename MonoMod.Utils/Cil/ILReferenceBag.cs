using System;
using System.Reflection;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using System.Linq;
using System.Collections.ObjectModel;
using InstrList = Mono.Collections.Generic.Collection<Mono.Cecil.Cil.Instruction>;
using MonoMod.Utils;
using System.Text;

namespace MonoMod.Cil {
    public interface IILReferenceBag {
        T Get<T>(int id);
        MethodInfo GetGetter<T>();
        int Store<T>(T t);
        void Clear<T>(int id);
    }

    public class NopILReferenceBag : IILReferenceBag {
        public readonly static NopILReferenceBag Instance = new NopILReferenceBag();

        private const string message = "Inline references not supported in this context";
        public T Get<T>(int id) => throw new NotSupportedException(message);
        public MethodInfo GetGetter<T>() => throw new NotSupportedException(message);
        public int Store<T>(T t) => throw new NotSupportedException(message);
        public void Clear<T>(int id) => throw new NotSupportedException(message);
    }

    public class RuntimeILReferenceBag : IILReferenceBag {
        public readonly static RuntimeILReferenceBag Instance = new RuntimeILReferenceBag();

        public T Get<T>(int id) => InnerBag<T>.Get(id);
        public MethodInfo GetGetter<T>() => InnerBag<T>.Getter;
        public int Store<T>(T t) => InnerBag<T>.Store(t);
        public void Clear<T>(int id) => InnerBag<T>.Clear(id);

        public static class InnerBag<T> {
            private static T[] array = new T[4];
            private static int count;

            public static T Get(int id) => array[id];
            public static readonly MethodInfo Getter = typeof(InnerBag<T>).GetMethod("Get");

            private static readonly object _storeLock = new object();
            public static int Store(T t) {
                lock (_storeLock) {
                    if (count == array.Length) {
                        T[] newarray = new T[array.Length * 2];
                        Array.Copy(array, newarray, array.Length);
                        array = newarray;
                    }
                    array[count] = t;
                    return count++;
                }
            }

            public static void Clear(int id) {
                lock (_storeLock)
                    array[id] = default;
            }
        }
    }
}
