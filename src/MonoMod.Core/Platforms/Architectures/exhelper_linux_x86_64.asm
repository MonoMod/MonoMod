;:; nasm -f elf64 -Ox exhelper_linux_x86_64.asm -o exhelper_linux_x86_64.o && ld -shared -fPIC -o exhelper_linux_x86_64.so exhelper_linux_x86_64.o

BITS 64
DEFAULT REL

%macro FRAME_PROLOG 0
    push rbp
    mov rbp, rsp
%endmacro

%macro RESERVE 1
    sub rsp, %1
%endmacro

%macro FRAME_EPILOG 0
    mov rsp, rbp
    pop rbp
%endmacro

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

section .tbss ; TLS section
global cur_ex_ptr
cur_ex_ptr: resq 1

section .text

extern _GLOBAL_OFFSET_TABLE_

GLOBAL eh_has_exception:function
eh_has_exception:
    mov rax, [rel cur_ex_ptr wrt ..gottpoff]
    mov r10, [fs:rax]
    xor eax, eax
    test r10, r10
    setnz al
    ret

GLOBAL eh_managed_to_native:function
eh_managed_to_native:
    FRAME_PROLOG

.begin_eh:
    ; managed->native sets up an exception handler to catch unmanaged exceptions for an arbitrary entrypoint
    ; that entrypoint will be passed in rax, using a dynamically generated stub
    call rax ; we have to call because we have a stack frame for EH
    nop ; nop to give us some buffer
    ; then just clean up our frame and return

.end_eh: nop

    FRAME_EPILOG
    ret

.landingpad:
    ; r10 *should* contain the exception object pointer
    mov rax, [rel cur_ex_ptr wrt ..gottpoff]
    mov [fs:rax], r10
    
    ; clear rax for safety
    xor eax, eax
    FRAME_EPILOG
    ret
    
extern _Unwind_RaiseException

GLOBAL eh_native_to_managed:function
eh_native_to_managed:
    FRAME_PROLOG

    ; native->managed calls into managed, then checks if an exception was caught by this helper on the other side, and rethrows if so
    call rax
    ; return value in rax now

    ; load cur_ex_ptr
    mov r10, [rel cur_ex_ptr wrt ..gottpoff]
    mov r10, [fs:r10]
    ; if it's nonzero, rethrow
    test r10, r10
    jnz .do_rethrow

    ; otherwise, exit normally
    FRAME_EPILOG
    ret

.do_rethrow:
    mov rdi, r10
    call _Unwind_RaiseException wrt ..plt
    int3 ; deliberately don't handle failures at this point. This will have been a crash anyway.

extern _Unwind_GetGR
extern _Unwind_SetGR
extern _Unwind_GetIP ; _Unwind_Context* context
extern _Unwind_SetIP ; _Unwind_Context* context, uint64 new_value

; argument passing:
; rdi, rsi, rdx, rcx, r8, r9
; int version, _Unwind_Actions actions, uint64 exceptionClass, _Unwind_Exception* exceptionObject, _Unwind_Context* context
_personality:
%push
    FRAME_PROLOG
    sub rsp, 6 * 8

    %define version rdi
    %define actions rsi
    %define exceptionClass rdx
    %define exceptionObject rcx
    %define context r8

    %define Sversion [rsp + 5*8]
    %define Sactions [rsp + 4*8]
    %define SexceptionClass [rsp + 3*8]
    %define SexceptionObject [rsp + 2*8]
    %define Scontext [rsp + 1*8]

    mov Sversion, version
    mov Sactions, actions
    mov SexceptionClass, exceptionClass
    mov SexceptionObject, exceptionObject
    mov Scontext, context

    ; rdi = version = 1

    %define _UA_SEARCH_PHASE 1
    %define _UA_CLEANUP_PHASE 2
    %define _UA_HANDLER_FRAME 4
    %define _UA_FORCE_UNWIND 8

    %define _URC_HANDLER_FOUND 6
    %define _URC_INSTALL_CONTEXT 7
    %define _URC_CONTINUE_UNWIND 8

    test actions, _UA_FORCE_UNWIND | _UA_CLEANUP_PHASE
    jz .should_process
    mov rax, _URC_CONTINUE_UNWIND
    jmp .ret

