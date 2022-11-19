

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