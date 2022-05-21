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

        object? ManagerData { get; set; }
    }
}
