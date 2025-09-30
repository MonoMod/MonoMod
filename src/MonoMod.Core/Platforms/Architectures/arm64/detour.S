// ./get-bytes.sh detour
// detour thunk

.text
.global _main

_main:
    ldr x9, _target
    br x9

_target: .quad 0x0
