
using System.Runtime.InteropServices;

namespace MarvinsAIRARefactored.WinApi;

partial class UXTheme
{
	[LibraryImport( "UXTheme.dll", EntryPoint = "#138", SetLastError = true)]
	[return: MarshalAs( UnmanagedType.Bool )]
	public static partial bool ShouldSystemUseDarkMode();
}
