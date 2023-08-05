;:; nasm -f elf64 -Ox exhelper_linux_x86_64.asm -o exhelper_linux_x86_64.o && ld -shared --eh-frame-hdr -z now -x -o exhelper_linux_x86_64.so exhelper_linux_x86_64.o

%define DWARF_EH_SECTION_NAME .eh_frame
%define DWARF_EH_SECTION_DECL .eh_frame progbits alloc noexec nowrite align=DWARF_WORDSIZE

%define DWARF_EH_PERS_INDIR 0

%define SHR_DECL_DATA .data
%define SHR_DECL_TEXT .text

%define SHR_DECLFN(name) GLOBAL name:function
%define SHR_IMPFN(name) EXTERN name
%define SHR_EXTFN(name) name wrt ..plt

; Very unfortunately, we can't actually use a TLS section, because the MUSL dynamic linker
; doesn't support loading that dynamically at runtime. Instead, we have to use the pthread APIs directly.

SHR_IMPFN(pthread_key_create)
SHR_IMPFN(pthread_setspecific)
SHR_IMPFN(pthread_getspecific)
SHR_IMPFN(malloc)
SHR_IMPFN(free)

%macro SHR_EXTRA_TEXT 0

; eh_get_exception pointer is expected to not clobber anything except rax (because we can expect that on Linux)
; as a result, we're using a kind-of custom calling convention that has argument registers be callee-saved, and so
; this function must save and restore argument registers. I don't think we need to care about vector regs though,
; because 1. this isn't used anywhere that uses them, and 2. I don't think tlv_get_addr uses them.
eh_get_exception_ptr:
    DECL_REG_SLOTS 8
    FUNCTION_PROLOG
    svreg rcx, rdx, rsi, rdi, r8, r9, r10, r11

    ; first, try to get the value and exit
    mov rdi, [tlskey]
    call SHR_EXTFN(pthread_getspecific)
    test rax, rax
    jz .init
    ; not null, we're safe to return
.ret:
    ldreg rcx, rdx, rsi, rdi, r8, r9, r10, r11
    FUNCTION_EPILOG
    ret

.init:
    ; null, we should set up a frame for safety and malloc() and set it

    mov rdi, 8 ; sizeof(void*)
    call SHR_EXTFN(malloc)
    mov [rbp - 8], rax
    mov rdi, [tlskey]
    mov rsi, rax
    call SHR_EXTFN(pthread_setspecific)
    mov rax, [rbp - 8]
    jmp .ret

_eh_init_tlskey:
    FUNCTION_PROLOG

    ; TODO: how can we report errors?
    lea rdi, [tlskey] ; &tlskey
    lea rsi, [SHR_EXTFN(free)] ; &free (dtor ptr)
    call SHR_EXTFN(pthread_key_create)
    ; success in rax
    test rax, rax
    jnz .error

    ; and we're done here
.ret:
    FUNCTION_EPILOG
    ret

.error:
    ; error is in rax
    jmp .ret

%endmacro

%include "exhelper_linux_macos_shared.asm"

;section .tbss ; TLS section
;global cur_ex_ptr
;cur_ex_ptr: resq 1

section .bss
tlskey: resq 1 ; pthread_key_t is a 32-bit integer, usually, but lets reserve more just in case

section .init_array
dq _eh_init_tlskey
