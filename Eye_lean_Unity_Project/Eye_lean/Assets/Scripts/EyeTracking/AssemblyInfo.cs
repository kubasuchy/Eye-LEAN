using System.Runtime.CompilerServices;

// Expose the internal test seams (RecordableRegistry.Register / Unregister /
// ResetForTests) to the EditMode test asmdef without making them part of the
// public API surface. EyeLean.Core does NOT make these public — researcher
// code should not call them.
[assembly: InternalsVisibleTo("EyeLean.Tests.EditMode")]
