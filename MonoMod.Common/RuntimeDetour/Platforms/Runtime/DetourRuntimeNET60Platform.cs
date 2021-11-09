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
    // This is based on the Core 3.0 implementation because they are nearly identical, save for how to get the GUID
#if !MONOMOD_INTERNAL
    public
#endif
    class DetourRuntimeNET60Platform : DetourRuntimeNETCore30Platform {
        // As of .NET 6, this GUID is found at src/coreclr/inc/jiteeversionguid.h as JITEEVersionIdentifier
        public static new readonly Guid JitVersionGuid = new Guid("5ed35c58-857b-48dd-a818-7c0136dc9f73");
    }
}
