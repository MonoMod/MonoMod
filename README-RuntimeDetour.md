# Using HookGen
HookGen makes runtime detouring easy to use and flexible.

1. Run `MonoMod.RuntimeDetour.HookGen.exe Celeste.exe`, which generates `MMHOOK_Celeste.dll`. Either automate this step in your "mod installer", ship the .dll with your modding API, or merge your API .dll with this .dll using [il-repack](https://github.com/gluck/il-repack). *Make sure to ship MonoMod.RuntimeDetour.dll and MonoMod.Utils.dll!*
2. Add `MMHOOK_Celeste.dll` to your assembly references in your mod project.
3.  
```cs
On.Celeste.PlayerHair.GetHairColor += (orig, self, index) => {
    // Do anything before.

    // Feel free to modify the parameters.
    // You can even replace the method's code entirely by ignoring the orig method.
    var color = orig(self, index);
    
    // Do anything afterwards.
    return color;
};
```

**The generated MMHOOK .dll doesn't contain any hooks in itself - it only enables runtime hooking, using RuntimeDetour behind the scene.**

For every non-generic method in the input assembly, HookGen generates an event and two delegate types with the "On." namespace prefix.  
The first delegate type, orig_MethodName, matches the original method's signature, adding a "self" parameter for instance methods.  
The second type, hook_MethodName, is the event type. The only difference between orig_ and hook_ is that latter takes an orig_ delegate as the first parameter.

MonoMod.RuntimeDetour.HookGen and MonoMod.RuntimeDetour handle all the dirty detouring work transparently. You can even remove your hooks the same way you'd remove event handlers:

```cs
// Add a hook.
On.Celeste.PlayerHair.GetHairColor += OnGetHairColor;
// Remove a hook.
On.Celeste.PlayerHair.GetHairColor -= OnGetHairColor;
```

## Technical details - RuntimeDetour is an onion
The RuntimeDetour namespace is split up into the following "layers", bottom to top:

### IDetour*Platforms:

The "platform layer" is an abstraction layer that makes porting RuntimeDetour to new native platforms (ARM) and new runtime platforms (.NET Core) much easier. The performance penalty of using an abstraction layer only applies during the detour creation / application process.

- **IDetourNativePlatform** is responsible for freeing, copying and allocating memory, setting page flags (read-write-execute) and applying the actual native detour, using the struct NativeDetourData.  
As it's operating completely on the native level, it isn't restricted to .NET runtime methods and can also detour native functions.  
This allows maintaining the x86 / x86-64 native code separately from the ARM native code and any other Windows-specific fixes.  
- **IDetourRuntimePlatform** is responsible for pinning methods, creating IL-copies at runtime and getting the starting address of the JIT's resulting native code.  
The Mono and .NET Framework detour platforms inherit from the shared DetourRuntimeILPlatform, with the only differences being the way how the RuntimeMethodHandle is obtained and how DynamicMethods are JITed.

### IDetour classes:

The following classes implement this interface and allow you to create detours by just instantiating them. The instances are also the way how you generate trampolines and how you undo detours.

- **NativeDetour** is the lowest-level "managed detour." Even though you can directly use the native and runtime platforms and the NativeDetourData struct, one should use this class instead.  
**No multi-detour management happens on this level.** A "from" pointer can be NativeDetoured only once at a time if you need deterministic results.  
If you apply a NativeDetour on a "managed function" (System.Reflection.MethodBase), it creates an IL-copy of the method and uses it as the trampoline. Otherwise, the trampoline temporarily undoes the detour and calls the original method. This means that **NativeDetour can be used on large enough native functions.**
- **Detour** is the first fully-managed detour level. It thus doesn't allow you to pass a pointer as a method to detour "from", but it manages a **dynamic detour chain**. Calling the **trampoline calls the previous detour**, if it exist, or the original method, if none exists. It holds an internal NativeDetour for the top of the detour chain.
- **Hook** takes detours to the next level and are the **driving force behind HookGen**. It allows you to detour from a method to any arbitrary delegate with a matching signature, allowing you to receive the trampoline as the first parameter.
