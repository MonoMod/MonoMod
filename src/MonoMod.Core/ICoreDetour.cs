using System;
using System.Reflection;

namespace MonoMod.Core {
    [CLSCompliant(true)]
    public interface ICoreDetour : ICoreDetourBase {
        MethodBase Source { get; }
        MethodBase Target { get; }
    }
}
