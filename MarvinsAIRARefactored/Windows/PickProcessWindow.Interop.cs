using System.Runtime.InteropServices;
using System.Text;

namespace MarvinsAIRARefactored.Windows;

partial class PickProcessWindow
{
	[DllImport( "kernel32.dll", SetLastError = true )]
	private static extern IntPtr OpenProcess( uint dwDesiredAccess, bool bInheritHandle, int dwProcessId );

	[DllImport( "kernel32.dll", SetLastError = true )]
	[return: MarshalAs( UnmanagedType.Bool )]
	private static extern bool CloseHandle( IntPtr hObject );

	[DllImport( "kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode )]
	private static extern bool QueryFullProcessImageName( IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize );
}
