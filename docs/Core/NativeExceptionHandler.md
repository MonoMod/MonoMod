# INativeExceptionHelper

`INativeExcepionHandler` is a helper that may be provided by an `ISystem` implementation to facilitate propagating
(but currently not catching!) native exceptions through managed code. It does this by creating helpers which sit
on the native side of P/Invoke transitions which catch and rethrow native exceptions as needed. The native exception
helper implementation will store the current native exception pointer in a thread-local accessible through
the `NativeException` property, and provide means to create the transition helpers for arbitrary function pointers
(currently as long as they pass all arguments in-register, and none on stack) to allow users to safely deal with
native exceptions.

## Why is this needed?

On Linux, MacOS, and similar OS's, exception handling information is stored in the executable or shared library
ELF/MachO binaries. This information is then read by the platform's `libunwind` (governed by the [Itanium ABI
specification](https://itanium-cxx-abi.github.io/cxx-abi/abi-eh.html#base-abi)) when an exception is thrown. This
information is used to unwind the stack, giving each frame an opportunity to perform cleanup work and decide whether
it is able to catch the exception that is being propagated. The appropriate handler is then decided, and execution
resumes at the 'landingpad' for the exception handler. The specification describes the API which can be used by
both application code and by the so-called 'personality' functions for each stack frame, but not the format which
unwind information is stored in, nor where it should be stored.

In practice, on Linux, this is stored in the ELF files that make up the loaded binaries, and have a format governed
by [this document](https://refspecs.linuxfoundation.org/LSB_5.0.0/LSB-Core-generic/LSB-Core-generic/ehframechpt.html).
The format specified is based on the DWARF debug information format, which is horribly overengineered. Moreover,
because these platforms look for unwind info in a section of the loaded binary, there is no means to dynamically
register such information, as would be required by the .NET runtime. (There are, for some platforms, undocumented
functions which serve this purpose: `__register_frame` for both GNU `libgcc` and NonGNU `libunwind`, though those
libraries actually take different parameters, and a much older `__register_frame_info` which seems to have stuck
around in MacOS's `unwind.h` header, but is documented to not actually work.) As such, for these platforms, we must
dynamically load a shared library containing exception handing information which we must call through in order to
handle exceptions.

## Usage

Generally, whenever a call is made to a native function which may throw a native exception, it should be called
through a generated managed-to-native helper, if the managed code is itself called by native code through a
native-to-managed helper. Immediately after such a call, `NativeException` should be checked, and if it is
non-null, it should be saved, and execution should be made to exit as immediately as possible. During the final
stages of cleanup, right before returning out of managed code, `NativeException` should be set to the exception
being manually propagated. This will cause the native-to-managed helper to rethrow the exception.

It is important to note that exceptions caught this way **must** be manually propagated, and not caught and
swallowed in managed code. This is because on Linux, such handling of an exception must call
`_Unwind_DeleteException`, which is not exposed via the native exception helper.

## Implementation

### Linux

The shared library for Linux x86-64 is `exhelper_linux_x86_64.so`, and is implemented in NASM assembly in
[`src/MonoMod.Core/Platforms/Architectures/x86_64/exhelper_linux_x86_64.asm`](../../src/MonoMod.Core/Platforms/Architectures/x86_64/exhelper_linux_x86_64.asm).
Like the rest of the assembly files in its sibling folders, the first line contains a command line which compiles
it.

Broadly speaking, the exception info is automatically generated via a mess of macros, mostly defined in `asminc/dwarf_eh.inc`. These macros are largely designed to resemble the CFI directives exposed by the GNU assembler for this
same purpose. Further macros exist in `x86_64/macros.inc` and `x86_64/dwarf_eh.inc`, which further assist creation
of function with exception handling.

#### `eh_get_exception` and `eh_set_exception`

These exports expose the TLS cell which holds the current exception. This cell is automatically set by
`eh_manged_to_native` when necessary, and cleared by `eh_native_to_managed`.

#### `eh_native_to_managed`

This export is the native-to-manged entrypoint. First, it clears the TLS cell. Then, it calls the target, passed in
through the architecture-specific special argument register `rax`. (This register is not used for argument passing
in any calling convention.) After the target returns, it checks the TLS cell for an exception, and if one is present,
calls `_Unwind_RaiseException` with it. Because `_Unwind_RaiseException` must unwind the stack, this entrypoint
erects a stack frame for it to unwind through, and ensures that unwind info is present. Due to this, however, stack
passed arguments are not supported currently. They may be in the future, however.

#### `eh_managed_to_native`

This export is the managed-to-native entrypoint. This is the meat of the behaviour, containing EH unwind info, as well
as a landingpad which recieves the exception in `r15`. When called, it saves `rax` (which contains the target) and `r15` to the stack, to be restored later. The `svreg` macro does this while ensuring that they are present in the unwind info. It then calls the target, with the same limitations as `eh_native_to_managed`. Finally, it returns.
If, however, an exception was caught, it saves the exception to the TLS cell, clears the return value, and returns
normally.

#### `_personality`

This is the personality function. This is called by the unwinder to decied how to handle exceptions. Our personality
function is fairly simple. It uses the language-specific data area pointer to point to a 32-bit relative pointer to
the landingpad for a catch if present, and if not, it is zero. Any given procedure may only have one landingpad, and it is hit for all exceptions.
