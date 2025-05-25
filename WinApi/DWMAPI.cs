
using System.Runtime.InteropServices;

namespace MarvinsAIRARefactored.WinApi;

public partial class DWMAPI
{
	public enum cbAttribute : uint
	{
		DWMWA_USE_IMMERSIVE_DARK_MODE = 20
	}

	// HRESULT DwmSetWindowAttribute( HWND hwnd, DWORD dwAttribute, LPCVOID pvAttribute, DWORD cbAttribute );
	[LibraryImport( "dwmapi.dll" )]
	public static partial int DwmSetWindowAttribute( nint hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute );
}
