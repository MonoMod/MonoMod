using MonoMod.RuntimeDetour;
using MonoMod.RuntimeDetour.Platforms;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MonoMod.RuntimeDetour.Platforms {
    // This is based on the Core 3.1 implementation for now
#if !MONOMOD_INTERNAL
    public
#endif
    class DetourRuntimeNET50p7Platform : DetourRuntimeNETCore30Platform {
        // As of .NET 5 preview 7, this GUID is found at src/coreclr/src/inc/corinfo.h as JITEEVersionIdentifier
        public static new readonly Guid JitVersionGuid = new Guid("2ca8d539-5db9-4831-8f1b-ade425f036bd");

        // TODO: Override the implementations to make it work on NET5
    }
}
