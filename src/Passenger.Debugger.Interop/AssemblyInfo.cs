#nullable enable

using System.Runtime.CompilerServices;

// The declarations pass structs as they lie in memory, which the generated marshalling needs to be told.
[assembly: DisableRuntimeMarshalling]
