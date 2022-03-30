using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: TypeForwardedTo(typeof(MemoryMarshal))]
[assembly: TypeForwardedTo(typeof(SequenceMarshal))]

[assembly: TypeForwardedTo(typeof(Memory<>))]
[assembly: TypeForwardedTo(typeof(MemoryExtensions))]
[assembly: TypeForwardedTo(typeof(ReadOnlyMemory<>))]
[assembly: TypeForwardedTo(typeof(ReadOnlySpan<>))]
[assembly: TypeForwardedTo(typeof(Span<>))]