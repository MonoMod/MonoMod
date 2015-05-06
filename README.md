# MonoMod
Random C# / Mono / .NET assembly patcher using Mono.Cecil.



## What does it do?
It patches any C# assembly (PE32 executable (console) Intel 80386 Mono/.Net assembly) with any given assembly patches.

The patches are in form of C# assemblies, making it easy to use for quick and (as long as the input assembly isn't obfuscated) version-independent patching / modding / prototyping.

Finally, MonoMod patches itself into the input assembly entry point. In case the user runs the patched assembly in a shell, the used MonoMod version gets printed.



## How to use it?
You drop the MonoMod.exe, Mono.Cecil.dll and your patches into a folder. Then, in your favorite shell (cmd, bash):

    MonoMod.exe Assembly.exe

MonoMod then scans the directory the file is in for files named Assembly.*.dll (in case of FEZ.exe: FEZ.Mod.dll).
Finally, it produces an Assembly.mm.exe, which is the patched version of the assembly.



## How does it exactly work?
Black magic, Mono.Cecil and a lot of luck. Oh, and many, **many** hacks and workarounds.



## How to create a patch assembly?
You decompile the input assembly to get a tree of all types and methods. Use that tree to create another assembly which contains pieces of that tree with custom code.

To further simplify patching, MonoMod offers following features:

* For types and methods, to be ignored by MonoMod (because of a superclass that is defined in another assembly and thus shouldn't be patched), use the MonoModIgnore attribute.
* For types that must have another name than the original type but should still get patched into the input assembly by MonoMod, prefix the type with "patch_".
* To call the original method, create a stub method of the same signature with the prefix "orig_".



## Are there limits?
Yes.

* Any patch that doesn't compile due to any reasons (feel free to add your workaround here).
* Any patch that crashes MonoMod (feel free to add your fix here).
* Any patch that causes issues at IL level (feel free to add your black magic here).



## Am I allowed to redistibute the patched assembly?
Ask the creator of the input assembly, the patches and your lawyer (actually not your lawyer).



## Am I allowed to redistribute MonoMod?
Yes. Do so freely, as long as you don't charge the user any costs for using / downloading MonoMod.



##Is it possible to patch patches?
Yes, but the orig_ methods will be ignored by MonoMod.


##Is it possible to patch patched patches?
...


##Is it possible to patch MonoMod?
Yes.


##Is it possible to make my PC burn?
Maybe. I've heard AMD CPUs start smoking at 400°C.
