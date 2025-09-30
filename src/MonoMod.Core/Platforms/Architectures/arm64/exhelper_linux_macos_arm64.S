// Linux: clang exhelper_linux_macos_arm64.S -o exhelper_linux_arm64.so    -shared -Wl,-x,--eh-frame-hdr,-z,now,-z,noexecstack,-z,noseparate-code,-z,norelro,--build-id=none -nodefaultlibs -nostdlib -s -fuse-ld=lld
// MacOS: clang exhelper_linux_macos_arm64.S -o exhelper_macos_arm64.dylib -shared -Wl,-x -mmacosx-version-min=11

#include "asm.i"

//
// EH Support Data - Language Specific Data Area and Personality refs
//

.data
.p2align 3

_LSDA_none:
    .word 0

_LSDA_mton:
    .word LOCAL_LABEL(emtn_landingpad) - _LSDA_mton

#if !defined(__APPLE__)
_ref_personality:
    .quad _personality
#endif


//
// TLS
//

#if defined(__APPLE__)
.tbss _cur_ex_ptr$tlv$init, 4, 2

.section __DATA,__thread_vars,thread_local_variables
.p2align 3
_cur_ex_ptr:
    .quad __tlv_bootstrap
    .quad 0
    .quad _cur_ex_ptr$tlv$init
#else
tlskey:
    .space 8

.section .init_array,"aw"
.dc.a _eh_init_tlskey

