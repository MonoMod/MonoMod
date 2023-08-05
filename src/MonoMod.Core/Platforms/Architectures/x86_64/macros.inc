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

%define _REGS_1 rdi
%define _REGS_2 rsi
%define _REGS_3 rdx
%define _REGS_4 rcx
%define _REGS_5 r8
%define _REGS_6 r9

%macro DECL_REG_SLOTS 1 ; 
    %xdefine __SSPACE %1
%endmacro

%macro FUNCTION_PROLOG 0-* ; takes register argument names

    %if %0 > 6
    %error Too many arguments! Not all arguments fit in registers!
    %endif

    FRAME_PROLOG

    %ifndef __SSPACE
        %define __SSPACE 0
    %endif

    ; reserve stack space for all arguments
    %xdefine %%numslots (__SSPACE + %0)
    %xdefine %%hnumslots (%%numslots / 2)
    %if %%hnumslots == 0 && %%numslots != 0
        %define %%hnumslots 1
    %endif

    %xdefine __SSPACE (%%hnumslots * 2 * 8)
    %if __SSPACE > 0
        sub rsp, __SSPACE
    %endif

    %xdefine __NARGS %0
    %define __NREG 1

    ; generate the offsets and register defs
    %assign %%i 1
    %rep %0
        %xdefine %[__AOFF_%+%1] (%%i * -8)
        %xdefine %[__AREG_%+%1] _REGS_%+%%i

        %assign %%i %%i + 1
        %rotate 1
    %endrep

%endmacro

%macro svarg 1-* ; takes the name of the argument
    
    %rep %0
        %xdefine %%offs %[__AOFF_%+%1]
        %xdefine %%reg %[__AREG_%+%1]
        mov [rbp + %%offs], %%reg

        %ifndef %[__ASAVED_%+%1]
            %ifdef __CFI_InProc
                CFI_offset DW_REG_%+%%reg, (%%offs) - 16
            %endif
        %endif

        %define %[__ASAVED_%+%1]
        %rotate 1
    %endrep

%endmacro

%macro svreg 1-* ; takes the name of the registers

    %rep %0
        %ifdef %[__ROFF_%+%1]
            mov [rbp + __ROFF_%+%1], %1
        %else
            %ifndef __NREG
                %define __NREG 1
            %endif

            %xdefine %%offs ((__NARGS + __NREG) * -8)
            %xdefine __NREG __NREG + 1

            %if -%%offs > __SSPACE
                %error Too many registers stored! Add more DECL_REG_SLOTS!
            %endif

            mov [rbp + %%offs], %1

            %ifdef __CFI_InProc
                CFI_offset DW_REG_%+%1, (%%offs) - 16 ; ret -> bp -> offs base
            %endif

            %xdefine %[__ROFF_%+%1] %%offs

        %endif
        %rotate 1
    %endrep

%endmacro

%macro ldarg 1-2 ; either takes arg name and moves it into its reg, or into the (first) target reg

    %rotate 1
    %xdefine %%offs %[__AOFF_%+%1]
    %xdefine %%reg %[__AREG_%+%1]
    %if %0 == 1 ; load into own reg
        mov %%reg, [rbp + %%offs]
    %else ; load into target reg
        mov %2, [rbp + %%offs]
    %endif

%endmacro

%macro ldreg 1-* ; takes the names of registers
    %rep %0
        mov %1, [rbp + %[__ROFF_%+%1]]
        %rotate 1
    %endrep
%endmacro

%define reg(arg) __AREG_%+arg
%define addr(arg) qword [rbp + __AOFF_%+arg]

%macro FUNCTION_EPILOG 0
    
    FRAME_EPILOG

%endmacro

%endif
