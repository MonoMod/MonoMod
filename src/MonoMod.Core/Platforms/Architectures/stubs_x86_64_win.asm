:; nasm -f bin -Ox stubs_x86_64_win.asm -o nasstubs_x86_64_win.bin

BITS 64
DEFAULT REL

[map sections stubs_x86_64_win.map]
[map symbols]

SECTION .vtbl_proxy_stub align=256

vtbl_proxy_stub:
    ; replace rcx with the real pointer
    mov rcx, [rcx + 8]
    ; load vtable index
    mov eax, dword [.index]
    ; load the vtable pointer
    mov r10, [rcx]
    ; deref and jump
    jmp [r10 + rax * 8]
  .index:
    dd 0

ALIGN 8, db 0xCC
  .end:

VtblProxyStubSize equ vtbl_proxy_stub.end - vtbl_proxy_stub
VtblProxyStubIndex equ vtbl_proxy_stub.index - vtbl_proxy_stub