:; nasm -f bin -Ox stubs_x86.asm -o stubs_x86.bin

BITS 32

[map sections stubs_x86.map]
[map symbols]

SECTION .win align=256

win_this_vtbl_proxy_stub:
  .start:
    ; replace the this pointer with the real pointer
    mov ecx, [ecx + 4]
    ; load the real vtable pointer
    mov eax, [ecx]
    ; jump to the actual method
    ; the offset field will be overwritten with the index (times 4)
    jmp [eax + 0x55555555] ; using 0x55555555 forces NASM to use a dword offset
  .after_index:
ALIGN 4, int3
  .end:

WinThis_VtblProxyStubSize equ .end - .start
WinThis_VtblProxyStubIndex equ .after_index - .start - 4

SECTION .gcc align=256

gcc_this_vtbl_proxy_stub:
  .start:
    ; load the this pointer
    mov eax, [esp + 4]
    ; load the real this pointer
    mov eax, [eax + 4]
    ; save the real this pointer back onto the stack
    mov [esp + 4], eax
    ; load the real vtable pointer
    mov eax, [eax]
    ; jump to the actual method
    ; the offset field will be overwritten with the index (times 4)
    jmp [eax + 0x55555555] ; using 0x55555555 forces NASM to use a dword offset
  .after_index:
ALIGN 4, int3
  .end:

GccThis_VtblProxyStubSize equ .end - .start
GccThis_VtblProxyStubIndex equ .after_index - .start - 4
