using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Core.Interop
{
    internal static partial class CoreCLR
    {
        // The preprocessor symbols that the native CoreCLR build was compiled with, as far as they can be determined for the
        // current process. [CppIf] conditions on [CppStruct] mirrors bind to these by name, and ported code tests them where
        // the C++ uses #if. A version class (V110, ...) can shadow one whose definition changes in that version.
        //
        // Paths are relative to src/coreclr/ in dotnet/runtime.

        // The target architecture of the runtime is that of the process.
        public static readonly bool TARGET_64BIT = IntPtr.Size == 8;
        public static readonly bool HOST_64BIT = TARGET_64BIT;
        public static readonly bool TARGET_X86 = PlatformDetection.Architecture == ArchitectureKind.x86;
        public static readonly bool TARGET_AMD64 = PlatformDetection.Architecture == ArchitectureKind.x86_64;
        public static readonly bool TARGET_ARM = PlatformDetection.Architecture == ArchitectureKind.Arm;
        public static readonly bool TARGET_ARM64 = PlatformDetection.Architecture == ArchitectureKind.Arm64;
        public static readonly bool TARGET_WINDOWS = PlatformDetection.OS.Is(OSKind.Windows);
        public static readonly bool TARGET_UNIX = !TARGET_WINDOWS;

        // eng/native/configurecompiler.cmake: defined for the Debug and Checked configurations. This cannot be detected,
        // so it follows the DebugClr switch.
        public static readonly bool _DEBUG = Switches.TryGetSwitchEnabled(Switches.DebugClr, out var isEnabled) && isEnabled;

        // clrdefinitions.cmake: if(CLR_CMAKE_TARGET_WIN32) add_definitions(-DFEATURE_COMINTEROP)
        public static readonly bool FEATURE_COMINTEROP = TARGET_WINDOWS;

        // clrdefinitions.cmake: if (NOT ((CLR_CMAKE_TARGET_ARCH_I386 AND CLR_CMAKE_TARGET_UNIX) OR CLR_CMAKE_TARGET_ARCH_WASM))
        public static readonly bool FEATURE_METADATA_UPDATER = !(TARGET_X86 && TARGET_UNIX);

        // clrfeatures.cmake and clr.featuredefines.props: enabled for the Debug and Checked configurations on AMD64, ARM64,
        // ARM, RISCV64 and LOONGARCH64 (except Android), and always on iOS, tvOS, Mac Catalyst and WASM. PlatformDetection
        // does not tell Mac Catalyst apart from macOS.
        public static readonly bool FEATURE_INTERPRETER =
            (_DEBUG && (TARGET_AMD64 || TARGET_ARM64 || TARGET_ARM) && !PlatformDetection.OS.Is(OSKind.Android))
            || PlatformDetection.OS.Is(OSKind.IOS);

        // clrfeatures.cmake: only for WASM, which MonoMod does not run on. Not const, so code under it stays reachable.
        [SuppressMessage("Performance", "CA1802:Use literals where appropriate", Justification = "See above.")]
        [SuppressMessage("Performance", "CA1805:Do not initialize unnecessarily", Justification = "Stated for clarity.")]
        public static readonly bool FEATURE_PORTABLE_ENTRYPOINTS = false;

        // vm/<arch>/cgencpu.h: defined for every architecture except WASM.
        public static readonly bool HAS_PINVOKE_IMPORT_PRECODE = !FEATURE_PORTABLE_ENTRYPOINTS;
    }
}
