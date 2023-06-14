## Notable APIs

- `MonoMod.RuntimeDetour.Hook` - An easy-to-use method hook
- `MonoMod.RuntimeDetour.ILHook` - Modifies the IL of a method
- `MonoMod.RuntiemDetour.DetourContext` - Persistent, shared detour configuration

## Basic Usage

```cs
// Create a Hook.
using (var d = new Hook(methodInfoFrom, methodInfoTo))
{
    // When the detour goes out-of-scope (and thus has Dispose() called), the detour is undone.
    // If the object is collected by the garbage collector, the detour is also undone.
}
```

---

Visit the [GitHub](https://github.com/MonoMod/MonoMod/) and look for RuntimeDetour for more documentation.
