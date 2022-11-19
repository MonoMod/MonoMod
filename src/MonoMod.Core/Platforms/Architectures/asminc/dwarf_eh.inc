%ifndef _DWARF_EH
%define _DWARF_EH

%define DW_EH_PE_absptr 0x00
%define DW_EH_PE_uleb128 0x01
%define DW_EH_PE_udata2 0x02
%define DW_EH_PE_udata4 0x03
%define DW_EH_PE_udata8 0x04
%define DW_EH_PE_sleb128 0x09
%define DW_EH_PE_sdata2 0x0A
%define DW_EH_PE_sdata4 0x0B
%define DW_EH_PE_sdata8 0x0C

%define DW_EH_PE_pcrel 0x10
%define DW_EH_PE_textrel 0x20 ; DON'T USE, NOT SUPPORTED
%define DW_EH_PE_datarel 0x30 ; DON'T USE, NOT SUPPORTED
%define DW_EH_PE_funcrel 0x40 ; DON'T USE, NOT SUPPORTED
%define DW_EH_PE_aligned 0x50
%define DW_EH_PE_indirect 0x80

%define DW_EH_PE_omit 0xff

%define DW_CFA_advance_loc (0x1<<6) ;>
%define DW_CFA_offset (0x2<<6) ;>
%define DW_CFA_restore (0x3<<6) ;>
%define DW_CFA_nop 0x00
%define DW_CFA_set_loc 0x01
%define DW_CFA_advance_loc1 0x02
%define DW_CFA_advance_loc2 0x03
%define DW_CFA_advance_loc4 0x04
%define DW_CFA_offset_extended 0x05
%define DW_CFA_restore_extended 0x06
%define DW_CFA_undefined 0x07
%define DW_CFA_same_value 0x08
%define DW_CFA_register 0x09
%define DW_CFA_remember_state 0x0a
%define DW_CFA_restore_state 0x0b
%define DW_CFA_def_cfa 0x0c
%define DW_CFA_def_cfa_register 0x0d
%define DW_CFA_def_cfa_offset 0x0e
%define DW_CFA_def_cfa_expression 0x0f
%define DW_CFA_expression 0x10
%define DW_CFA_offset_extended_sf 0x11
%define DW_CFA_def_cfa_sf 0x12
%define DW_CFA_def_cfa_offset_sf 0x13
%define DW_CFA_val_offset 0x14
%define DW_CFA_val_offset_sf 0x15
%define DW_CFA_val_expression 0x16

%define _UA_SEARCH_PHASE 1
%define _UA_CLEANUP_PHASE 2
%define _UA_HANDLER_FRAME 4
%define _UA_FORCE_UNWIND 8

%define _URC_HANDLER_FOUND 6
%define _URC_INSTALL_CONTEXT 7
%define _URC_CONTINUE_UNWIND 8

extern _Unwind_RaiseException ; _Unwind_Exception* exception
extern _Unwind_GetLanguageSpecificData ; _Unwind_Context* context
extern _Unwind_GetGR ; _Unwind_Context* context, int reg
extern _Unwind_SetGR ; _Unwind_Context* context, int reg, uint64 new_value
extern _Unwind_GetIP ; _Unwind_Context* context
extern _Unwind_SetIP ; _Unwind_Context* context, uint64 new_value

%macro LEB128 1-*
%push
    ; takes a number, encodes it as LEB128

    %rep %0
        ; first we compute the number of bytes we'll emit (minus 1)
        ; we do this by taking the argument and right-shift-and-masking it, then comparing that with zero
        %assign val %1
        %assign n 0
        %rep 8 ; I sure hope we never need to encode anything that'll be longer than 8 bytes
            ; because we want the number of bytes minus 1, we start with a right shift
            %assign val val>>7
            ; then test against zero
            %if val != 0
                %assign n n+1
            %endif
        %endrep

        ; n now holds the number of continuation bytes we need, we can get to encoding
        %assign val %1
        %rep n
            ; each iter, we want to output a byte with the high bit set, but otherwise the low bits of the input
            db 0x80 | (val & 0x7f)
            %assign val val>>7
        %endrep
        ; then we always want to write val out at the end
        db val

        ; and we're done!
        %rotate 1
    %endrep
%pop
%endmacro

