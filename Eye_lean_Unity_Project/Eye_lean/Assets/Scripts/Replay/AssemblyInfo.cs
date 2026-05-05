using System.Runtime.CompilerServices;

// Expose ReplayMode.ResetForTests to the EditMode test asmdef.
[assembly: InternalsVisibleTo("EyeLean.Tests.EditMode")]
