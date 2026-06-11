
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MarvinsAIRARefactored.Windows;

public partial class SpeechToTextWindow : Window
{
	private bool _isDraggable = false;

	private float _windowVisibilityTimer = 0f;
	private float _finalVisibilityTimer = 0f;

	private int _speakingCarIdx = -1;

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

		_isDraggable = settings.SteeringEffectsMakeGripOMeterDraggable;

		var hwnd = new WindowInteropHelper( this ).Handle;

		var exStyle = PInvoke.GetWindowLong( (HWND) hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE );

		if ( _isDraggable )
		{
			exStyle &= ~(int) WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
		}
		else
		{
			exStyle |= (int) WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
		}

		_ = PInvoke.SetWindowLong( (HWND) hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle );
	}

	protected override void OnMouseLeftButtonDown( MouseButtonEventArgs e )
	{
		if ( _isDraggable )
		{
			DragMove();
		}
	}

	private static string TruncateForLog( string text, int maxLength )
	{
		if ( text.Length <= maxLength )
		{
			return text;
		}

		return text[..maxLength];
	}

	private static string FormatDriverLabel( App app, int carIdx )
	{
		var driver = app.Simulator.GetDriver( carIdx );

		if ( driver is null )
		{
			return string.Empty;
		}

		return $"#{driver.CarNumber} {driver.UserName}";
	}

	public void SetPartialText( string text )
	{
		var app = App.Instance!;

		var radioTransmitCarIdx = app.Simulator.RadioTransmitCarIdx;
		var lastRadioTransmitCarIdx = app.Simulator.LastRadioTransmitCarIdx;

		Dispatcher.BeginInvoke( () =>
		{
			var previousSpeakingCarIdx = _speakingCarIdx;

			var chosenCarIdx = radioTransmitCarIdx;

			if ( chosenCarIdx == -1 )
			{
				chosenCarIdx = lastRadioTransmitCarIdx;
			}

			var driverLabel = ( chosenCarIdx != -1 ) ? FormatDriverLabel( app, chosenCarIdx ) : string.Empty;

			app.Logger.WriteLine( $"[SpeechToTextWindow] Partial text: radioTransmitCarIdx={radioTransmitCarIdx}, lastRadioTransmitCarIdx={lastRadioTransmitCarIdx}, previousSpeakingCarIdx={previousSpeakingCarIdx}, chosenCarIdx={chosenCarIdx}, driver='{driverLabel}', text='{TruncateForLog( text, 200 )}'" );

			_speakingCarIdx = chosenCarIdx;

			if ( !string.IsNullOrWhiteSpace( driverLabel ) )
			{
				Partial_Driver_TextBlock.Visibility = Visibility.Visible;
				Partial_Driver_TextBlock.Text = driverLabel;
			}
			else
			{
				Partial_Driver_TextBlock.Visibility = Visibility.Collapsed;
				Partial_Driver_TextBlock.Text = string.Empty;
			}

			Partial_Message_TextBlock.Text = text;
			Partial_Message_TextBlock.Visibility = Visibility.Visible;

			_windowVisibilityTimer = 10f;

			Show();
		} );
	}

	public void SetFinalText( string text )
	{
		var app = App.Instance!;

		var radioTransmitCarIdx = app.Simulator.RadioTransmitCarIdx;
		var lastRadioTransmitCarIdx = app.Simulator.LastRadioTransmitCarIdx;

		Dispatcher.BeginInvoke( () =>
		{
			var chosenCarIdx = _speakingCarIdx;

			if ( chosenCarIdx == -1 )
			{
				chosenCarIdx = radioTransmitCarIdx;

				if ( chosenCarIdx == -1 )
				{
					chosenCarIdx = lastRadioTransmitCarIdx;
				}
			}

			var driverLabel = ( chosenCarIdx != -1 ) ? FormatDriverLabel( app, chosenCarIdx ) : string.Empty;

			app.Logger.WriteLine( $"[SpeechToTextWindow] Final text: radioTransmitCarIdx={radioTransmitCarIdx}, lastRadioTransmitCarIdx={lastRadioTransmitCarIdx}, chosenCarIdx={chosenCarIdx}, driver='{driverLabel}', text='{TruncateForLog( text, 200 )}'" );

			_speakingCarIdx = -1;

			if ( !string.IsNullOrWhiteSpace( driverLabel ) )
			{
				Final_Driver_TextBlock.Visibility = Visibility.Visible;
				Final_Driver_TextBlock.Text = driverLabel;
			}
			else
			{
				Final_Driver_TextBlock.Visibility = Visibility.Collapsed;
				Final_Driver_TextBlock.Text = string.Empty;
			}

			Final_Message_TextBlock.Text = text;
			Final_Message_TextBlock.Visibility = Visibility.Visible;

			Partial_Driver_TextBlock.Visibility = Visibility.Collapsed;
			Partial_Driver_TextBlock.Text = string.Empty;

			Partial_Message_TextBlock.Visibility = Visibility.Collapsed;
			Partial_Message_TextBlock.Text = string.Empty;

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
				Final_Driver_TextBlock.Text = string.Empty;

				Final_Message_TextBlock.Visibility = Visibility.Collapsed;
				Final_Message_TextBlock.Text = string.Empty;

				Partial_Driver_TextBlock.Visibility = Visibility.Collapsed;
				Partial_Driver_TextBlock.Text = string.Empty;

				Partial_Message_TextBlock.Visibility = Visibility.Collapsed;
				Partial_Message_TextBlock.Text = string.Empty;

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
				Final_Driver_TextBlock.Text = string.Empty;

				Final_Message_TextBlock.Visibility = Visibility.Collapsed;
				Final_Message_TextBlock.Text = string.Empty;
			}
		}
	}
}
