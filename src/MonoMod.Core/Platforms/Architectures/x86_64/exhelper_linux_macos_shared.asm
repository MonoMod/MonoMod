BITS 64
DEFAULT REL

%include "dwarf_eh.inc"
%include "macros.inc"

section SHR_DECL_DATA

LSDA_mton:
    dd eh_managed_to_native.landingpad - $
LSDA_none:
    dd 0

section SHR_DECL_TEXT

CFI_INIT _personality

SHR_EXTRA_TEXT

; defined in specific platform's implementation
SHR_DECLFN(eh_get_exception_ptr)

SHR_DECLFN(eh_managed_to_native)
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
    call eh_get_exception_ptr
    mov [rax], r15
    
    ; clear rax for safety
    xor eax, eax
    ldreg r15
    FUNCTION_EPILOG
    ret
    
    CFI_ENDPROC
%pop
    

SHR_DECLFN(eh_native_to_managed)
eh_native_to_managed:
    CFI_STARTPROC LSDA_none
    DECL_REG_SLOTS 0
    FUNCTION_PROLOG

    mov r11, rax
    call eh_get_exception_ptr
    xor r10, r10
    mov [rax], r10
    mov rax, r11

    ; native->managed calls into managed, then checks if an exception was caught by this helper on the other side, and rethrows if so
    call rax
    ; return value in rax now

    ; load cur_ex_ptr
    mov r11, rax
    call eh_get_exception_ptr
    mov rax, [rax]
    xchg rax, r11
    ; if it's nonzero, rethrow
    test r11, r11
    jnz .do_rethrow

    CFI_push

    ; otherwise, exit normally
    FUNCTION_EPILOG
    ret
    
    CFI_pop

.do_rethrow:
    mov rdi, r11
    call SHR_EXTFN(_Unwind_RaiseException)
    int3 ; deliberately don't handle failures at this point. This will have been a crash anyway.
    CFI_ENDPROC
    
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
    call SHR_EXTFN(_Unwind_GetLanguageSpecificData)
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
    call SHR_EXTFN(_Unwind_SetIP)

    ; set r15 to contain our exception pointer
    ldarg rdi, context
    mov rsi, DW_REG_r15
    ldarg rdx, exceptionObject
    call SHR_EXTFN(_Unwind_SetGR)

    mov rax, _URC_INSTALL_CONTEXT
    ;jmp .ret

.ret:
    ldreg rbx
    FUNCTION_EPILOG
    ret
    CFI_ENDPROC
%pop

CFI_UNINIT
