using MonoMod;
using System;

namespace MMILExt {
    public class StaticAccess {
        static StaticAccess() {
            throw new InvalidOperationException("One shouldn't access MMILStaticAccess at runtime!");
        }
        public StaticAccess(string type, string name) { }

        public object New(params object[] args) { return null; }
        public void Call(params object[] args) { }
        public TReturn Call<TReturn>(params object[] args) { return default(TReturn); }
        public TValue Get<TValue>() { return default(TValue); }
        public void Set<TValue>(TValue value) { }
    }
    public class StaticAccess<TSelf> : StaticAccess {
        static StaticAccess() {
            throw new InvalidOperationException("One shouldn't access MMILStaticAccess at runtime!");
        }
        public StaticAccess(string name) : base(null, null) { }
        public StaticAccess(string type, string name) : base(null, null) { }

        public new TSelf New(params object[] args) { return default(TSelf); }
    }

    public class Access {
        static Access() {
            throw new InvalidOperationException("One shouldn't access MMILAccess at runtime!");
        }
        public Access(object self, string type, string name) { }

        public void Call(params object[] args) { }
        public TReturn Call<TReturn>(params object[] args) { return default(TReturn); }
        public TValue Get<TValue>() { return default(TValue); }
        public void Set<TValue>(TValue value) { }
    }
    public class Access<TSelf> : Access {
        static Access() {
            throw new InvalidOperationException("One shouldn't access MMILAccess at runtime!");
        }
        public Access(object self, string name) : base(null, null, null) { }
        public Access(object self, string type, string name) : base(null, null, null) { }
    }
}
