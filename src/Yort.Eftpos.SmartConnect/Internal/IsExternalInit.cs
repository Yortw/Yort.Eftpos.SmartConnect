using System.ComponentModel;

namespace System.Runtime.CompilerServices;

// Polyfill that enables C# 'init' accessors on netstandard2.0. The runtime supplies this type on net5+;
// declaring our own internal copy lets the compiler emit init-only setters on older targets. Harmless and standard.
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}
