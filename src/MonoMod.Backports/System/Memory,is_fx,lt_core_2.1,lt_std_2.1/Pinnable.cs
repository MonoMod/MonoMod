// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace System {
    //
    // This class exists solely so that arbitrary objects can be Unsafe-casted to it to get a ref to the start of the user data.
    //
    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("Performance", "CA1812", Justification = "Objects are unsafe-casted to this to be stored in Memory and Span")]
    internal sealed class Pinnable<T> {
        public T Data = default!;
    }
}