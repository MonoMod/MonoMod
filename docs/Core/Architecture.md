# `MonoMod.Core`'s Architecture

`MonoMod.Core` is divided up into approximately 3 layers of abstractions, each being progressively lower level:

- `IDetourFactory`, implemented by `PlatformTripleDetourFactory`
  
  This level is responsible for maintaining the single method -> method detour. Its core responsibilities are
  managing the underlying `ISimpleNativeDetour`, handed out and managed by `PlatformTriple`, and keeping it up to
  date with any recompilations the method may undergo as a result of the runtime's tiered compilation. The events
  which allow it to respond when the runtime recompiles a method are provided by the `IRuntime` interface.

- `PlatformTriple`
  
  The platform triple is mostly a wrapper around the lowest abstraction level, though it does provide some important
  higher-level operations that require all three (`IArchitecture`, `ISystem`, and `IRuntime`). The most notable of
  these is the creation of `ISimpleNativeDetour` objects, via `CreateSimpleDetour`. This also includes other
  operations, such as the identification of `MethodBase` objects, creating ABI fixup proxy methods, and following
  various runtimes' precode thunks to find a method's real entry point.

- `IArchitecture`, `ISystem`, and `IRuntime`
  
  These are the bread and butter of `MonoMod.Core`'s design. These represent the processor architecture, operating
  system, and .NET runtime, respectively. Each of them provide a collection of APIs which are used by the higher
  level abstractions to have consistent behaviour for any combination of them. While they are largely designed to be
  treated as orthogonal, in practice, they aren't quite, and each of the implementations have a few checks for the
  state of the others to decide how to behave.

  An overview of each:

  - `IArchitecture`
  
    Most notably, this contains the `ComputeDetourInfo`, `GetDetourBytes`, `ComputeRetargetInfo`, and
    `GetRetargetBytes` methods. These are used by `PlatformTriple.CreateSimpleDetour` to determine which detour type
    to use, and how to redirect that detour later, if necessary.

    It also provides the `KnownMethodThunks` property, which returns a `BytePatternCollection` with patterns for all
    of the known precode and thunks that a runtime may use between a method's publicly visible entry point and its
    actual code. The runtime implementation will set the `RuntimeFeature.RequiresBodyThunkWalking` feature flag if
    this needs to be used.

  - `ISystem`

    This interface is primarily used to interact with the current process's memory, via operating system APIs. It
    exposes a means to estimate the size of the readable memory after a specified address, if any, as well as an
    atomic `PatchData` method and an unmanaged memory allocator.

    The `PatchData` is designed to be atomic primarily because of the restrictions imposed by the Apple M1 processor
    line; memory can never be both writable and executable. This API makes it possible to implement the method as a
    purely native method, capable of calling the necessary OS functions to make a memory block writable, copy memory
    around as needed, then undo that, all without interfering with normal execution of managed code.

    The most notable feature of the `IMemoryAllocator` provided by a system implementation is the ability to request
    an allocated block of memory 'near' certain address, for an arbitrary definition of 'near'. This allows the
    implementation of detour types such as `x86_64`'s `Rel32Ind64` detour (which consists of a jump instruction which
    dereferences an indirection cell) to be able to be used more often, by allocating that indirection cell close
    enough to the detoured method body to be accessible.
  
  - `IRuntime`

    This interface represents the currently running .NET runtime, and provides methods which allow poking arbitrarily
    deep into their internals.

    It (may) provide methods to identify a `MethodBase`, reliably get the `RuntimeMethodHandle` for a `MethodBase`,
    disable inlining for a particular method, and 'pin' a method to prevent its garbage collection. It must also
    provide a method to get the entry point of a method. Often, this is simply done through `RuntimeMethodHandle
    GetFunctionPointer()`, but in some circumstances, it may use `GetLdftnPointer()` instead.

    It may also provide an `OnMethodCompiled` event, which is invoked after the JIT compiles a method. Currently,
    only the .NET Core runtime implementations provide this.
