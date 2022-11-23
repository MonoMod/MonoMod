# Discussion about the CoreCLR JIT hook design

CoreCLR loads the JIT by loading the `clrjit` separate shared library (though it can also load a so-called `altjit`,
MonoMod does not currently handle this case), which exports a `getJit` function, which itself takes no arguments and
returns a pointer to the JIT interface, `ICorJitCompiler`. The exact layout of `ICorJitCompiler` varies based on the
runtime version, but notably, it always contains a `compileMethod` method which performs the actual method
compilation, and it always contains a `getVersionGuid(out GUID)` method. Since both of their vtable indicies vary,
`Core21Runtime` exposes virtual properties containing their indicies. It also exposes a virtual `GUID` property
`ExpectedJitVersion` which is checked against the `GUID` returned by `getVersionGuid` to ensure that the runtime
implementation being used is using the same JIT interface version as the JIT itself. This is the same check that the
runtime does.

Similarly, because the calling convention (and potentially even signature, though that has not changed yet) of the
`compileMethod` method can change, it exposes `InvokeCompileMethodPtr`, which returns a callable function pointer
which invokes the `compileMethod` method with the correct calling convention, and `CastCompileHookToRealType`, which
casts a delegate pointing to our `compileMethod` hook to a delegate type with the correct calling convention
attributes. This allows the setup code to then call `Marshal.GetFunctionPointerForDelegate` to easily get a pointer
that we can patch in to the JIT interface's vtable.

The `compileMethod` hook itself requires a handful of helpers to allow us to get a `RuntimeMethodHandle` from the
`MethodDesc` pointers given to `compileMethod`, which is then passed down to `FxCoreBaseRuntime.OnMethodCompiledCore`
which contains the core logic for converting `RuntimeMethodHandle`s to `MethodBase`s and invoking the event, as
necessary. (This is placed in `FxCoreBaseRuntime` because .NET Framework uses a nearly identical architecture with
respect to its JIT, which means that implementing the hook for .NET Framework should be incredibly simple, even
though it has not been done yet.) Importantly, the `compileMethod` hook has 2 guards. The first (and by far more
important) is the `hookEntrancy` guard. While in the hook, we may call methods which have not yet been compiled (or
otherwise trigger a method compilation in this thread) and must not recurse too deeply. If this happens, we simply
call the normal `compileMethod` function and exit early, without doing any extra work. The second guard is the last P
Invoke error guard. Because our JIT code may P/Invoke into OS functions (and in fact, often does), our hook may
clobber the last P/Invoke error that the runtime stores. This is especially troublesome when some code invokes the
JIT immediately after a P/Invoke, and that JITted method will check the result. This happens in the .NET 6 new
`FileStream` implementation (See [#93](https://github.com/MonoMod/MonoMod/issues/93)).

Another major aspect which must be handled is that of native exceptions. On all non-Windows platforms that CoreCLR
supports, exceptions are not allowed to propagate across P/Invoke boundaries, due to the underlying exception handling
framework on those platforms. As it happens, however, the JIT (and EE that the JIT calls back into) can throw
exceptions in certain circumstances (see [dotnet/runtime#78271](https://github.com/dotnet/runtime/issues/78271)).
To prevent `abort()`s in this case, we need to call from the JIT hook into the JIT via a native method with exception
handling information present, which will ultimately catch any exceptions that reach that point and manually propagate
them back through managed code, before rethrowing at the native-to-managed boundary. This is done via the
`INativeExceptionHelper` interface, optionally provided by `ISystem`. It provides `CreateNativeToManagedHelper`,
`CreateManagedToNativeHelper`, and a property for getting the current saved native exception. The two
`Create...Helper` methods create stubs which call a particular function through the OS's exception helper. The
native-to-managed helper clears the current native exception, calls the target, then checks for a native exception,
rethrowing if present. The managed-to-native helper simply catches exceptions that throw into it, saving it into
the current exception slot. When setting up the JIT hook, we pass the original JIT function pointer through
`CreateManagedToNativeHelper` to catch exceptions thrown by the JIT, and we pass the generated function pointer
for our hook through `CreateNativeToManangedHelper` to allow it to rethrow when necessary. Take a look at
[NativeExceptionHelper](./NativeExceptionHandler.md) for more details.

As it happens, the JIT interface is quite stable over time (at least, with respect to the parts we care about). The
JIT Interface Version GUID does change quite regularly, however, meaning that our checks do not pass when running on
preview builds of any given major version.

---

.NET 7 introduced some troubles with the JIT hook. After the main JIT returns, the part of memory that its `out`
parameter points to as the start of code is filled with zeroes. This is a result of the runtime's improved support
for W^X. The JIT writes the code it outputs to an RW-only block of memory that the EE gives it via the `allocMem`
method on `ICorJitInfo`, though it only returns the executable pointer that the EE gives it. After calling the JIT,
the EE then copies the code from the RW memory to the actual target, and adjusts its protections as needed.

In order to support this, MonoMod has 2 options:

 1. It can rely on the layout of the actual object that the `ICorJitInfo` pointer the JIT gets points to, and read
    the RW memory block out of there. This requires doing some pointer arithmetic to the read value, and this layout
    is quite likely to be incredibly unstable over time. As such, this is not the approach that MonoMod takes.
 2. MonoMod can pass a wrapper `ICorJitInfo` object into the real JIT method, which replaces the `allocMem`
    implementation with our own. This allows us to access the allocation pointers as they are being returned from the
    EE to the JIT. This approach does require us to generate a full proxy vtable though.

The current implementation takes option 2, and uses the `IArchitecture.CreateNativeVtableProxyStubs` method (which
was created for this) to generate a full proxy vtable for `ICorJitInfo`, then replace the entry for `allocMem`. The
JIT hook then gets or creates a memory allocation used by the current thread to hold the wrapper object, sets its
vtable and wrapped object field, and passes that into the JIT.

The wrapper object, per `CreateNativeVtableProxyStubs`'s contract, must be shaped like this:

```c
struct WrapperObject {
  void** __vtable;
  void* WrappedObject; // this is expected to have offset 0 contain a vtable pointer, as in all major ABIs
  // any extra data
};
```

This layout makes the assembly thunks used incredibly simple, for all architectures.

The size of the vtable that must be generated, as well as the offset of `allocMem`, and methods to create and cast
the delegate are made virtual on `Core70Runtime`, just like they are for the `compileMethod` hook. `Core70Runtime`
also provides a virtual `PatchWrapperVtable` which can be overridden to make it easy to hook more `ICorJitInfo`
methods.
