using MonoMod.Core;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    internal interface IDetour {

        IDetourFactory Factory { get; }
        DetourConfig? Config { get; }

        MethodInfo InvokeTarget { get; }
        MethodBase NextTrampoline { get; }

        object? ManagerData { get; set; }
    }
}
