using System;
using System.Buffers;
using System.Runtime.CompilerServices;

[assembly: TypeForwardedTo(typeof(IMemoryOwner<>))]
[assembly: TypeForwardedTo(typeof(IPinnable))]
[assembly: TypeForwardedTo(typeof(IBufferWriter<>))]
[assembly: TypeForwardedTo(typeof(MemoryHandle))]
[assembly: TypeForwardedTo(typeof(MemoryManager<>))]
[assembly: TypeForwardedTo(typeof(StandardFormat))]
[assembly: TypeForwardedTo(typeof(BuffersExtensions))]

[assembly: TypeForwardedTo(typeof(ReadOnlySequenceSegment<>))]
[assembly: TypeForwardedTo(typeof(ReadOnlySequence<>))]
[assembly: TypeForwardedTo(typeof(SequencePosition))]