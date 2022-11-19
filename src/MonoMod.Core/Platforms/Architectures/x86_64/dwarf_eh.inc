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
%define DW_REG_RA 16
%define DW_REG_xmm 17
%define DW_REG_st 33
%define DW_REG_mm 41

; define this section ahead of time
section .eh_frame progbits alloc noexec nowrite align=8

; https://refspecs.linuxfoundation.org/LSB_5.0.0/LSB-Core-generic/LSB-Core-generic.html#DWARFEXT
%macro CFI_INIT 1 ; takes personality routine

    [section .eh_frame] ; put our data in the .eh_frame section
    %%CIE: 
    %define __CFI_LastCIE %%CIE

    %%CIE.Length: dd %%CIE.end - %%CIE.ID

    %%CIE.ID: dd 0
    %%CIE.Version: db 1
    %%CIE.AugString:
        db 'z' ; include AugmentationData
        db 'R' ; include encoding for pointers
        db 'P' ; the above contains a pointer to a personality routine
        db 0
    %%CIE.CodeAlignmentFactor: LEB128 0x01 ; set it to 1 because I'm not sure what its purpose is
    %%CIE.DataAlignmentFactor: db 0x78 ; -8, encoded SLEB128
    %%CIE.ReturnAddressColumn: LEB128 DW_REG_RA
    %%CIE.AugmentationLength: LEB128 6 ; MAKE SURE THIS STAYS UP-TO-DATE
    %%CIE.AugmentationData:
        %%CIE.PointerEncoding: db DW_EH_PE_pcrel | DW_EH_PE_sdata4
        %%CIE.PersonalityEncoding: db DW_EH_PE_pcrel | DW_EH_PE_sdata4
        %%CIE.PersonalityRoutine: dd %1 - $
    %%CIE.AugEnd:
    %%CIE.InitialInstructions:
        ; a sequence of Call Frame Instructions (6.4.2 of the DWARF spec)
        ; this is always what the stack looks like immediately after a call

        ; define the CFA to be at rsp+8
        ; the CFA points at the high end of the return addr
        db DW_CFA_def_cfa
            LEB128 DW_REG_rsp, 8
        ; set the return addr to be cfa-8 (we encode 1 because the data alignment factor is -8)
        db DW_CFA_offset | DW_REG_RA
            LEB128 1

    %%CIE.end:

    ALIGN 4, db 0

    __?SECT?__ ; then return back to the original section

%endmacro

%macro CFI_UNINIT 0
    %undef __CFI_LastCIE

    [section .eh_frame]
    dd 0
    ALIGN 4, db 0
    __?SECT?__
%endmacro

%macro CFI_STARTPROC 0

    %push cfi_proc

    %%proc_start:
    %define %$__CFI_FDEProcEndTok %%proc_end

    [section .eh_frame] ; put our data in the .eh_frame section

    %%FDE:
    %define %$__CFI_LastFDE %%FDE
    %define %$__CFI_FDE_end %%FDE_end

    %%FDE.Length: dd %%FDE_end - %%FDE.pCIE
    %%FDE.pCIE: dd $ - __CFI_LastCIE
    %%FDE.PCBegin: dd %%proc_start - $
    %%FDE.PCRange: dd %%proc_end - %%proc_start
    %%FDE.AugmentationLength: LEB128_fixed 1, 0
    %%FDE.AugmentationData:
    %%FDE.AugEnd:
    %%FDE.CallFrameInstructions:

    %define __CFI_InProc 1

    CFI_EXIT_DATA

%endmacro

%macro CFI_ENDPROC 0

    %undef __CFI_InProc

    ; mark the end of the proc
    %$__CFI_FDEProcEndTok:

    [section .eh_frame] ; put our data in the .eh_frame section

    ; mark the end of the FDE
    %$__CFI_FDE_end:
    ; align
    ALIGN 4, db 0

    ; return to our orignal section
    CFI_EXIT_DATA

    ; pop the context
    %pop cfi_proc

%endmacro

%macro CFI_IN_PROC 0
    %ifndef __CFI_InProc
    %error "Must be in CFI proc"
    %endif
%endmacro

%macro CFI_ENTER_DATA 0
    CFI_IN_PROC
    %%pc EQU $
    [section .eh_frame]
    %%delta EQU %%pc - __CFA_LastPC
    %if %%delta > 0
        %if %%delta < (1<<6);>
            db DW_CFA_advance_loc | %%delta
        %elif %%delta <= 0xff
            db DW_CFA_advance_loc1, %%delta
        %else ; TODO: encore more advances
            db DW_CFA_advance_loc2
            dw %%delta
        %endif
    %endif
%endmacro

%macro CFI_EXIT_DATA 0
    __?SECT?__
    %%lastpc EQU $
    %define __CFA_LastPC %%lastpc
%endmacro

%macro CFI_def_cfa 2 ; reg, offset
    CFI_ENTER_DATA

    db DW_CFA_def_cfa
    LEB128 %1, %2

    CFI_EXIT_DATA
%endmacro

%macro CFI_def_cfa_reg 1 ; reg
    CFI_ENTER_DATA

    db DW_CFA_def_cfa_register
    LEB128 %1

    CFI_EXIT_DATA
%endmacro

%macro CFI_def_cfa_offset 1 ; reg
    CFI_ENTER_DATA

    db DW_CFA_def_cfa_offset
    LEB128 %1

    CFI_EXIT_DATA
%endmacro

%macro CFI_offset 2 ; reg, offset
    CFI_ENTER_DATA

    db DW_CFA_offset | %1
    LEB128 %2 // (-8)
    
    CFI_EXIT_DATA
%endmacro
