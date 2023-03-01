;:; nasm -f macho64 -Ox exhelper_macos_x86_64.asm -o exhelper_macos_x86_64.o && ld -arch x86_64 -platform_version macos 10.6 10.6 -x -o exhelper_macos_x86_64.dylib exhelper_macos_x86_64.o

%pragma macho lprefix L_
%pragma macho gprefix _

%define DWARF_EH_SECTION_NAME __TEXT,__eh_frame
%define DWARF_EH_SECTION_DECL __TEXT,__eh_frame align=DWARF_WORDSIZE no_dead_strip

%define SHR_DECL_DATA .data ; alias for __DATA,__data data
%define SHR_DECL_TEXT .text ; alias for __TEXT,__text text

%define DWARF_DATA_SECTION_NAME .data
%assign DWARF_EH_PERS_INDIR 1

%define SHR_DECLFN(name) GLOBAL name
%define SHR_IMPFN(name) EXTERN name
%define SHR_EXTFN(name) name wrt ..gotpcrel

%macro SHR_EXTRA_TEXT 0

eh_get_exception_ptr:
    CFI_STARTPROC LSDA_none
    lea rax, [rel cur_ex_ptr wrt ..tlvp]
    call [rax]
    ret
    CFI_ENDPROC

%endmacro

%include "exhelper_linux_macos_shared.asm"

section __DATA,__thread_bss bss align=8
    cur_ex_ptr$tlv$init: resq 1
section __DATA,__thread_vars data align=8
    cur_ex_ptr: dq 0
