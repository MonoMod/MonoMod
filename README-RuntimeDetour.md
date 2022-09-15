# Using RuntimeDetour

Creating a new detour looks like this most of the time:

<!-- #usage -->
```cs
// Create a Hook.
using (var d = new Hook(methodInfoFrom, methodInfoTo))
{
    // When the detour goes out-of-scope (and thus has Dispose() called), the detour is undone.
    // If the object is collected by the garbage collector, the detour is also undone.
}
```
<!-- #usage -->

<!-- #types -->
## Detour Types

There are 2 managed detour types that are available:

 1. `Hook` - This is effectively `Detour` but better. It sits as part of the same detour chain as `Detour`
    objects. The target of the hook may be a delegate, and so may be an instance method associated with some
    object. Hook targets may also take as their first parameter a delegate with a signature matching the detour
    source. This delegate, when called, will invoke the next detour in the chain, or the original if this is the last detour in the chain. **Note: this delegate should usually only be called while the hook method
    is on the stack. See [The Detour Chain](#the-detour-chain) for more information.**
 2. `ILHook` - This is a different kind of detour. If you're familiar with Harmony, this is effectively a
    transpiler. When you construct an ILHook, you provide a delegate which gets provided an `ILContext` which
    can be used to modify the IL of the method. If multiple ILHooks are present for the same method, the order
    the manipulators are invoked is the same as the order detours in a detour chain would be.

Each detour (`Hook` or `ILHook`) may have an associated `DetourConfig`. Each detour config must have
an ID--this will typically be the name of the mod which applies the hook. They also have a list of IDs which
detours associated with this config will run before, and a similar list that they will run after. If some config `A` wants to run before `B`, and `B` wants to run before `A`, the resulting order is unspecified. The
MonoMod debug log will make a note of this.

Detour configs may also have a priority. Detours with *any* priority will execute before any without, unless
one of the before or after fields caused it to be placed differently. The before and after fields take precedence over the priority field.

Any detours with no `DetourConfig` get run in an arbitrary order after all those with a `DetourConfig`.

<!-- #types -->
<!-- #chain -->
## The Detour Chain

All detours whose source method is the same are part of one *detour chain*. When the source method is called,
the first detour in the chain gets called. That detour then has complete control over how that function behaves.
It may, at any point, invoke the delegate (gotten from the delegate
parameter to a hook method) to invoke the next detour in the chain. It may pass any parameters, and do anything
with the return value. 

While within the original method invocation (i.e. in the detour chain, *with every prior detour on the
stack*), invoking the continuation delegate is safe, and will always invoke the next detour in the chain.
Modifying the detour chain for a method is thread safe, and modifications will wait until all existing 
invocations of the detour chain exit, and all new invocations of the chain will wait until the chain 
modification is complete before execution. Notably, though, **this means that invoking continuation delegates
while the detour chain is not on the stack is not thread-safe, nor is it guaranteed to actually invoke the
delegate chain as it exists at the moment of invocation.** Always invoke the original method, and never delay
invocation of the original delegate.

<!-- #chain -->

## Detour Order Calculation details

We define the calculation of detour order by defining how we insert a new detour into the chain.

There are two chains that we compute independently: the *config chain* and the *no-config chain*.

The *config chain* is computed as follows:

 1. Each detour with a `DetourConfig` is assigned a node. This node holds a list of detour nodes which must
    execute before this detour. These nodes form a graph.
 2. When inserting a node, we add this node to the before list of every existing node with an ID in this node
    config's before list.
     1. We also add every node with an ID in this node's after list to this node's before list
     2. The same is done in reverse as well.
     3. When inserting into any of the lists, nodes are inserted according to their priority value.
        Higher priority values get placed earlier in the list, and lower priorities get placed later in the
        list. Nodes which do not have a priority get placed at the end of the list, after all nodes with 
        priority.
 3. The new node is then added to the list of all nodes, in the same manner as above.
 4. After inserting the node into the graph, it is flattened by iterating through the list of all nodes,
    recursively adding each node in the current node's before list to the output order. After this step,
    all nodes are in the correct relative positions, using the priority field to further refine ordering.

The *no-config chain* is simply appended to whenever a new detour with no `DetourConfig` is applied.

The final detour chain is then just the *config chain* followed by the *no-config* chain.

<!-- #hookgen -->
# Using HookGen

**NOTE: HookGen is not currently updated to use the new RuntimeDetour!**

HookGen makes runtime detouring easy to use and flexible.

 1. Run `MonoMod.RuntimeDetour.HookGen.exe Celeste.exe`, which generates `MMHOOK_Celeste.dll`. Either automate this step in your "mod installer", ship the .dll with your modding API, or merge your API .dll with this .dll using [il-repack](https://github.com/gluck/il-repack). *Make sure to ship MonoMod.RuntimeDetour.dll and MonoMod.Utils.dll!*
 2. Add `MMHOOK_Celeste.dll` to your assembly references in your mod project.
 3. ```cs
    // Let's hook Celeste.Player.GetTrailColor
    // Note that we can also -= this (and the IL. manipulation) by using separate methods.
    On.Celeste.Player.GetTrailColor += (orig, player, wasDashB) => {
        Console.WriteLine("1 - Hello, World!");

        // Get the "original" color and manipulate it.
        // This step is optional - we can return anything we want.
        // We can also pass anything to the orig method.
        Color color = orig(player, wasDashB);

        // If the player is facing left, display a modified color.
        if (player.Facing == Facings.Left)
            return new Color(0xFF, color.G, color.B, color.A);

        return color;
    };

    // Or...

    IL.Celeste.Player.GetTrailColor += (il) => {
        ILCursor c = new ILCursor(il);
        // The new cursor starts out at the beginning of the method.
        // You can either set .Index directly or perform basic pattern matching using .Goto*

        // Insert Console.WriteLine(...)
        c.Emit(OpCodes.Ldstr, "2 - Hello, IL manipulation!");
        c.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", new Type[] { typeof(string) }));

        // After that, emit an inline delegate call.
        c.EmitDelegate<Action>(() => {
            Console.WriteLine("3 - Hello, C# code in IL!");
        });
        
        // There are also many other helpers to f.e. quickly advance to a region or
        // push + invoke any arbitrary delegate accepting any arguments, returning anything.
        // Take a look at the il. and c. autocomplete recommendations.

        // Leave the rest of the method unmodified.
    };

    ```

**HookGen doesn't modify the original assembly. The generated MMHOOK .dll doesn't contain the original code - it only contains events using RuntimeDetour behind behind the scene.**

For every non-generic method in the input assembly, HookGen generates an event and two delegate types with the "On." namespace prefix.  
The first delegate type, orig_MethodName, matches the original method's signature, adding a "self" parameter for instance methods.  
The second type, hook_MethodName, is the event type. The only difference between orig_ and hook_ is that latter takes an orig_ delegate as the first parameter.

It also generates an event with the "IL." namespace prefix to manipulate the IL at runtime. This feature is still WIP, but it's proven to work already.

MonoMod.RuntimeDetour.HookGen and MonoMod.RuntimeDetour handle all the dirty detouring work transparently. You can even remove your hooks the same way you'd remove event handlers:

```cs
// Add a hook.
On.Celeste.PlayerHair.GetHairColor += OnGetHairColor;
// Remove a hook.
On.Celeste.PlayerHair.GetHairColor -= OnGetHairColor;
```
<!-- #hookgen -->

# **NOTE: Everything below this point is outdated!

# Technical details - RuntimeDetour is an onion
The RuntimeDetour namespace is split up into the following "layers", bottom to top:

## IDetour*Platforms:

The "platform layer" is an abstraction layer that makes porting RuntimeDetour to new native platforms (ARM) and new runtime platforms (.NET Core) much easier. The performance penalty of using an abstraction layer only applies during the detour creation / application process.

- **IDetourNativePlatform** is responsible for freeing, copying and allocating memory, setting page flags (read-write-execute) and applying the actual native detour, using the struct NativeDetourData.  
As it's operating completely on the native level, it isn't restricted to .NET runtime methods and can also detour native functions.  
This allows maintaining the x86 / x86-64 native code separately from the ARM native code and any other platform-specific fixes.  
- **IDetourRuntimePlatform** is responsible for pinning methods, creating IL-copies at runtime and getting the starting address of the JIT's resulting native code.  
The Mono and .NET Framework detour platforms inherit from the shared DetourRuntimeILPlatform, with the only main differences being how the RuntimeMethodHandle is obtained and how DynamicMethods are JITed.

## IDetour classes:

The following classes implement this interface and allow you to create detours by just instantiating them. The instances are also the way how you generate trampolines and how you undo detours.

- **NativeDetour** is the lowest-level "managed detour." Even though you can directly use the native and runtime platforms and the NativeDetourData struct, one should use this class instead.  
**No multi-detour management happens on this level.** A "from" pointer can be NativeDetoured only once at a time if you need deterministic results.  
If you apply a NativeDetour on a "managed function" (System.Reflection.MethodBase), it creates an IL-copy of the method and uses it as the trampoline. Otherwise, the trampoline temporarily undoes the detour and calls the original method. This means that **NativeDetour can be used on large enough native functions.**
- **Detour** is the first fully-managed detour level. It thus doesn't allow you to pass a pointer as a method to detour "from", but it manages a **dynamic detour chain**. Calling the **trampoline calls the previous detour**, if it exist, or the original method, if none exists. It holds an internal NativeDetour for the top of the detour chain.
- **Hook** takes detours to the next level and are the driving force behind HookGen. It allows you to detour from a method to any arbitrary **delegate with a matching signature**, allowing you to receive the **trampoline as the first parameter**.
