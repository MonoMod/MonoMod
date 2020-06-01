using MonoMod.RuntimeDetour;
using MonoMod.RuntimeDetour.Platforms;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MonoMod.Common.RuntimeDetour.Platforms.Runtime {
    // This is based on the Core 3.1 implementation for now
#if !MONOMOD_INTERNAL
    public
#endif
    class DetourRuntimeNET50p4Platform : DetourRuntimeNETCore31Platform {

        // TODO: Override the implementations to make it work on NET5
    }
}
