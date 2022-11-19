%ifndef _DWARF_EH_64
%define _DWARF_EH_64

%use ifunc
%include "../asminc/dwarf_eh.inc"

%define DW_REG_rax 0
%define DW_REG_rdx 1
%define DW_REG_rcx 2
%define DW_REG_rbx 3
%define DW_REG_rsi 4
%define DW_REG_rdi 5
%define DW_REG_rbp 6
%define DW_REG_rsp 7
%define DW_REG_r 0
%define DW_REG_r8 8
%define DW_REG_r9 9
%define DW_REG_r10 10
%define DW_REG_r11 11
%define DW_REG_r12 12
%define DW_REG_r13 13
%define DW_REG_r14 14
%define DW_REG_r15 15
%define DW_REG_RA 16
%define DW_REG_xmm 17
%define DW_REG_st 33
%define DW_REG_mm 41

%if __?BITS?__ == 64
   %macro __CFA_Initial 0
        ; this is always what the stack looks like immediately after a call

        ; define the CFA to be at rsp+8
        ; the CFA points at the high end of the return addr
        db DW_CFA_def_cfa
            LEB128 DW_REG_rsp, 8
        ; set the return addr to be cfa-8 (we encode 1 because the data alignment factor is -8)
        db DW_CFA_offset | DW_REG_RA
            LEB128 1
   %endmacro
%elif __?BITS?__ == 32
    %error TODO: implement 32-bit CFA initial
%else
%error Unsupported BITS value __?BITS?__
%endif

%endif