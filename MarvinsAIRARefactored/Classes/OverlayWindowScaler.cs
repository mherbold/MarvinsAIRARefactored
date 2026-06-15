using System.Windows;
using System.Windows.Input;

using WinFormsCursor = System.Windows.Forms.Cursor;

namespace MarvinsAIRARefactored.Classes;

// Drives an overlay window's scale by tracking mouse movement after the user clicks the scale icon.
// While scaling, the cursor is hidden and locked to a fixed anchor so the movement range is unbounded.
// Moving right or down increases the scale; moving left or up decreases it.
public sealed class OverlayWindowScaler
{
	// How much the scale changes per pixel of cursor movement
	private const float Sensitivity = 0.005f;

	private readonly Window _window;
	private readonly Func<float> _getScale;
	private readonly Action<float> _setScale;

	private bool _isScaling = false;
	private System.Drawing.Point _anchor;

	public OverlayWindowScaler( Window window, Func<float> getScale, Action<float> setScale )
	{
		_window = window;
		_getScale = getScale;
		_setScale = setScale;
	}

	public bool IsScaling => _isScaling;

	public void Start()
	{
		if ( _isScaling )
		{
			return;
		}

		_isScaling = true;

		_anchor = WinFormsCursor.Position;

		Mouse.OverrideCursor = System.Windows.Input.Cursors.None;

		_window.LostMouseCapture += Window_LostMouseCapture;

		_window.CaptureMouse();
	}

	public void Update()
	{
		if ( !_isScaling )
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

		// moving right or down increases the scale; left or up decreases it (the setter clamps the value)
		_setScale( _getScale() + ( deltaX + deltaY ) * Sensitivity );

		// recenter the hidden cursor so the user can keep moving in the same direction indefinitely
		WinFormsCursor.Position = _anchor;
	}

	public void Stop()
	{
		if ( !_isScaling )
		{
			return;
		}

		_isScaling = false;

		_window.LostMouseCapture -= Window_LostMouseCapture;

		_window.ReleaseMouseCapture();

		Mouse.OverrideCursor = null;
	}

	private void Window_LostMouseCapture( object sender, System.Windows.Input.MouseEventArgs e )
	{
		Stop();
	}
}
