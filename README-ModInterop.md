# Using ModInterop
The `MonoMod.ModInterop` namespace in `MonoMod.Utils.dll` allows mods to exchange methods with each other, no matter in which order they get loaded, while avoiding hard dependencies / assembly references.

```cs
// When your mod gets loaded, register ModExports.
typeof(ModExports).ModInterop();

[ModExportName("FancyMod")] // Defaults to the mod assembly name.
public static class ModExports {
    // Methods are exported.
    public static int CalculateSomething(int a, int b) => a + b;
    public static void Give(int player, int item) => PlayerManager.Get(player).Give(item);
}

// When another mod gets loaded, they register their imports from FancyMod like so:
typeof(FancyModImports).ModInterop();

// ModImportName is optional. Leaving it out fills fields with the first matching method signature, ignoring mod names.
[ModImportName("FancyMod")] // This can also be applied to fields, using the Mod.Method syntax.
public static class FancyModImports {
    // Fields are imported.
    public static Func<int, int, int> CalculateSomething;
    public static Action<int, int> Give;
}
```
