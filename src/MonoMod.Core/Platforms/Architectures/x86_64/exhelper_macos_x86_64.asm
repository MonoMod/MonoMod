;:; nasm -f macho64 -O0 exhelper_macos_x86_64.asm -o exhelper_macos_x86_64.o && ld64.lld -dylib -arch x86_64 -undefined dynamic_lookup -platform_version macos 10.6 10.6 -x -L . -lSystem -o exhelper_macos_x86_64.dylib exhelper_macos_x86_64.o
;:; needs a patched nasm + LLVM (for now) ._.

%pragma macho lprefix _
%pragma macho gprefix _

%define DWARF_EH_SECTION_NAME __TEXT,__eh_frame
%define DWARF_EH_SECTION_DECL __TEXT,__eh_frame align=DWARF_WORDSIZE no_dead_strip

%define SHR_DECL_DATA .data ; alias for __DATA,__data data
%define SHR_DECL_TEXT .text ; alias for __TEXT,__text text

%define DWARF_DATA_SECTION_NAME .data
%assign DWARF_EH_PERS_INDIR 1

%define SHR_DECLFN(name) GLOBAL name
%define SHR_IMPFN(name) EXTERN name
%define SHR_EXTFN(name) [name wrt ..gotpcrel]

%define SAVE_XMM_REGS 0

%macro SHR_EXTRA_TEXT 0

; eh_get_exception pointer is expected to not clobber anything except rax (because we can expect that on Linux)
; as a result, we're using a kind-of custom calling convention that has argument registers be callee-saved, and so
; this function must save and restore argument registers. I don't think we need to care about vector regs though,
; because 1. this isn't used anywhere that uses them, and 2. I don't think tlv_get_addr uses them.
eh_get_exception_ptr:
    CFI_STARTPROC LSDA_none
%xdefine regSlots 8
%if SAVE_XMM_REGS
    %assign regSlots regSlots + 8*2
%endif
    DECL_REG_SLOTS regSlots
    FUNCTION_PROLOG
    svreg rcx, rdx, rsi, rdi, r8, r9, r10, r11

%if SAVE_XMM_REGS
%xdefine i 0
%rep 8
    movdqu [rbp - 8*8 - 16*(i+1)], xmm%+i
    %assign i i+1
%endrep
%endif

    mov rdi, [rel cur_ex_ptr wrt ..tlvp]
    call [rdi]
    
%if SAVE_XMM_REGS
%xdefine i 0
%rep 8
    movdqu xmm%+i, [rbp - 8*8 - 16*(i+1)]
    %assign i i+1
%endrep
%endif

    ldreg rcx, rdx, rsi, rdi, r8, r9, r10, r11
    FUNCTION_EPILOG
    ret
    CFI_ENDPROC

%endmacro

%include "exhelper_linux_macos_shared.asm"

SHR_IMPFN(_tlv_bootstrap)

section __DATA,__thread_bss thread_bss align=8
thread_bss_start:
    cur_ex_ptr$tlv$init: resq 1

; this is a sequence of TLVDescriptors
;    struct TLVDescriptor
;    {
;        void*			(*thunk)(struct TLVDescriptor*);
;        unsigned long	key;
;        unsigned long	offset;
;    };
section __DATA,__thread_vars align=8
    cur_ex_ptr: dq _tlv_bootstrap
    dq 0
    dq cur_ex_ptr$tlv$init - thread_bss_start