%macro SLEB128 1-*
%push ; TODO: actually support negative inputs
    %rep %0
        ; first we compute the number of bytes we'll emit (minus 1)
        ; we do this by taking the argument and right-shift-and-masking it, then comparing that with zero
        %assign val %1
        %assign n 0
        %rep 8 ; I sure hope we never need to encode anything that'll be longer than 8 bytes
            ; because we want the number of bytes minus 1, we start with a right shift
            ; we use 6, because if the high bit is set, its considered negative
            %assign val val>>6
            ; then test against zero
            %if val != 0
                %assign n n+1
            %endif
            %assign val val>>1 ; correct here
        %endrep

        ; n now holds the number of continuation bytes we need, we can get to encoding
        %assign val %1
        %rep n
            ; each iter, we want to output a byte with the high bit set, but otherwise the low bits of the input
            db 0x80 | (val & 0x7f)
            %assign val val>>7
        %endrep
        ; then we always want to write val out at the end
        db val

        ; and we're done!
        %rotate 1
    %endrep
%pop
%endmacro

%macro LEB128_fixed 2 ; fixed number of bytes, value
%push
    
    ; ensure that the number fits in the number of bits needed
    %assign %$maxbits 7 * %1
    %if (%2 >> %$maxbits) != 0
    %error %2 will not fit in %1-byte LEB128
    %endif
    
    %assign %$val %2
    %rep %1 - 1
        db (%$val & 0x7f) | 0x80
        %assign %$val %$val >> 7
    %endrep
    db %$val

%pop
%endmacro


%assign DWARF_WORDSIZE __?BITS?__ / 8

; define this section ahead of time
section .eh_frame progbits alloc noexec nowrite align=DWARF_WORDSIZE

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
        db 'P' ; AugData contains a pointer to a personality routine
        db 'L' ; include an encoding for LSDA, and LSDA in FDE
        db 0
    %%CIE.CodeAlignmentFactor: LEB128 0x01 ; set it to 1 because I'm not sure what its purpose is
    %%CIE.DataAlignmentFactor: db 0x80 - DWARF_WORDSIZE ; -DWARF_WORDSIZE, encoded SLEB128
    %%CIE.ReturnAddressColumn: LEB128 DW_REG_RA
    %%CIE.AugmentationLength: LEB128 7 ; MAKE SURE THIS STAYS UP-TO-DATE
    %%CIE.AugmentationData:
        %%CIE.PointerEncoding: db DW_EH_PE_pcrel | DW_EH_PE_sdata4
        %%CIE.PersonalityEncoding: db DW_EH_PE_pcrel | DW_EH_PE_sdata4
        %%CIE.PersonalityRoutine: dd %1 - $
        %%CIE.LSDAEncoding: db DW_EH_PE_pcrel | DW_EH_PE_sdata4
    %%CIE.AugEnd:
    %%CIE.InitialInstructions:
        ; a sequence of Call Frame Instructions (6.4.2 of the DWARF spec)
        __CFA_Initial
    ALIGN DWARF_WORDSIZE, db 0

    %%CIE.end:

    __?SECT?__ ; then return back to the original section

%endmacro

%macro CFI_UNINIT 0
    %undef __CFI_LastCIE

    [section .eh_frame]
    dd 0
    ALIGN DWARF_WORDSIZE, db 0
    __?SECT?__
%endmacro

%macro CFI_STARTPROC 1 ; takes LSDA label

    %push cfi_proc

    %%proc_start:
    %define %$__CFI_FDEProcEndTok %%proc_end

    [section .eh_frame] ; put our data in the .eh_frame section

    %%FDE:
    %define %$__CFI_LastFDE %%FDE
    %define %$__CFI_FDE_end %%FDE.end

    %%FDE.Length: dd %%FDE.end - %%FDE.pCIE
    %%FDE.pCIE: dd $ - __CFI_LastCIE
    %%FDE.PCBegin: dd %%proc_start - $
    %%FDE.PCRange: dd %%proc_end - %%proc_start
    %%FDE.AugmentationLength: LEB128_fixed 1, 4
        %%FDE.LSDA: dd %1 - $
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
    
    ; align
    ALIGN DWARF_WORDSIZE, db 0
    ; mark the end of the FDE
    %$__CFI_FDE_end:

    ; return to our orignal section
    CFI_EXIT_DATA

    ; pop the context
    %pop cfi_proc

    ; throw in a bit of alignment
    ALIGN DWARF_WORDSIZE, int3

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
        %if %%delta < (1<<6) ;>
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
    LEB128 %1
    SLEB128 (%2)

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
    SLEB128 %1

    CFI_EXIT_DATA
%endmacro

%macro CFI_offset 2 ; reg, offset
    CFI_ENTER_DATA

    db DW_CFA_offset | %1
    SLEB128 ((%2) // (-DWARF_WORDSIZE))
    
    CFI_EXIT_DATA
%endmacro

%macro CFI_push 0
    CFI_ENTER_DATA

    db DW_CFA_remember_state

    CFI_EXIT_DATA
%endmacro

%macro CFI_pop 0
    CFI_ENTER_DATA

    db DW_CFA_restore_state

    CFI_EXIT_DATA
%endmacro

%endif