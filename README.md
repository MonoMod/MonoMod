# MonoMod
A general purpose .NET assembly modding "basework", powered by [cecil](https://github.com/jbevain/cecil/).  
*MIT-licensed.*

## Sections
- [Introduction](#introduction)
- [Using MonoMod](#using-monomod)
- [Using ModInterop (ext)](/README-ModInterop.md)
- [Using HookGen (ext)](/README-RuntimeDetour.md)
- [FAQ](#using-monomod)

[![MonoMod Discord](https://discordapp.com/api/guilds/295566538981769216/embed.png?style=banner2)](https://discord.gg/jm7GCZB)

### Special thanks to my [patrons on Patreon](https://www.patreon.com/0x0ade):
- [Chad Yates](https://twitter.com/ChadCYates)
- [Renaud Bédard](https://twitter.com/renaudbedard)
- [Artus Elias Meyer-Toms](https://twitter.com/artuselias)

----

## Introduction
MonoMod is a modding "basework" (base tools and framework) consisting of the following parts:
- **MonoMod:** The core MonoMod IL patcher and relinker. Used to patch [Gungeon](https://modthegungeon.github.io/), [Hollow Knight](https://github.com/seanpr96/HollowKnight.Modding), [Celeste](https://everestapi.github.io/), [Rain World (via Partiality wrapper)](http://www.raindb.net/) and [FEZ (abandoned)](https://github.com/0x0ade/FEZMod-Legacy), among other games. *Ping me if your mod uses MonoMod!*
- **MonoMod.Utils:** Utilities and helpers that not only benefit MonoMod, but also mods in general. It contains classes such as `FastReflectionHelper`, `LimitedStream`, `DynamicMethodHelper` and the `ModInterop` namespace.
- **MonoMod.DebugIL:** Enable IL-level debugging of third-party assemblies in Visual Studio.
- **MonoMod.BaseLoader:** A base on which a C# mod loader can be built upon, including a basic engine-agnostic mod content manager and mod relinker.
- **MonoMod.RuntimeDetour:** A flexible and easily extensible runtime detouring library, which doesn't require cecil.
- **MonoMod.RuntimeDetour.HookGen:** Shortened "HookGen", it's an utiltiy generating a "hook helper .dll" for any IL assembly. This allows you to hook methods in runtime mods as if they were events.

### Why?
- Cross-version compatibility, even with obfuscated assemblies.
- Cross-platform compatibility, even if the game uses another engine (f.e. XNA vs FNA in Celeste).
- Using language features which otherwise wouldn't be supported (f.e. C# 7 in Unity 4.3).
- Patching being done on the player's machine with a mod installer - no need to pre-patch and redistribute a dozen patched assemblies.
- With HookGen, runtime hooks are basically `On.Namespace.Type.Method += (orig, self, a, b, c) => { /* ... */ }` - no reflection black magic.
- [You are a civilized person accepting alternatives to "decompile-patch-recompile"-modding and redistributing the patched game.
](https://cdn.discordapp.com/attachments/234007828728119299/441937768898363394/unknown.png)

----

## Using MonoMod
Drop `MonoMod.exe`, all dependencies (Utils, cecil) and your patches into the game directory. Then, in your favorite shell (cmd, bash):

    MonoMod.exe Assembly.exe

MonoMod scans the directory for files named `[Assembly].*.mm.dll` and generates `MONOMODDED_[Assembly].exe`, which is the patched version of the assembly.

### Example

You've got `Celeste.exe` and want to patch the method `public override void Celeste.Player.Added(Scene scene)`.

If you haven't created a mod project yet, create a shared C# library project called `Celeste.ModNameHere.mm`, targeting the same framework as `Celeste.exe`.  
Add `Celeste.exe`, `MonoMod.exe`, all dependencies (.Utils, cecil) and (optionally) `MonoMod.RuntimeDetour.dll` as assembly references.  
*Note:* Make sure to set "Copy Local" to `False` on the game's assemblies. Otherwise your patch will ship with a copy of the game!
 
```cs
#pragma warning disable CS0626 // orig_ method is marked external and has no attributes on it.
namespace Celeste {
    // The patch_ class is in the same namespace as the original class.
    // This can be bypassed by placing it anywhere else and using [MonoModPatch("global::Celeste.Player")]

    // Visibility defaults to "internal", which hides your patch from runtime mods.
    // If you want to "expose" new members to runtime mods, create extension methods in a public static class PlayerExt
    static class patch_Player {
        // MonoMod creates a copy of the original method, called orig_Added.
        public extern void orig_Added(Scene scene);
        public override void Added(Scene scene) {
            // Do anything before.

            // Feel free to modify the parameters.
            // You can even replace the method's code entirely by ignoring the orig_ method.
            orig_Added(scene);
            
            // Do anything afterwards.
        }
    }
}
```

Build `Celeste.ModNameHere.mm.dll`, copy it into the game's directory and run `MonoMod.exe Celeste.exe`, which generates `MONOMODDED_Celeste.exe`.  
*Note:* This can be automated by a post-build step in your IDE and integrated in an installer, f.e. [Everest.Installer (GUI)](https://github.com/EverestAPI/Everest.Installer), [MiniInstaller (CLI)](https://github.com/EverestAPI/Everest/blob/master/MiniInstaller/Program.cs) or [PartialityLauncher (GUI)](https://github.com/PartialityModding/PartialityLauncher).

To make patching easy, yet flexible, the MonoMod patcher offers a few extra features:

- `MonoMod.MonoModRules` will be executed at patch time. Your rules can define relink maps (relinking methods, fields or complete assemblies), change the patch behavior per platform or [define custom modifiers](MonoMod/Modifiers/MonoModCustomAttribute.cs) to f.e. [modify a method on IL-level using cecil.](https://github.com/0x0ade/MonoMod/issues/15#issuecomment-344570625)
- For types and methods, to be ignored by MonoMod (because of a superclass that is defined in another assembly and thus shouldn't be patched), use the `MonoModIgnore` attribute.
- [Check the full list of standard modifiers with their descriptions](MonoMod/Modifiers), including "patch-time hooks", proxies via `[MonoModLinkTo]`, conditinal patching via `[MonoModIfFlag]` + MonoModRules, and a few more. 

----

## FAQ

### How does the patcher work?
- MonoMod first checks for a `MonoMod.MonoModRules` type in your patch assembly, isolates it and executes the code.
- It then copies any new types, including nested types, except for patch types and ignored types.
- Afterwards, it copies each type member and patches the methods. Make sure to use `[MonoModIgnore]` on anything you don't want to change.
- Finally, all relink mappings get applied and all references get fixed (method calls, field get / set operations, ...).


### How can I check my assembly has been modded?
MonoMod creates a type called "WasHere" in the namespace "MonoMod" in your assembly:

```cs
if (Assembly.GetExecutingAssembly().GetType("MonoMod.WasHere") != null) {
    Console.WriteLine("MonoMod was here!");
} else {
    Console.WriteLine("Everything fine, move on.");
}
```

*Note:* This can be easily bypassed by modifying MonoMod and doesn't detect changes outside of MonoMod.  
Please don't fight against the modding community. If you're worried about cheating, we modders are willing to help. And sadly, you can't prevent the game from being modified by other means.


### Am I allowed to redistribute the patched assembly?
This depends on the licensing situation of the input assemblies. If you're not sure, ask the authors of the patch and of the game / program / library.


### Is it possible to use multiple patches?
Yes, as long as the patches don't affect the same regions of code.

While technically possible, its behaviour is not strictly defined and depends on the patching order.  
Instead, please use runtime detours / hooks instead, as those were built with "multiple mod support" in mind.
