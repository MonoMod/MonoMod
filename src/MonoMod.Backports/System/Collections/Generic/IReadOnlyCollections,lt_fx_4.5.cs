// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Collections.Generic {
    // Provides a read-only, covariant view of a generic list.
    // Note: Because .NET 3 does not allow for covariance for IEnumerable, we cannot support that
    public interface IReadOnlyCollection<T> : IEnumerable<T> {
        int Count {
            get;
        }
    }

    // Note: Because .NET 3 does not allow for covariance for IEnumerable, we cannot support that
    [Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix",
        Justification = "This is a polyfill for the BCL's IReadOnlyList")]
    public interface IReadOnlyList<T> : IReadOnlyCollection<T> {
        T this[int index] {
            get;
        }
    }
}
