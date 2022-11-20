;:; nasm -f elf64 -Ox exhelper_linux_x86_64.asm -o exhelper_linux_x86_64.o && ld -shared --eh-frame-hdr -z now -x -o exhelper_linux_x86_64.so exhelper_linux_x86_64.o

BITS 64
DEFAULT REL

%include "dwarf_eh.inc"
%include "macros.inc"

section .tbss ; TLS section
global cur_ex_ptr
cur_ex_ptr: resq 1

section .data

LSDA_mton:
    dd eh_managed_to_native.landingpad - $
LSDA_none:
    dd 0

section .text

CFI_INIT _personality

GLOBAL eh_has_exception:function
eh_has_exception:
    CFI_STARTPROC LSDA_none
    mov rax, [rel cur_ex_ptr wrt ..gottpoff]
    mov r10, [fs:rax]
    xor eax, eax
    test r10, r10
    setnz al
    ret
    CFI_ENDPROC

GLOBAL eh_managed_to_native:function
eh_managed_to_native:
%push
    CFI_STARTPROC LSDA_mton
    DECL_REG_SLOTS 2
    FUNCTION_PROLOG

    svreg rax, r15

    ; managed->native sets up an exception handler to catch unmanaged exceptions for an arbitrary entrypoint
    ; that entrypoint will be passed in rax, using a dynamically generated stub
    call rax ; we have to call because we have a stack frame for EH
    ; then just clean up our frame and return

    CFI_push

    ldreg r15
    FUNCTION_EPILOG
    ret

    CFI_pop

.landingpad:
    mov rax, [rel cur_ex_ptr wrt ..gottpoff]
    mov [fs:rax], r15
    
    ; clear rax for safety
    xor eax, eax
    ldreg r15
    FRAME_EPILOG
    ret
    
    CFI_ENDPROC
%pop
    

GLOBAL eh_native_to_managed:function
eh_native_to_managed:
    CFI_STARTPROC LSDA_none
    FRAME_PROLOG

    ; zero cur_ex_ptr
    mov r11, 0
    mov r10, [rel cur_ex_ptr wrt ..gottpoff]
    mov [fs:r10], r11

    ; native->managed calls into managed, then checks if an exception was caught by this helper on the other side, and rethrows if so
    call rax
    ; return value in rax now

    ; load cur_ex_ptr
    mov r10, [rel cur_ex_ptr wrt ..gottpoff]
    mov r10, [fs:r10]
    ; if it's nonzero, rethrow
    test r10, r10
    jnz .do_rethrow

    CFI_push

    ; otherwise, exit normally
    FRAME_EPILOG
    ret
    
    CFI_pop

.do_rethrow:
    mov rdi, r10
    call _Unwind_RaiseException wrt ..plt
    int3 ; deliberately don't handle failures at this point. This will have been a crash anyway.
    CFI_ENDPROC
    
; TODO: for some reason, when our personality is called in phase 2, it doesn't point to the same exception object it does in phase 1
; the pointer seems to be outright invalid in phase 2, while correct in phase 1

; argument passing:
; rdi, rsi, rdx, rcx, r8, r9
; int version, _Unwind_Actions actions, uint64 exceptionClass, _Unwind_Exception* exceptionObject, _Unwind_Context* context
_personality:
%push
    CFI_STARTPROC LSDA_none
    DECL_REG_SLOTS 1
    FUNCTION_PROLOG version, actions, exceptionClass, exceptionObject, context

    svreg rbx

    ; rdi = version = 1

    test reg(actions), _UA_FORCE_UNWIND
    jz .should_process
    mov rax, _URC_CONTINUE_UNWIND
    jmp .ret

.should_process:
    svarg context, actions, exceptionObject

    ; load the LSDA value into rbx, because we'll always need it after this point
    mov rdi, reg(context)
    call _Unwind_GetLanguageSpecificData wrt ..plt
    movsxd rbx, dword [rax]

    ldarg actions
    test reg(actions), _UA_SEARCH_PHASE
    jz .handler_phase
    ; this is the search phase, do we have a handler?

    ; we want to check that the LSDA's pointer is non-null
    test ebx, ebx
    jz .no_handler

    mov rax, _URC_HANDLER_FOUND ; yes, we have a handler
    jmp .ret
.no_handler:
    mov rax, _URC_CONTINUE_UNWIND ; no, we don't have a handler
    jmp .ret

.handler_phase:
    ; check that Sactions contains _UA_HANDLER_FRAME
    test reg(actions), _UA_HANDLER_FRAME
    jz .no_handler

    ; rax contains pLSDA, and rbx contains LSDA
    ; their sum is the landingpad
    add rax, rbx

    ; set our IP
    ldarg rdi, context
    mov rsi, rax
    call _Unwind_SetIP WRT ..plt

    ; set r15 to contain our exception pointer
    ldarg rdi, context
    mov rsi, DW_REG_r15
    ldarg rdx, exceptionObject
    call _Unwind_SetGR WRT ..plt

    mov rax, _URC_INSTALL_CONTEXT
    ;jmp .ret

.ret:
    ldreg rbx
    FUNCTION_EPILOG
    ret
    CFI_ENDPROC
%pop

CFI_UNINIT
