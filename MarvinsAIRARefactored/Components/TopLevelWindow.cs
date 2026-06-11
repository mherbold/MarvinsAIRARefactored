
using System.Windows.Interop;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MarvinsAIRARefactored.Components;

public class TopLevelWindow
{
	private HwndSource? _source;
	public IntPtr WindowHandle { get; private set; } = 0;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[TopLevelWindow] Initialize >>>" );

		var hwndSourceParameters = new HwndSourceParameters( "MAIRA DirectInput Host" )
		{
			Width = 1,
			Height = 1,
			PositionX = -10000,
			PositionY = -10000,

			WindowStyle = unchecked((int) (
				(uint) WINDOW_STYLE.WS_POPUP |
				(uint) WINDOW_STYLE.WS_CLIPSIBLINGS |
				(uint) WINDOW_STYLE.WS_CLIPCHILDREN
			)),

			ExtendedWindowStyle = unchecked((int) (
				(uint) WINDOW_EX_STYLE.WS_EX_TOOLWINDOW |  // hides from Alt+Tab
				(uint) WINDOW_EX_STYLE.WS_EX_NOACTIVATE    // does not steal focus
														   // intentionally NOT WS_EX_APPWINDOW
			))
		};

		_source = new HwndSource( hwndSourceParameters );

		WindowHandle = _source.Handle;

		// Make sure it stays hidden (not strictly required; it has no taskbar/Alt+Tab presence anyway)
		PInvoke.ShowWindow( (HWND) WindowHandle, SHOW_WINDOW_CMD.SW_HIDE );

		app.Logger.WriteLine( "[TopLevelWindow] <<< Initialize" );
	}

	public void Dispose()
	{
		_source?.Dispose();
		_source = null;

		WindowHandle = IntPtr.Zero;
	}
}
