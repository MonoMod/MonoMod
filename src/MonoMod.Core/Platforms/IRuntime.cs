using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.Core.Platforms {
    public interface IRuntime {
        Runtime Target { get; }

        RuntimeFeature Features { get; }

        MethodBase GetIdentifiable(MethodBase method);
        RuntimeMethodHandle GetMethodHandle(MethodBase method);

        IDisposable? PinMethodIfNeeded(MethodBase method);
    }
}