.text
_eh_init_tlskey:
    stp x29, x30, [sp, #-16]!
    mov x29, sp
    LOAD_INTERNAL x0, tlskey
    LOAD_EXTERNAL x1, free
    bl EXTERNAL_C_FUNC(pthread_key_create)
    cbnz x0, LOCAL_LABEL(eit_error)
LOCAL_LABEL(eit_ret):
    ldp x29, x30, [sp], #16
    ret
LOCAL_LABEL(eit_error):
    b LOCAL_LABEL(eit_ret)
#endif


//
// Main exports
//
.text
.global C_FUNC(eh_get_exception_ptr), C_FUNC(eh_has_exception), C_FUNC(eh_managed_to_native), C_FUNC(eh_native_to_managed)

C_FUNC(eh_get_exception_ptr):
    .cfi_startproc
    LSDA(_LSDA_none)
    stp x19, x20, [sp, #-96]!
    stp x7, x8, [sp, #16]
    stp x5, x6, [sp, #32]
    stp x3, x4, [sp, #48]
    stp x1, x2, [sp, #64]
    stp x29, x30, [sp, #80]
    .cfi_def_cfa_offset 96
    .cfi_offset x30, -8
    .cfi_offset x29, -16
    .cfi_offset x2, -24
    .cfi_offset x1, -32
    .cfi_offset x4, -40
    .cfi_offset x3, -48
    .cfi_offset x6, -56
    .cfi_offset x5, -64
    .cfi_offset x8, -72
    .cfi_offset x7, -80
    .cfi_offset x20, -88
    .cfi_offset x19, -96
    add x29, sp, #80
#if defined(__APPLE__)
    adrp x0, _cur_ex_ptr@TLVPPAGE
    ldr x0, [x0, _cur_ex_ptr@TLVPPAGEOFF]
    ldr x19, [x0]
    blr x19
#else
    LOAD_INTERNAL x0, tlskey
    ldr x0, [x0]
    mov x20, x0
    bl EXTERNAL_C_FUNC(pthread_getspecific)
    cbz x0, LOCAL_LABEL(egep_init)
LOCAL_LABEL(egep_ret):
#endif
    ldp x29, x30, [sp, #80]
    ldp x1, x2, [sp, #64]
    ldp x3, x4, [sp, #48]
    ldp x5, x6, [sp, #32]
    ldp x7, x8, [sp, #16]
    ldp x19, x20, [sp], #96
#if !defined(__APPLE)
    .cfi_remember_state
#endif
    .cfi_restore x30
    .cfi_restore x29
    .cfi_restore x2
    .cfi_restore x1
    .cfi_restore x4
    .cfi_restore x3
    .cfi_restore x6
    .cfi_restore x5
    .cfi_restore x8
    .cfi_restore x7
    .cfi_restore x20
    .cfi_restore x19
    .cfi_def_cfa_offset 0
    ret
#if !defined(__APPLE__)
LOCAL_LABEL(egep_init):
    .cfi_restore_state
    mov x0, #8
    bl EXTERNAL_C_FUNC(malloc)
    mov x19, x0
    mov x1, x0
    mov x0, x20
    bl EXTERNAL_C_FUNC(pthread_setspecific)
    mov x0, x19
    b LOCAL_LABEL(egep_ret)
#endif
    .cfi_endproc


C_FUNC(eh_has_exception):
    .cfi_startproc
    LSDA(_LSDA_none)
    stp x29, x30, [sp, #-16]!
    .cfi_def_cfa_offset 16
    .cfi_offset x30, -8
    .cfi_offset x29, -16
    mov x29, sp
    bl C_FUNC(eh_get_exception_ptr)
    ldr x0, [x0]
    cmp x0, #0
    cset x0, ne
    ldp x29, x30, [sp], #16
    .cfi_restore x30
    .cfi_restore x29
    .cfi_def_cfa_offset 0
    ret
    .cfi_endproc


C_FUNC(eh_managed_to_native):
    .cfi_startproc
    PERSONALITY(_personality)
    LSDA(_LSDA_mton)
    stp x29, x30, [sp, #-16]!
    .cfi_def_cfa_offset 16
    .cfi_offset x30, -8
    .cfi_offset x29, -16
    mov x29, sp
    blr x9
    ldp x29, x30, [sp], #16
    .cfi_remember_state
    .cfi_restore x30
    .cfi_restore x29
    .cfi_def_cfa_offset 0
    ret
LOCAL_LABEL(emtn_landingpad):
    .cfi_restore_state
    mov x1, x0 
    bl C_FUNC(eh_get_exception_ptr)
    str x1, [x0]
    mov x0, xzr
    ldp x29, x30, [sp], #16
    .cfi_restore x30
    .cfi_restore x29
    .cfi_def_cfa_offset 0
    ret
    .cfi_endproc


C_FUNC(eh_native_to_managed):
    .cfi_startproc
    PERSONALITY(_personality)
    LSDA(_LSDA_none)
    stp x20, x19, [sp, #-32]!
    stp x29, x30, [sp, #16]
    .cfi_def_cfa_offset 32
    .cfi_offset x30, -8
    .cfi_offset x29, -16
    .cfi_offset x19, -24
    .cfi_offset x20, -32
    add x29, sp, #16
    mov x19, x9
    mov x20, x0
    bl C_FUNC(eh_get_exception_ptr)
    str xzr, [x0]
    mov x0, x20
    blr x19
    mov x20, x0
    bl C_FUNC(eh_get_exception_ptr)
    ldr x0, [x0]
    cbnz x0, LOCAL_LABEL(entm_do_rethrow)
    mov x0, x20
    ldp x29, x30, [sp, #16]
    ldp x20, x19, [sp], #32
    .cfi_remember_state
    .cfi_restore x30
    .cfi_restore x29
    .cfi_restore x19
    .cfi_restore x20
    .cfi_def_cfa_offset 0
    ret
LOCAL_LABEL(entm_do_rethrow):
    .cfi_restore_state
    bl EXTERNAL_C_FUNC(_Unwind_RaiseException)
    brk #1
    .cfi_endproc


_personality:
    .cfi_startproc
    LSDA(_LSDA_none)
    stp x22, x21, [sp, #-48]!
    stp x20, x19, [sp, #16]
    stp x29, x30, [sp, #32]
    .cfi_def_cfa_offset 48
    .cfi_offset x30, -8
    .cfi_offset x29, -16
    .cfi_offset x19, -24
    .cfi_offset x20, -32
    .cfi_offset x21, -40
    .cfi_offset x22, -48
    add x29, sp, #32
    mov x19, x1
    mov x20, x2
    mov x21, x3
    mov x22, x4
    tbz x19, _UA_FORCE_UNWIND, LOCAL_LABEL(p_should_process)
    mov x0, _URC_CONTINUE_UNWIND
    b LOCAL_LABEL(p_ret)
LOCAL_LABEL(p_should_process):
    mov x0, x22
    bl EXTERNAL_C_FUNC(_Unwind_GetLanguageSpecificData)
    ldrsw x1, [x0]
    tbz x19, _UA_SEARCH_PHASE, LOCAL_LABEL(p_handler_phase)
    cbz x1, LOCAL_LABEL(p_no_handler)
    mov x0, _URC_HANDLER_FOUND
    b LOCAL_LABEL(p_ret)
LOCAL_LABEL(p_no_handler):
    mov x0, _URC_CONTINUE_UNWIND
    b LOCAL_LABEL(p_ret)
LOCAL_LABEL(p_handler_phase):
    tbz x19, _UA_HANDLER_FRAME, LOCAL_LABEL(p_no_handler)
    add x1, x1, x0
    mov x0, x22
    bl EXTERNAL_C_FUNC(_Unwind_SetIP)
    mov x0, x22
    mov x1, #DW_REG_x0
    mov x2, x21
    bl EXTERNAL_C_FUNC(_Unwind_SetGR)
    mov x0, _URC_INSTALL_CONTEXT
LOCAL_LABEL(p_ret):
    ldp x29, x30, [sp, #32]
    ldp x20, x19, [sp, #16]
    ldp x22, x21, [sp], #48
    .cfi_restore x30
    .cfi_restore x29
    .cfi_restore x19
    .cfi_restore x20
    .cfi_restore x21
    .cfi_restore x22
    .cfi_def_cfa_offset 0
    ret
    .cfi_endproc
