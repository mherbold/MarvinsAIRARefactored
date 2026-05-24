
using System.Windows.Interop;

using static PInvoke.User32;

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
				(uint) WindowStyles.WS_POPUP |
				(uint) WindowStyles.WS_CLIPSIBLINGS |
				(uint) WindowStyles.WS_CLIPCHILDREN
			)),
			ExtendedWindowStyle = unchecked((int) (
				(uint) WindowStylesEx.WS_EX_TOOLWINDOW | // hides from Alt+Tab
				(uint) WindowStylesEx.WS_EX_NOACTIVATE       // don’t steal focus
															 // intentionally NOT WS_EX_APPWINDOW
			))
		};

		_source = new HwndSource( hwndSourceParameters );

		WindowHandle = _source.Handle;

		// Make sure it stays hidden (not strictly required; it has no taskbar/Alt+Tab presence anyway)
		ShowWindow( WindowHandle, WindowShowStyle.SW_HIDE );

		app.Logger.WriteLine( "[TopLevelWindow] <<< Initialize" );
	}

	public void Dispose()
	{
		_source?.Dispose();
		_source = null;

		WindowHandle = IntPtr.Zero;
	}
}
