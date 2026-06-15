
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Windows;

public partial class SpeechToTextWindow : Window
{
	private bool _isDraggable = false;

	private readonly OverlayWindowScaler _scaler;

	private float _windowVisibilityTimer = 0f;
	private float _finalVisibilityTimer = 0f;

	private int _speakingCarIdx = -1;

	// Sample text shown while "make all overlay windows visible and draggable" is enabled, so the window can be positioned and scaled
	private const string SampleFinalDriver = "#24 Jane Sample";
	private const string SampleFinalMessage = "Box this lap, box this lap — we are switching to plan B for the final stint.";
	private const string SamplePartialDriver = "#7 John Placeholder";
	private const string SamplePartialMessage = "Copy that, I am pushing now and closing the gap to the car ahead…";

	public SpeechToTextWindow()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SpeechToTextWindow] Constructor >>>" );

		InitializeComponent();

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		_scaler = new OverlayWindowScaler( this, () => settings.OverlaysSpeechToTextWindowScale, value => settings.OverlaysSpeechToTextWindowScale = value );

		var rectangle = settings.OverlaysSpeechToTextWindowPosition;

		Left = rectangle.Location.X;
		Top = rectangle.Location.Y;

		WindowStartupLocation = WindowStartupLocation.Manual;

		MakeDraggable();

		// Only show if draggable mode is on, otherwise it will be shown when speech is detected
		if ( App.Instance!.OverlaysDraggable )
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

			var rectangle = settings.OverlaysSpeechToTextWindowPosition;

			rectangle.Location = new System.Drawing.Point( (int) RestoreBounds.Left, (int) RestoreBounds.Top );

			settings.OverlaysSpeechToTextWindowPosition = rectangle;
		}
	}

	public void ResetWindow()
	{
		Left = 0;
		Top = 0;
	}

	public void MakeDraggable()
	{
		_isDraggable = App.Instance!.OverlaysDraggable;

		ScaleIcon.Visibility = _isDraggable ? Visibility.Visible : Visibility.Collapsed;

		if ( _isDraggable )
		{
			ShowSampleData();
		}
		else
		{
			ClearSampleData();
		}

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

	private void Window_PreviewMouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		if ( ReferenceEquals( e.OriginalSource, ScaleIcon ) )
		{
			_scaler.Start();

			e.Handled = true;
		}
	}

	private void Window_PreviewMouseLeftButtonUp( object sender, MouseButtonEventArgs e )
	{
		if ( _scaler.IsScaling )
		{
			_scaler.Stop();

			e.Handled = true;
		}
	}

	private void Window_MouseMove( object sender, System.Windows.Input.MouseEventArgs e )
	{
		_scaler.Update();
	}

	private void ShowSampleData()
	{
		_windowVisibilityTimer = 0f;
		_finalVisibilityTimer = 0f;

		Final_Driver_TextBlock.Text = SampleFinalDriver;
		Final_Driver_TextBlock.Visibility = Visibility.Visible;

		Final_Message_TextBlock.Text = SampleFinalMessage;
		Final_Message_TextBlock.Visibility = Visibility.Visible;

		Partial_Driver_TextBlock.Text = SamplePartialDriver;
		Partial_Driver_TextBlock.Visibility = Visibility.Visible;

		Partial_Message_TextBlock.Text = SamplePartialMessage;
		Partial_Message_TextBlock.Visibility = Visibility.Visible;
	}

	private void ClearSampleData()
	{
		Final_Driver_TextBlock.Text = string.Empty;
		Final_Driver_TextBlock.Visibility = Visibility.Collapsed;

		Final_Message_TextBlock.Text = string.Empty;
		Final_Message_TextBlock.Visibility = Visibility.Collapsed;

		Partial_Driver_TextBlock.Text = string.Empty;
		Partial_Driver_TextBlock.Visibility = Visibility.Collapsed;

		Partial_Message_TextBlock.Text = string.Empty;
		Partial_Message_TextBlock.Visibility = Visibility.Collapsed;
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

		// while sample data is being shown for overlay setup, ignore real speech text
		if ( app.OverlaysDraggable )
		{
			return;
		}

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

		// while sample data is being shown for overlay setup, ignore real speech text
		if ( app.OverlaysDraggable )
		{
			return;
		}

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

				if ( !app.OverlaysDraggable )
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
