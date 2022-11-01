:; nasm -f bin -Ox stubs_x86_64_win.asm -o stubs_x86_64_win.bin

BITS 64
DEFAULT REL

[map sections stubs_x86_64_win.map]
[map symbols]

SECTION .vtbl_proxy_stub align=256

vtbl_proxy_stub:
    ; replace rcx with the real pointer
    mov rcx, [rcx + 8]
    ; load the real vtable pointer
    mov rax, [rcx]
    ; jump to the actual method
    ; the offset field will be overwritten with the index (times 8)
    jmp [rax + 0x55555555] ; using 0x55555555 forces NASM to use a dword offset
  .after_index:

ALIGN 8, db 0xCC
  .end:

VtblProxyStubSize equ vtbl_proxy_stub.end - vtbl_proxy_stub
VtblProxyStubIndex equ vtbl_proxy_stub.after_index - vtbl_proxy_stub - 4