// This empty library is used only at link time with SONAME libgcc_s.so.1.
// --no-as-needed records that runtime dependency even though it defines no symbols.
// It is not embedded or shipped: Linux must load the real system libgcc_s.so.1.
.section .note.GNU-stack,"",%progbits
