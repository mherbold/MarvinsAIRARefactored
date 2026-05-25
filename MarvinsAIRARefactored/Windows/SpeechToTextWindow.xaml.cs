
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

using PInvoke;

using static PInvoke.User32;

namespace MarvinsAIRARefactored.Windows;

public partial class SpeechToTextWindow : Window
{
	private bool _isDraggable = false;

	private float _windowVisibilityTimer = 0f;
	private float _finalVisibilityTimer = 0f;

	private int _speakingCarIdx = -1;
	private DateTime _speakingTimestamp;

	public SpeechToTextWindow()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SpeechToTextWindow] Constructor >>>" );

		InitializeComponent();

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var rectangle = settings.SpeechToTextOverlayWindowPosition;

		Left = rectangle.Location.X;
		Top = rectangle.Location.Y;

		WindowStartupLocation = WindowStartupLocation.Manual;

		MakeDraggable();

		// Only show if draggable mode is on, otherwise it will be shown when speech is detected
		if ( settings.SpeechToTextMakeOverlayWindowDraggable )
		{
			Show();
		}

		app.Logger.WriteLine( "[SpeechToTextWindow] <<< Constructor" );
	}

	private void Window_LocationChanged( object sender, EventArgs e )
	{
		if ( IsVisible && ( WindowState == WindowState.Normal ) )
		{
			var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

			var rectangle = settings.SpeechToTextOverlayWindowPosition;

			rectangle.Location = new System.Drawing.Point( (int) RestoreBounds.Left, (int) RestoreBounds.Top );

			settings.SpeechToTextOverlayWindowPosition = rectangle;
		}
	}

	public void ResetWindow()
	{
		Left = 0;
		Top = 0;
	}

	public void MakeDraggable()
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		_isDraggable = settings.SpeechToTextMakeOverlayWindowDraggable;

		var hwnd = new WindowInteropHelper( this ).Handle;

		var exStyle = User32.GetWindowLong( hwnd, WindowLongIndexFlags.GWL_EXSTYLE );

		if ( _isDraggable )
		{
			_ = User32.SetWindowLong( hwnd, WindowLongIndexFlags.GWL_EXSTYLE, (SetWindowLongFlags) ( (uint) exStyle & (uint) ~SetWindowLongFlags.WS_EX_TRANSPARENT ) );
		}
		else
		{
			_ = User32.SetWindowLong( hwnd, WindowLongIndexFlags.GWL_EXSTYLE, (SetWindowLongFlags) ( (uint) exStyle | (uint) SetWindowLongFlags.WS_EX_TRANSPARENT ) );
		}
	}

	protected override void OnMouseLeftButtonDown( MouseButtonEventArgs e )
	{
		if ( _isDraggable )
		{
			DragMove();
		}
	}

	public void SetPartialText( string text )
	{
		Dispatcher.BeginInvoke( () =>
		{
			var app = App.Instance!;

			app.Logger.WriteLine( $"[SpeechToText] Got partial text ({text})" );

			var elapsedTime = DateTime.UtcNow - _speakingTimestamp;

			if ( _speakingCarIdx == -1 || ( elapsedTime.TotalSeconds > 1 ) )
			{
				_speakingCarIdx = app.Simulator.LastRadioTransmitCarIdx;

				app.Logger.WriteLine( $"[SpeechToText] Speaking car index was not set - now set to {_speakingCarIdx}" );
			}

			_speakingTimestamp = DateTime.UtcNow;

			var driver = app.Simulator.GetDriver( _speakingCarIdx );

			if ( driver != null )
			{
				Partial_Driver_TextBlock.Visibility = Visibility.Visible;
				Partial_Driver_TextBlock.Text = $"#{driver.CarNumber} {driver.UserName}";
			}
			else
			{
				Partial_Driver_TextBlock.Visibility = Visibility.Collapsed;
			}

			Partial_Message_TextBlock.Text = text;

			Partial_Driver_TextBlock.Visibility = Visibility.Visible;
			Partial_Message_TextBlock.Visibility = Visibility.Visible;

			_windowVisibilityTimer = 10f;

			Show();
		} );
	}

	public void SetFinalText( string text )
	{
		Dispatcher.BeginInvoke( () =>
		{
			var app = App.Instance!;

			app.Logger.WriteLine( $"[SpeechToText] Got final text ({text})" );

			var driver = app.Simulator.GetDriver( _speakingCarIdx );

			_speakingCarIdx = -1;

			app.Logger.WriteLine( $"[SpeechToText] Speaking car index cleared" );

			if ( driver != null )
			{
				Final_Driver_TextBlock.Visibility = Visibility.Visible;
				Final_Driver_TextBlock.Text = $"#{driver.CarNumber} {driver.UserName}";
			}
			else
			{
				Final_Driver_TextBlock.Visibility = Visibility.Collapsed;
			}

			Final_Message_TextBlock.Text = text;

			Final_Driver_TextBlock.Visibility = Visibility.Visible;
			Final_Message_TextBlock.Visibility = Visibility.Visible;

			Partial_Driver_TextBlock.Visibility = Visibility.Collapsed;
			Partial_Message_TextBlock.Visibility = Visibility.Collapsed;

			_windowVisibilityTimer = 10f;
			_finalVisibilityTimer = 10f;

			Show();
		} );
	}

	public void Tick( App app )
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		if ( _windowVisibilityTimer > 0f )
		{
			_windowVisibilityTimer -= 1f / 60f;

			if ( _windowVisibilityTimer <= 0f )
			{
				_finalVisibilityTimer = 0f;

				Final_Driver_TextBlock.Visibility = Visibility.Collapsed;
				Final_Message_TextBlock.Visibility = Visibility.Collapsed;

				Partial_Driver_TextBlock.Visibility = Visibility.Collapsed;
				Partial_Message_TextBlock.Visibility = Visibility.Collapsed;

				if ( !settings.SpeechToTextMakeOverlayWindowDraggable )
				{
					Hide();
				}
			}
		}

		if ( _finalVisibilityTimer > 0f )
		{
			_finalVisibilityTimer -= 1f / 60f;

			if ( _finalVisibilityTimer <= 0f )
			{
				Final_Driver_TextBlock.Visibility = Visibility.Collapsed;
				Final_Message_TextBlock.Visibility = Visibility.Collapsed;
			}
		}
	}
}
