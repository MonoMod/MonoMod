# `MonoMod.ILHelpers`

`MonoMod.ILHelpers` is a collection of helpers manually implemented in IL.

Notably, this contains a backport of `System.Runtime.CompilerServices.Unsafe`, as it exists in .NET 6, to all older
runtimes. This means that any environment which *also* provides that class which is older than .NET 6 will require
an `extern alias` to be able to use properly.

## Notable APIs

- `System.Runtime.CompilerServices.Unsafe`
- `MonoMod.ILHelpers`
- 