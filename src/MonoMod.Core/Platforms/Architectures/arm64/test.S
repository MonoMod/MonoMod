// clang++ ../shared/test.cpp test.S exhelper_linux_macos_arm64.S
// exception handling testing support

#include "asm.i"

.text
.extern EXTERNAL_C_FUNC(throwex), EXTERNAL_C_FUNC(caller), EXTERNAL_C_FUNC(eh_managed_to_native), EXTERNAL_C_FUNC(eh_native_to_managed)
.global C_FUNC(px_call_throwex), C_FUNC(px_call_caller)
.p2align 2

C_FUNC(px_call_throwex):
    LOAD_EXTERNAL x9, throwex
    b EXTERNAL_C_FUNC(eh_managed_to_native)

C_FUNC(px_call_caller):
    LOAD_EXTERNAL x9, caller
    b EXTERNAL_C_FUNC(eh_native_to_managed)
