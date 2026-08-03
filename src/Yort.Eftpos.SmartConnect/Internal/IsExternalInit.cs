#if !NET5_0_OR_GREATER

using System.ComponentModel;

namespace System.Runtime.CompilerServices;

// Polyfill that enables C# 'init' accessors on netstandard2.0. The runtime supplies this type on net5+, where
// compiling our own copy would collide with the framework's (CS0436) and — with TreatWarningsAsErrors — break
// the build, hence the guard. Declaring it lets the compiler emit init-only setters on older targets.
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}

#endif
