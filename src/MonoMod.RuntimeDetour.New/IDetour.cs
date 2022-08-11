using MonoMod.Cil;
using MonoMod.Core;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    internal interface IDetour {

        IDetourFactory Factory { get; }
        DetourConfig? Config { get; }

        MethodInfo InvokeTarget { get; }
        MethodBase NextTrampoline { get; }
    }

    internal interface IILHook {
        IDetourFactory Factory { get; }
        DetourConfig? Config { get; }
        ILContext.Manipulator Manip { get; }
    }
}
