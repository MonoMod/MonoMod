# `MonoMod.Backports`

`MonoMod.Backports` is a collection of backports of new library features from new versions of the .NET BCL to all of
the versions which MonoMod targets. This includes several extra types which expose functionality which was added to
existing types. As long as `MonoMod.Backports` targets the most recent framework version, all code which uses
it will use the library features actually provided by the runtime.

## Notable APIs

- `MonoMod.Backports.MethodImplOptionsEx` - This can be used everywhere in a project with

  ```cs
  global using MethodImplOptions = MonoMod.Backports.MethodImplOptionsEx;
  ```
