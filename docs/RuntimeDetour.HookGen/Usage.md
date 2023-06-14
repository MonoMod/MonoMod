<!-- #hookgen -->
# Using HookGen

> Note: HookGen struggles to generate assemblies for some usages. If it fails, try using RuntimeDetour directly.
> 
> HookGen uses RuntimeDetour internally for hooking anyway, but may make it difficult to manage the lifetimes
> of those hooks. Prefer using RuntimeDetour directly for any but the simplest of usecases.

HookGen makes runtime detouring easy to use and flexible.

 1. Run `MonoMod.RuntimeDetour.HookGen.exe Celeste.exe`, which generates `MMHOOK_Celeste.dll`. Either automate this
    step in your "mod installer", ship the .dll with your modding API, or merge your API .dll with this .dll using
    [il-repack](https://github.com/gluck/il-repack). *Make sure to ship MonoMod.RuntimeDetour.dll and MonoMod.Utils.dll!*
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

**HookGen doesn't modify the original assembly. The generated MMHOOK .dll doesn't contain the original code -
it only contains events using RuntimeDetour behind behind the scene.**

For every non-generic method in the input assembly, HookGen generates an event and two delegate types with the "On." namespace prefix.  
The first delegate type, orig_MethodName, matches the original method's signature, adding a "self" parameter for instance methods.  
The second type, hook_MethodName, is the event type. The only difference between orig_ and hook_ is that latter takes an orig_ delegate as the first parameter.

It also generates an event with the "IL." namespace prefix to manipulate the IL at runtime. This feature is still WIP, but it's proven to work already.

MonoMod.RuntimeDetour.HookGen and MonoMod.RuntimeDetour handle all the dirty detouring work transparently. You can
even remove your hooks the same way you'd remove event handlers:

```cs
// Add a hook.
On.Celeste.PlayerHair.GetHairColor += OnGetHairColor;
// Remove a hook.
On.Celeste.PlayerHair.GetHairColor -= OnGetHairColor;
```
<!-- #hookgen -->
