using MonoMod.Backports;
using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Core.Platforms {
    public static class Platform {
        public static IRuntime CreateCurrentRuntime()
            => PlatformDetection.Runtime switch {
                RuntimeKind.Framework => Runtimes.FxBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion),
                RuntimeKind.CoreCLR => Runtimes.CoreCLRBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion),
                RuntimeKind.Mono => throw new NotImplementedException(),
                var kind => throw new PlatformNotSupportedException($"Runtime kind {kind} not supported"),
            };

        public static IArchitecture CreateCurrentArchitecture()
            => PlatformDetection.Architecture switch {
                ArchitectureKind.x86 => throw new NotImplementedException(),
                ArchitectureKind.x86_64 => new Architectures.x86_64Arch(),
                ArchitectureKind.Arm => throw new NotImplementedException(),
                ArchitectureKind.Arm64 => throw new NotImplementedException(),
                var kind => throw new PlatformNotSupportedException($"Architecture kind {kind} not supported"),
            };

        public static ISystem CreateCurrentSystem()
            => PlatformDetection.OS switch {
                OSKind.Posix => throw new NotImplementedException(),
                OSKind.Linux => throw new NotImplementedException(),
                OSKind.Android => throw new NotImplementedException(),
                OSKind.OSX => throw new NotImplementedException(),
                OSKind.IOS => throw new NotImplementedException(),
                OSKind.BSD => throw new NotImplementedException(),
                OSKind.Windows or OSKind.Wine => new Systems.WindowsSystem(),
                var kind => throw new PlatformNotSupportedException($"OS kind {kind} not supported"),
            };
    }
}
