;:; nasm -f elf64 -Ox test.asm -o testa.o

BITS 64
DEFAULT REL

section .text

extern throwex
extern caller

global px_call_throwex
global px_call_caller

extern eh_native_to_managed
extern eh_managed_to_native

px_call_throwex:
    lea rax, [throwex]
    jmp eh_managed_to_native wrt ..plt
px_call_caller:
    lea rax, [caller]
    jmp eh_native_to_managed wrt ..plt