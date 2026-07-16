using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using WinFormsCursor = System.Windows.Forms.Cursor;

namespace MarvinsAIRARefactored.Classes;

// Drives an overlay window's position by tracking mouse movement after the user clicks the move icon.
// While moving, the cursor is hidden and locked to a fixed anchor so the movement range is unbounded.
// Moving the mouse drags the window by the same amount.
public sealed class OverlayWindowMover
{
	private readonly Window _window;
	private readonly FrameworkElement _dragHandle;

	private bool _isMoving = false;
	private System.Drawing.Point _anchor;

	public OverlayWindowMover( Window window, FrameworkElement dragHandle )
	{
		_window = window;
		_dragHandle = dragHandle;
	}

	public bool IsMoving => _isMoving;

	public void Start()
	{
		if ( _isMoving )
		{
			return;
		}

		_isMoving = true;

		_anchor = WinFormsCursor.Position;

		Mouse.OverrideCursor = System.Windows.Input.Cursors.None;

		_window.LostMouseCapture += Window_LostMouseCapture;

		_window.CaptureMouse();
	}

	public void Update()
	{
		if ( !_isMoving )
		{
			return;
		}

		var position = WinFormsCursor.Position;

		var deltaX = position.X - _anchor.X;
		var deltaY = position.Y - _anchor.Y;

		if ( ( deltaX == 0 ) && ( deltaY == 0 ) )
		{
			return;
		}

		// cursor deltas are in physical screen pixels; Left/Top are in device-independent units
		var dpiScale = VisualTreeHelper.GetDpi( _window );

		_window.Left += deltaX / dpiScale.DpiScaleX;
		_window.Top += deltaY / dpiScale.DpiScaleY;

		// recenter the hidden cursor so the user can keep moving in the same direction indefinitely
		WinFormsCursor.Position = _anchor;
	}

	public void Stop()
	{
		if ( !_isMoving )
		{
			return;
		}

		_isMoving = false;

		// place the cursor at the center of the drag handle's new on-screen location so it reappears under the button the user was holding,
		// instead of back at the anchor point where the drag started
		var center = _dragHandle.PointToScreen( new System.Windows.Point( _dragHandle.ActualWidth / 2, _dragHandle.ActualHeight / 2 ) );

		WinFormsCursor.Position = new System.Drawing.Point( (int) center.X, (int) center.Y );

		_window.LostMouseCapture -= Window_LostMouseCapture;

		_window.ReleaseMouseCapture();

		Mouse.OverrideCursor = null;
	}

	private void Window_LostMouseCapture( object sender, System.Windows.Input.MouseEventArgs e )
	{
		Stop();
	}
}
