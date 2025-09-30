// ./get-bytes.sh eh
// exception handler thunk aka SpecialEntryStub

.text
.global _main

_main:
    ldr x9, _argument
    ldr x10, _target
    br x10
    nop

_argument: .quad 0x0
_target: .quad 0x0
