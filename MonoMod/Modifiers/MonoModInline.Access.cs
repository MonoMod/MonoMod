using MonoMod;
using System;
using System.Reflection;

namespace MMILAccess {
    public abstract class AccessBase<T> where T : AccessBase<T> {
        static AccessBase() {
            throw new InvalidOperationException("One shouldn't access MMILAccessBase at runtime!");
        }

        public object Call(params object[] args) { return null; }
        public TReturn Call<TReturn>(params object[] args) { return default(TReturn); }
        public TValue Get<TValue>() { return default(TValue); }
        public void Set<TValue>(TValue value) { }
    }

    public sealed class StaticAccess : AccessBase<StaticAccess> {
        static StaticAccess() {
            throw new InvalidOperationException("One shouldn't access MMILStaticAccess at runtime!");
        }
        public StaticAccess(string type, string name) { }

        public object New(params object[] args) { return null; }
    }
    public sealed class StaticAccess<TSelf> : AccessBase<StaticAccess<TSelf>> {
        static StaticAccess() {
            throw new InvalidOperationException("One shouldn't access MMILStaticAccess at runtime!");
        }
        public StaticAccess(string name) { }
        public StaticAccess(string type, string name) { }

        public TSelf New(params object[] args) { return default(TSelf); }
    }

    public sealed class Access : AccessBase<Access> {
        static Access() {
            throw new InvalidOperationException("One shouldn't access MMILAccess at runtime!");
        }
        public Access(object self, string type, string name) { }
    }
    public sealed class Access<TSelf> : AccessBase<Access<TSelf>> {
        static Access() {
            throw new InvalidOperationException("One shouldn't access MMILAccess at runtime!");
        }
        public Access(object self, string name) { }
        public Access(object self, string type, string name) { }
    }

    public abstract class BatchAccessBase<T> where T : BatchAccessBase<T> {
        static BatchAccessBase() {
            throw new InvalidOperationException("One shouldn't access MMILBatchAccessBase at runtime!");
        }

        public MethodBase[] AllMethods => null;
        public FieldInfo[] AllFields => null;

        public T With(params string[] list) { return null; }
        public T Without(params string[] list) { return null; }
        public void CopyTo(object target) { }
    }

    public sealed class BatchAccess : BatchAccessBase<BatchAccess> {
        static BatchAccess() {
            throw new InvalidOperationException("One shouldn't access MMILBatchAccess at runtime!");
        }
        public BatchAccess(string type) { }
        public BatchAccess(object self, string type) { }
    }
    public sealed class BatchAccess<TSelf> : BatchAccessBase<BatchAccess<TSelf>> {
        static BatchAccess() {
            throw new InvalidOperationException("One shouldn't access MMILBatchAccess at runtime!");
        }
        public BatchAccess() { }
        public BatchAccess(string type) { }
        public BatchAccess(object self) { }
        public BatchAccess(object self, string type) { }
    }

}
