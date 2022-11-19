
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

extern _Unwind_RaiseException
extern _Unwind_GetGR
extern _Unwind_SetGR
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