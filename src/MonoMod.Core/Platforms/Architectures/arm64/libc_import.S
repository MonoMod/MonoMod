// This empty library is used only at link time with SONAME libc.so.
// --no-as-needed records that runtime dependency even though it defines no symbols.
// It is not embedded or shipped: Android must load the real system libc.so (bionic).
.section .note.GNU-stack,"",%progbits