using System;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    public interface IDetour : IDisposable {
        bool IsValid { get; }
        bool IsApplied { get; }

        void Apply();
        void Undo();
        void Free();

        MethodBase GenerateTrampoline(MethodBase signature = null);
        T GenerateTrampoline<T>() where T : Delegate;
    }

    public interface ISortableDetour : IDetour {
        uint GlobalIndex { get; }
        int Priority { get; set; }
        string ID { get; set; }
        IEnumerable<string> Before { get; set; }
        IEnumerable<string> After { get; set; }
    }
}
