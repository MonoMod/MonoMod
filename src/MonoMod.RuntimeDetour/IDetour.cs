using MonoMod.Cil;
using MonoMod.Core;
using System;
using System.Reflection;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// A <see cref="Hook"/>, <see cref="ILHook"/>, or <see cref="NativeHook"/>.
    /// </summary>
    public interface IDetour : IDisposable
    {
        /// <summary>
        /// Gets the <see cref="DetourConfig"/> associated with this <see cref="IDetour"/>, if any.
        /// </summary>
        DetourConfig? Config { get; }

        /// <summary>
        /// Gets whether or not this detour is valid and can be manipulated.
        /// </summary>
        bool IsValid { get; }
        /// <summary>
        /// Gets whether or not this hook is currently applied.
        /// </summary>
        bool IsApplied { get; }

        /// <summary>
        /// Applies this detour if it was not already applied.
        /// </summary>
        void Apply();

        /// <summary>
        /// Undoes this hook if it was applied.
        /// </summary>
        void Undo();
    }

    // note: IDetourBase is perhaps poorly named. Its used as the base for passing information into the detour manager.
    // These interfaces probably don't actually need to exist anymore though.
    internal interface IDetourBase
    {
        IDetourFactory Factory { get; }
        DetourConfig? Config { get; }
    }

    internal interface IDetourTrampoline
    {
        MethodBase TrampolineMethod { get; }
        void StealTrampolineOwnership();
        void ReturnTrampolineOwnership();
    }

    internal interface IHook : IDetourBase
    {
        MethodInfo PublicTarget { get; }
        MethodInfo InvokeTarget { get; }
        IDetourTrampoline NextTrampoline { get; }
    }

    internal interface IILHook : IDetourBase
    {
        ILContext.Manipulator Manip { get; }
    }

    internal interface INativeDetour : IDetourBase
    {
        IntPtr Function { get; }
        Type NativeDelegateType { get; }
        Delegate Invoker { get; }
        bool HasOrigParam { get; }
    }
}
