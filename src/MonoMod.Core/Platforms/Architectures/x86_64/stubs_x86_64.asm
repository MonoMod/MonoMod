:; nasm -f bin -Ox stubs_x86_64.asm -o stubs_x86_64.bin

BITS 64
DEFAULT REL

[map sections stubs_x86_64.map]
[map symbols]

%macro vtbl_proxy_stub 2
  .start:
    ; replace the this pointer with the real pointer
    mov %2, [%2 + 8]
    ; load the real vtable pointer
    mov rax, [%2]
    ; jump to the actual method
    ; the offset field will be overwritten with the index (times 8)
    jmp [rax + 0x55555555] ; using 0x55555555 forces NASM to use a dword offset
  .after_index:
ALIGN 8, int3
  .end:

%1%+_VtblProxyStubSize equ .end - .start
%1%+_VtblProxyStubIndex equ .after_index - .start - 4

%endmacro

SECTION .win align=256

win_vtbl_proxy_stub:
    vtbl_proxy_stub Win, rcx

SECTION .sysv align=256

sysv_vtbl_proxy_stub:
    vtbl_proxy_stub SysV, rdi

SECTION .shared align=256

rax_jmp_stub:
.start:
    mov rax, strict qword 0
    .after_target:
    mov r10, strict qword 0
    .after_helper:
    jmp r10
.end:

RaxJmpStubSize equ .end - .start
RaxJmpStubTgt equ .after_target - .start - 8
RaxJmpStubHlp equ .after_helper - .start - 8