;:; nasm -f macho64 -O0 libSystem.shim.asm -o libSystem.shim.o && ld64.lld -dylib -arch x86_64 -platform_version macos 10.6 10.6 -x -o libSystem.dylib libSystem.shim.o
; intentionally left empty