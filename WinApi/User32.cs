
using System.Runtime.InteropServices;

namespace MarvinsAIRARefactored.WinApi;

partial class User32
{
	[LibraryImport( "user32.dll", SetLastError = true, EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16 )]
	public static partial IntPtr FindWindow( string? lpClassName, string lpWindowName );

	[return: MarshalAs( UnmanagedType.Bool )]
	[LibraryImport( "user32.dll", SetLastError = true, EntryPoint = "PostMessageA" )]
	public static partial bool PostMessage( IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam );
}
