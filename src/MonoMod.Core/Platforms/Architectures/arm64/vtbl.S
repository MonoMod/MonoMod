// ./get-bytes.sh vtbl
// vtable proxy stub

.text
.global _main

_main:
    ldr x0, [x0, #8]
    ldr x9, [x0]
    ldr w10, _offset
    add x9, x9, x10
    ldr x9, [x9]
    br x9

_offset: .word 0x0
