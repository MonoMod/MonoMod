%ifndef _MACROS
%define _MACROS

%macro FRAME_PROLOG 0
    push rbp
    %ifdef __CFI_InProc
        CFI_def_cfa_offset 16
        CFI_offset DW_REG_rbp, -16
    %endif
    mov rbp, rsp
    %ifdef __CFI_InProc
        CFI_def_cfa_reg DW_REG_rbp
    %endif
%endmacro

%macro RESERVE 1
    sub rsp, %1
%endmacro

%macro FRAME_EPILOG 0
    mov rsp, rbp
    %ifdef __CFI_InProc
        CFI_def_cfa_reg DW_REG_rsp
    %endif
    pop rbp
    %ifdef __CFI_InProc
        CFI_def_cfa_offset 8
    %endif
%endmacro

%endif