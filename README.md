# MonoMod
<!-- #links -->
<a href="https://discord.gg/jm7GCZB"><img align="right" alt="MonoMod Discord" src="https://discordapp.com/api/guilds/295566538981769216/embed.png?style=banner2" /></a>
General purpose .NET assembly modding "basework", powered by [cecil](https://github.com/jbevain/cecil/).  
*<sup>MIT-licensed.</sup>*
<!-- #links -->

[![Build status](https://img.shields.io/azure-devops/build/MonoMod/MonoMod/1.svg?style=flat-square)](https://dev.azure.com/MonoMod/MonoMod/_build/latest?definitionId=1) ![Deployment status](https://img.shields.io/azure-devops/release/MonoMod/572c97eb-dbaa-4a55-90e5-1d05431535bd/1/1.svg?style=flat-square)

| GitHub: All | NuGet: Patcher | NuGet: Utils | NuGet: RuntimeDetour | NuGet: HookGen |
|--|--|--|--|--|
| [![GitHub releases](https://img.shields.io/github/downloads/MonoMod/MonoMod/total.svg?style=flat-square)](https://github.com/MonoMod/MonoMod/releases) | [![Core](https://img.shields.io/nuget/dt/MonoMod.svg?style=flat-square)](https://www.nuget.org/packages/MonoMod/) | [![Utils](https://img.shields.io/nuget/dt/MonoMod.Utils.svg?style=flat-square)](https://www.nuget.org/packages/MonoMod.Utils/) | [![RuntimeDetour](https://img.shields.io/nuget/dt/MonoMod.RuntimeDetour.svg?style=flat-square)](https://www.nuget.org/packages/MonoMod.RuntimeDetour/) | [![HookGen](https://img.shields.io/nuget/dt/MonoMod.RuntimeDetour.HookGen.svg?style=flat-square)](https://www.nuget.org/packages/MonoMod.RuntimeDetour.HookGen/) |
| [![Version](https://img.shields.io/github/release/MonoMod/MonoMod.svg?style=flat-square)](https://github.com/MonoMod/MonoMod/releases) | [![Version](https://img.shields.io/nuget/v/MonoMod.svg?style=flat-square)](https://www.nuget.org/packages/MonoMod/) | [![Version](https://img.shields.io/nuget/v/MonoMod.Utils.svg?style=flat-square)](https://www.nuget.org/packages/MonoMod.Utils/) | [![Version](https://img.shields.io/nuget/v/MonoMod.RuntimeDetour.svg?style=flat-square)](https://www.nuget.org/packages/MonoMod.RuntimeDetour/) | [![Version](https://img.shields.io/nuget/v/MonoMod.RuntimeDetour.HookGen.svg?style=flat-square)](https://www.nuget.org/packages/MonoMod.RuntimeDetour.HookGen/) |

<sup>[... or download fresh build artifacts for the last commit.](https://dev.azure.com/MonoMod/MonoMod/_build/latest?definitionId=1)</sup>

## Sections
- [Introduction](#introduction)
- [Using MonoMod](#using-monomod)
- [Using ModInterop (ext)](/README-ModInterop.md)
- [Using RuntimeDetour & HookGen (ext)](/README-RuntimeDetour.md)
- [FAQ](#faq)

### Special thanks to my [patrons on Patreon](https://www.patreon.com/0x0ade):
- [Chad Yates](https://twitter.com/ChadCYates)
- [Sc2ad](https://github.com/sc2ad)
- Raegous
- Chaser6
- [Harrison Clarke](https://twitter.com/hay_guise)
- [KyleTheScientist](https://www.twitch.tv/kylethescientist)
- [Renaud Bédard](https://twitter.com/renaudbedard)
- [leo60228](https://leo60228.space)
- [Rubydragon](https://www.twitch.tv/rubydrag0n)
- Holly Magala
- [Jimmy Londo (iamdadbod)](https://www.youtube.com/iamdadbod)
- [Artus Elias Meyer-Toms](https://twitter.com/artuselias)

----

## Introduction
MonoMod is a modding "basework" (base tools + framework).  
Mods / mod loaders for the following games are already using it in one way or another:
- Terraria: [tModLoader](https://github.com/blushiemagic/tModLoader), [TerrariaHooks](https://github.com/0x0ade/TerrariaHooks)
- Hollow Knight: [HollowKnight.Modding](https://github.com/seanpr96/HollowKnight.Modding)
- Celeste: [Everest](https://everestapi.github.io/)
- Risk of Rain 2: [BepInExPack (BepInEx + MonoMod + R2API)](https://thunderstore.io/package/bbepis/BepInExPack/)
- Enter the Gungeon: [Mod the Gungeon](https://modthegungeon.github.io/)
- Rain World: [RainDB via Partiality](http://www.raindb.net/)
- Totally Accurate Battle Simulator: [TABS-Multiplayer](https://github.com/Ceiridge/TABS-Multiplayer)
- Salt and Sanctuary: [Salt.Modding](https://github.com/seanpr96/Salt.Modding)
- Nimbatus: [Nimbatus-Mods via Partiality](https://github.com/OmegaRogue/Nimbatus-Mods)
- Dungeon of the Endless: [DungeonOfTheEndless-Mod via Partiality](https://github.com/sc2ad/DungeonOfTheEndless-Mod)
- FEZ: [FEZMod (defunct)](https://github.com/0x0ade/FEZMod-Legacy)
- And many more! *Ping me on Discord if your mod uses MonoMod!*

It consists of the following **modular components**:
- [**MonoMod.Patcher**](docs/README.Patcher.md): The ahead-of-time MonoMod patcher and relinker.
- [**MonoMod.Utils**](docs/README.Utils.md): Utilities and helpers that not only benefit MonoMod, but also mods in general.
  It contains classes such as `PlatformDetection`, `FastReflectionHelper`, `DynamicMethodHelper`, `DynamicMethodDefinition`, `DynDll` and the `ModInterop` namespace.
- **MonoMod.DebugIL**: Enable IL-level debugging of third-party assemblies in Visual Studio / MonoDevelop.
- [**MonoMod.Core**](docs/README.Core.md): The core upon which runtime method detouring is built.
- [**MonoMod.RuntimeDetour**](docs/RuntimeDetour/Usage.md): A flexible and easily extensible runtime detouring library, supporting x86/x86_64 on .NET Framework, .NET Core, and Mono.
- [**MonoMod.RuntimeDetour.HookGen**](docs/RuntimeDetour.HookGen/Usage.md): A utility to generate a "hook helper .dll" for any IL assembly. This allows you to hook
  methods in runtime mods as if they were events. Built with MonoMod and RuntimeDetour.
- [**MonoMod.Backports**](docs/README.Backports.md): A collection of BCL backports, enabling the use of many new language and library features, as far back as .NET Framework 3.5.

### Why?
- Cross-version compatibility, even with obfuscated assemblies.
- Cross-platform compatibility, even if the game uses another engine (f.e. Celeste uses XNA on Windows, FNA on macOS and Linux).
- Use language features which otherwise wouldn't be supported (f.e. C# 7 in Unity 4.3).
- Patch on the player's machine with a basic mod installer. No need to pre-patch, no redistribution of game data, no copyright violations.
- With HookGen, runtime hooks are as simple as `On.Namespace.Type.Method += (orig, a, b, c) => { /* ... */ }`
- With HookGen IL, you can manipulate IL at runtime and even inline C# delegate calls between instructions.
- Modularity allows you to mix and match. Use only what you need!

---

### Debugging mods that use MonoMod

See [Debugging](docs/Debugging.md).