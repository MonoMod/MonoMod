;:; nasm -f elf64 -Ox exhelper_linux_x86_64.asm -o exhelper_linux_x86_64.o && ld -shared --eh-frame-hdr -z now -x -o exhelper_linux_x86_64.so exhelper_linux_x86_64.o

%define DWARF_EH_SECTION_NAME .eh_frame
%define DWARF_EH_SECTION_DECL .eh_frame progbits alloc noexec nowrite align=DWARF_WORDSIZE

%define DWARF_EH_PERS_INDIR 0

%define SHR_DECL_DATA .data
%define SHR_DECL_TEXT .text

%define SHR_DECLFN(name) GLOBAL name:function
%define SHR_EXTFN(name) name wrt ..plt

%macro SHR_EXTRA_TEXT 0

eh_get_exception_ptr:
    CFI_STARTPROC LSDA_none
    mov rax, [fs:0]
    add rax, [rel cur_ex_ptr wrt ..gottpoff]
    ret
    CFI_ENDPROC

%endmacro

%include "exhelper_linux_macos_shared.asm"

section .tbss ; TLS section
global cur_ex_ptr
cur_ex_ptr: resq 1
