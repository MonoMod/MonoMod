### **THIS IS PROBABLY NOT THE LIBRARY YOU WANT TO BE USING.** You probably want `RuntimeDetour` instead.

---

## Notable APIs

- `MonoMod.Core.DetourFactory.Current`
- `MonoMod.Core.ICoreDetour`

## Usage

Use `DetourFactory.Current.CreateDetour` to create a single detour from one method to another. The detour will be
automatically undone when the returnedc object is disposed or garbage collected. Only one such detour may be made per
method. If multiple are made, they will not be cleaned up properly. `MonoMod.Core` does not track which methods have
already been detoured, and will not throw.

It is therefore **highly recommended** to use a higher-level detouring API (like that provided by
`MonoMod.RuntimeDetour` or `Harmony`) to perform detours. Those higher level APIs also provide solutions to many of
the limitations to `Core`'s detour abstraction, such as the ability to call the original, unmodified method, or
modify the IL of the method. (See their documentation for how this is actually done.)

Additionally, interfaces in this package may have members added across minor version updates. Other version-based
compatability guarantees are retained.

## Other potentially useful APIs

The default `IDetourFactory` utilizes `MonoMod.Core.Platforms.PlatformTriple.Current` to implement detours. Notably,
that factory handles re-creating the underlying `ISimpleNativeDetour` objects when methods are recompiled by the
runtime.

`PlatformTriple` does provide a few other utilities which may be useful for a higher-level library, however:

- `PlatformTriple.Prepare` JIT compiles a method, sully supporting generic instantiations
- `PlatformTriple.GetIdentifiable` translates the provided `MethodBase` into an instance which can be used as, for
  instance, the key to a `ConditionalWeakTable`

The other methods of `PlatformTriple` are means of implementing the detour factory, and likely should not be touched.