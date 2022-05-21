using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.RuntimeDetour {
    internal interface IDetour {

        DetourConfig? Config { get; }

        MethodInfo InvokeTarget { get; }
        MethodBase NextTrampoline { get; }

        // TODO: add a way to store detour parent nodes to make removing detours far easier

    }
}