.should_process:
    test actions, _UA_SEARCH_PHASE
    jz .handler_phase
    ; this is the search phase, do we have a handler?
    mov rax, _URC_HANDLER_FOUND ; yes, we have a handler, if our personality is called here, we want to use our handler
    jmp .ret

.handler_phase:
    ; actions contains _UA_HANDLER_FRAME

    ; set our IP
    mov rdi, context
    lea rsi, [eh_managed_to_native.landingpad]
    call _Unwind_SetIP WRT ..plt

    ; set r10 to contain our exception pointer
    mov rdi, Scontext
    mov rsi, DW_REG_r+10
    mov rdx, SexceptionObject
    call _Unwind_SetGR WRT ..plt

    ; TODO: what kinds of register fixups do we need to do to call into the landingpad?

    mov rax, _URC_INSTALL_CONTEXT
    ;jmp .ret

.ret:
    FRAME_EPILOG
    ret
%pop


section .eh_frame progbits alloc noexec nowrite align=8

StartEHFrame:

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

%define DW_CFA_advance_loc (0x1<<6)
%define DW_CFA_offset (0x2<<6)
%define DW_CFA_restore (0x3<<6)
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

ALIGN 8, db 0

; https://refspecs.linuxfoundation.org/LSB_5.0.0/LSB-Core-generic/LSB-Core-generic.html#DWARFEXT
CIE:
.Length: dd .end - .ID

.ID: dd 0
.Version: db 1
.AugString:
    db 'z' ; include AugmentationData
    db 'R' ; include encoding for pointers
    db 'P' ; the above contains a pointer to a personality routine
    db 0
.CodeAlignmentFactor: LEB128 0x01 ; set it to 1 because I'm not sure what its purpose is
.DataAlignmentFactor: db 0x78 ; -8, encoded SLEB128
.ReturnAddressColumn: LEB128 DW_REG_RA
.AugmentationLength: LEB128 2+4 ; MAKE SURE THIS STAYS CORRECT, uleb128
.AugmentationData:
    .PointerEncoding: db DW_EH_PE_pcrel | DW_EH_PE_sdata4
    .PersonalityEncoding: db DW_EH_PE_pcrel | DW_EH_PE_sdata4
    .PersonalityRoutine: dd _personality - $
.AugEnd:
.InitialInstructions:
    ; a sequence of Call Frame Instructions (6.4.2 of the DWARF spec)
    ; holy shit FUCK DWARF

    ; define the CFA to be at rsp+8
    ; the CFA points at the high end of the return addr
    db DW_CFA_def_cfa
        LEB128 DW_REG_rsp, 8
    ; set the return addr to be cfa-8 (we encode 1 because the data alignment factor is -8)
    db DW_CFA_offset | DW_REG_RA
        LEB128 1

.end:

ALIGN 8, db 0

; TODO: extend range of this to cover entire EH function
; TODO: create macros to help generate this

FDE0:
.Length: dd .end - .pCIE
.pCIE: dd $ - CIE
.PCBegin: dd eh_managed_to_native.begin_eh - $
.PCRange: dd eh_managed_to_native.end_eh - eh_managed_to_native.begin_eh
.AugmentationLength: LEB128 0 ; MAKE SURE THIS STAYS CORRECT, uleb128
.AugmentationData:

.AugEnd:
.CallFrameInstructions:
    ; this frame info refers exclusively to the region within the prolog/epilog
    ; as such we don't need to do anything particularly fancy other than set the CFA info correctly

    ; CFA is defined as above, but it's relative to the base pointer, which currently points 16 below
    db DW_CFA_def_cfa
        LEB128 DW_REG_rbp, 16
    ; we also need to say that the previous value of rbp is stored atr cfa-16
    db DW_CFA_offset | DW_REG_rbp
        LEB128 2 ; data alignment is -8, so -16 is encoded as 2

    ; it will for the entirety of the EH region, so that's all we need
.end:

ALIGN 8, db 0

; null terminator
dd 0

section .eh_frame_hdr progbits alloc noexec nowrite

EhFrameHdr:
.Version: db 1
.EhFramePtrEnc: db DW_EH_PE_pcrel | DW_EH_PE_sdata4
.FdeCountEnc: db DW_EH_PE_omit
.TableEnc: db DW_EH_PE_omit
.EhFramePtr: dd StartEHFrame - $