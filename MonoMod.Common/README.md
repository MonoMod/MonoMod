
# MonoMod.Common

**[Please read the README in the main repository first.](https://github.com/MonoMod/MonoMod/#readme)**

The code in **this repository is not a MonoMod "component"** in of itself, but instead helps with **sharing any commonly used functionality** between MonoMod and other projects (f.e. [Harmony](https://github.com/pardeike/Harmony/)).

The goal of this repository is to provide a common ground to **share functionality, fixes and general findings between .NET modding libraries** such as differences between .NET runtimes (Mono, NET Framework, .NET Core) and platforms (x86, ARM).

**If you're a mod developer:** This repo is *not* meant to be used by mods as is, unless you *really* want to build your mod very close to metal and possibly *break compatibility with other mods* (no chained detours, no default relinker, no mod interop utilities).

**If you're a developer of a .NET modding library:** Feel free to add this repo as a submodule to your project. If done right, the new `.csproj` format supported by newer versions of `msbuild` and Visual Studio will automatically include all `.cs` files in this repository by default.

If you want to include MonoMod.Common as a separate library, don't - at least for now. The `.csproj` found in this repository is meant to only be used by MonoMod itself right now, but this will change in the near future.

If you want to only make use of individual source files, add matching `<Compile Include="..." Exclude="..." />` tags to your own project's `.csproj` file.
 
The current folder dependency tree should be:

| Folder        | Dependencies |
|---------------|--------------|
| Utils         | *None*       |
| RuntimeDetour | Utils        |

Please open an issue if you've got any questions or problems when trying to include parts of MonoMod.Common in your library.
