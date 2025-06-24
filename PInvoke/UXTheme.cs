
using System.Runtime.InteropServices;

namespace MarvinsAIRARefactored.PInvoke;

partial class UXTheme
{
	[LibraryImport( "UXTheme.dll", EntryPoint = "#138", SetLastError = true)]
	[return: MarshalAs( UnmanagedType.Bool )]
	public static partial bool ShouldSystemUseDarkMode();
}
