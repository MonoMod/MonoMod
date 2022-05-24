using MonoMod.Cil;
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

    internal interface IILHook {
        IDetourFactory Factory { get; }
        DetourConfig? Config { get; }
        ILContext.Manipulator Manip { get; }

        object? ManagerData { get; set; }
    }
}
