
// This file is used by Code Analysis to maintain SuppressMessage 
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given 
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Usage", "xUnit1013:Public method should be marked as test",
    Justification = "Some test-related methods need to be public. Marking them as tests would be wrong."
)]

