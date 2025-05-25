
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using ScrollEventArgs = System.Windows.Controls.Primitives.ScrollEventArgs;
using TabControl = System.Windows.Controls.TabControl;

using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.WinApi;

namespace MarvinsAIRARefactored;

public partial class MainWindow : Window
{
	public nint WindowHandle { get; private set; } = 0;
	public bool GraphsTabItemIsVisible { get; private set; } = false;
	public bool DebugTabItemIsVisible { get; private set; } = false;

	private Point _lastMousePosition = new();

	private bool _racingWheelMaxForceCapturedMouse = false;
	private bool _racingWheelDetailStrengthCapturedMouse = false;
	private bool _racingWheelParkedStrengthCapturedMouse = false;
	private bool _racingWheelSoftLockStrengthCapturedMouse = false;
	private bool _racingWheelFrictionCapturedMouse = false;

	public MainWindow()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[MainWindow] Constructor >>>" );

			InitializeComponent();

			var version = Misc.GetVersion();

			app.Logger.WriteLine( $"[MainWindow] Version is {version}" );

			Title = Components.DataContext.Instance.Localization[ "AppTitle" ] + " " + Misc.GetVersion();

			RacingWheel.SetAlgorithmComboBoxItemsSource( RacingWheelAlgorithm_ComboBox );

			Components.Localization.SetLanguageComboBoxItemsSource( AppLanguage_ComboBox );

			app.Logger.WriteLine( "[MainWindow] <<< Constructor" );
		}
	}

	public void Initialize()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[MainWindow] Initialize >>>" );

			WindowHandle = new WindowInteropHelper( this ).Handle;

			var value = UXTheme.ShouldSystemUseDarkMode() ? 1 : 0;

			DWMAPI.DwmSetWindowAttribute( WindowHandle, (uint) DWMAPI.cbAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, (uint) System.Runtime.InteropServices.Marshal.SizeOf( value ) );

			UpdateRacingWheelPowerIcon();
			UpdateAppTopmostWindowEnabledIcon();

			app.Logger.WriteLine( "[MainWindow] <<< Initialize" );
		}
	}

	public void UpdateRacingWheelPowerIcon()
	{
		var app = App.Instance;

		if ( app != null )
		{
			Dispatcher.BeginInvoke( () =>
			{
				var redIconVisibility = Visibility.Hidden;
				var yellowIconVisibility = Visibility.Hidden;
				var greenIconVisibility = Visibility.Hidden;

				if ( !Components.DataContext.Instance.Settings.RacingWheelEnableForceFeedback )
				{
					redIconVisibility = Visibility.Visible;
				}
				else if ( app.RacingWheel.Suspend )
				{
					yellowIconVisibility = Visibility.Visible;
				}
				else
				{
					greenIconVisibility = Visibility.Visible;
				}

				RacingWheelPowerRed_Image.Visibility = redIconVisibility;
				RacingWheelPowerYellow_Image.Visibility = yellowIconVisibility;
				RacingWheelPowerGreen_Image.Visibility = greenIconVisibility;
			} );
		}
	}

	public void UpdateRacingWheelFadeEnabledIcon()
	{
		UpdateSwitchIcon( Components.DataContext.Instance.Settings.RacingWheelFadeEnabled, RacingWheelFade_Off_Image, RacingWheelFade_On_Image );
	}

	public void UpdateAppTopmostWindowEnabledIcon()
	{
		UpdateSwitchIcon( Components.DataContext.Instance.Settings.AppTopmostWindowEnabled, AppTopmostWindow_Off_Image, AppTopmostWindow_On_Image );
	}

	private void UpdateSwitchIcon( bool switchIsOn, Image offImage, Image onImage )
	{
		Dispatcher.BeginInvoke( () =>
		{
			var offIconVisibility = Visibility.Hidden;
			var onIconVisibility = Visibility.Hidden;

			if ( !switchIsOn )
			{
				offIconVisibility = Visibility.Visible;
			}
			else
			{
				onIconVisibility = Visibility.Visible;
			}

			offImage.Visibility = offIconVisibility;
			onImage.Visibility = onIconVisibility;
		} );
	}

	private void TabControl_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( e.Source is TabControl tabControl )
		{
			if ( tabControl.SelectedItem is TabItem selectedTab )
			{
				GraphsTabItemIsVisible = ( selectedTab == Graphs_TabItem );
				DebugTabItemIsVisible = ( selectedTab == Debug_TabItem );
			}
		}
	}

	private void RacingWheelPower_Click( object sender, RoutedEventArgs e )
	{
		Components.DataContext.Instance.Settings.RacingWheelEnableForceFeedback = !Components.DataContext.Instance.Settings.RacingWheelEnableForceFeedback;
	}

	private void RacingWheelTest_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.RacingWheel.PlayTestSignal = true;
		}
	}

	private void RacingWheelReset_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.RacingWheel.Reset = true;
		}
	}

	private void RacingWheelMaxForceKnob_MouseDown( object sender, MouseButtonEventArgs e )
	{
		Mouse.Capture( (Image) sender );

		Mouse.OverrideCursor = Cursors.SizeWE;

		_lastMousePosition = e.GetPosition( null );

		_racingWheelMaxForceCapturedMouse = true;
	}

	private void RacingWheelMaxForceKnob_MouseUp( object sender, MouseButtonEventArgs e )
	{
		if ( _racingWheelMaxForceCapturedMouse )
		{
			Mouse.Capture( null );

			Mouse.OverrideCursor = null;

			_racingWheelMaxForceCapturedMouse = false;
		}
	}

	private void RacingWheelMaxForceKnob_MouseMove( object sender, MouseEventArgs e )
	{
		if ( _racingWheelMaxForceCapturedMouse )
		{
			var newPosition = e.GetPosition( null );

			if ( newPosition != _lastMousePosition )
			{
				var delta = ( newPosition.X - _lastMousePosition.X ) + ( newPosition.Y - _lastMousePosition.Y );

				Misc.AdjustKnobControl( "RacingWheelMaxForce", (float) delta * 0.01f, RacingWheelMaxForceKnob_Image, 1f );

				_lastMousePosition = newPosition;
			}
		}
	}

	private void RacingWheelMaxForcePlus_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			Misc.AdjustKnobControl( "RacingWheelMaxForce", 1f, RacingWheelMaxForceKnob_Image, 1f );
		}
	}

	private void RacingWheelMaxForceMinus_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			Misc.AdjustKnobControl( "RacingWheelMaxForce", -1f, RacingWheelMaxForceKnob_Image, 1f );
		}
	}

	private void RacingWheelDetailStrengthKnob_MouseDown( object sender, MouseButtonEventArgs e )
	{
		Mouse.Capture( (Image) sender );

		Mouse.OverrideCursor = Cursors.SizeWE;

		_lastMousePosition = e.GetPosition( null );

		_racingWheelDetailStrengthCapturedMouse = true;
	}

	private void RacingWheelDetailStrengthKnob_MouseUp( object sender, MouseButtonEventArgs e )
	{
		if ( _racingWheelDetailStrengthCapturedMouse )
		{
			Mouse.Capture( null );

			Mouse.OverrideCursor = null;

			_racingWheelDetailStrengthCapturedMouse = false;
		}
	}

	private void RacingWheelDetailStrengthKnob_MouseMove( object sender, MouseEventArgs e )
	{
		if ( _racingWheelDetailStrengthCapturedMouse )
		{
			var newPosition = e.GetPosition( null );

			if ( newPosition != _lastMousePosition )
			{
				var delta = ( newPosition.X - _lastMousePosition.X ) + ( newPosition.Y - _lastMousePosition.Y );

				Misc.AdjustKnobControl( "RacingWheelDetailStrength", (float) delta * 0.001f, RacingWheelDetailStrengthKnob_Image, 10f );

				_lastMousePosition = newPosition;
			}
		}
	}

	private void RacingWheelDetailStrengthPlus_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			Misc.AdjustKnobControl( "RacingWheelDetailStrength", 0.1f, RacingWheelDetailStrengthKnob_Image, 10f );
		}
	}

	private void RacingWheelDetailStrengthMinus_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			Misc.AdjustKnobControl( "RacingWheelDetailStrength", -0.1f, RacingWheelDetailStrengthKnob_Image, 10f );
		}
	}

	private void RacingWheelParkedStrengthKnob_MouseDown( object sender, MouseButtonEventArgs e )
	{
		Mouse.Capture( (Image) sender );

		Mouse.OverrideCursor = Cursors.SizeWE;

		_lastMousePosition = e.GetPosition( null );

		_racingWheelParkedStrengthCapturedMouse = true;
	}

	private void RacingWheelParkedStrengthKnob_MouseUp( object sender, MouseButtonEventArgs e )
	{
		if ( _racingWheelParkedStrengthCapturedMouse )
		{
			Mouse.Capture( null );

			Mouse.OverrideCursor = null;

			_racingWheelParkedStrengthCapturedMouse = false;
		}
	}

	private void RacingWheelParkedStrengthKnob_MouseMove( object sender, MouseEventArgs e )
	{
		if ( _racingWheelParkedStrengthCapturedMouse )
		{
			var newPosition = e.GetPosition( null );

			if ( newPosition != _lastMousePosition )
			{
				var delta = ( newPosition.X - _lastMousePosition.X ) + ( newPosition.Y - _lastMousePosition.Y );

				Misc.AdjustKnobControl( "RacingWheelParkedStrength", (float) delta * 0.001f, RacingWheelParkedStrengthKnob_Image, 10f );

				_lastMousePosition = newPosition;
			}
		}
	}

	private void RacingWheelParkedStrengthPlus_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			Misc.AdjustKnobControl( "RacingWheelParkedStrength", 0.05f, RacingWheelParkedStrengthKnob_Image, 10f );
		}
	}

	private void RacingWheelParkedStrengthMinus_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			Misc.AdjustKnobControl( "RacingWheelParkedStrength", -0.05f, RacingWheelParkedStrengthKnob_Image, 10f );
		}
	}

	private void RacingWheelSoftLockStrengthKnob_MouseDown( object sender, MouseButtonEventArgs e )
	{
		Mouse.Capture( (Image) sender );

		Mouse.OverrideCursor = Cursors.SizeWE;

		_lastMousePosition = e.GetPosition( null );

		_racingWheelSoftLockStrengthCapturedMouse = true;
	}

	private void RacingWheelSoftLockStrengthKnob_MouseUp( object sender, MouseButtonEventArgs e )
	{
		if ( _racingWheelSoftLockStrengthCapturedMouse )
		{
			Mouse.Capture( null );

			Mouse.OverrideCursor = null;

			_racingWheelSoftLockStrengthCapturedMouse = false;
		}
	}

	private void RacingWheelSoftLockStrengthKnob_MouseMove( object sender, MouseEventArgs e )
	{
		if ( _racingWheelSoftLockStrengthCapturedMouse )
		{
			var newPosition = e.GetPosition( null );

			if ( newPosition != _lastMousePosition )
			{
				var delta = ( newPosition.X - _lastMousePosition.X ) + ( newPosition.Y - _lastMousePosition.Y );

				Misc.AdjustKnobControl( "RacingWheelSoftLockStrength", (float) delta * 0.001f, RacingWheelSoftLockStrengthKnob_Image, 10f );

				_lastMousePosition = newPosition;
			}
		}
	}

	private void RacingWheelSoftLockStrengthPlus_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			Misc.AdjustKnobControl( "RacingWheelSoftLockStrength", 0.05f, RacingWheelSoftLockStrengthKnob_Image, 10f );
		}
	}

	private void RacingWheelSoftLockStrengthMinus_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			Misc.AdjustKnobControl( "RacingWheelSoftLockStrength", -0.05f, RacingWheelSoftLockStrengthKnob_Image, 10f );
		}
	}

	private void RacingWheelFrictionKnob_MouseDown( object sender, MouseButtonEventArgs e )
	{
		Mouse.Capture( (Image) sender );

		Mouse.OverrideCursor = Cursors.SizeWE;

		_lastMousePosition = e.GetPosition( null );

		_racingWheelFrictionCapturedMouse = true;
	}

	private void RacingWheelFrictionKnob_MouseUp( object sender, MouseButtonEventArgs e )
	{
		if ( _racingWheelFrictionCapturedMouse )
		{
			Mouse.Capture( null );

			Mouse.OverrideCursor = null;

			_racingWheelFrictionCapturedMouse = false;
		}
	}

	private void RacingWheelFrictionKnob_MouseMove( object sender, MouseEventArgs e )
	{
		if ( _racingWheelFrictionCapturedMouse )
		{
			var newPosition = e.GetPosition( null );

			if ( newPosition != _lastMousePosition )
			{
				var delta = ( newPosition.X - _lastMousePosition.X ) + ( newPosition.Y - _lastMousePosition.Y );

				Misc.AdjustKnobControl( "RacingWheelFriction", (float) delta * 0.001f, RacingWheelFrictionKnob_Image, 10f );

				_lastMousePosition = newPosition;
			}
		}
	}

	private void RacingWheelFrictionPlus_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			Misc.AdjustKnobControl( "RacingWheelFriction", 0.05f, RacingWheelFrictionKnob_Image, 10f );
		}
	}

	private void RacingWheelFrictionMinus_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			Misc.AdjustKnobControl( "RacingWheelFriction", -0.05f, RacingWheelFrictionKnob_Image, 10f );
		}
	}

	private void RacingWheelFadeEnabled_Click( object sender, RoutedEventArgs e )
	{
		Components.DataContext.Instance.Settings.RacingWheelFadeEnabled = !Components.DataContext.Instance.Settings.RacingWheelFadeEnabled;
	}

	private void HeaderDataViewer_MouseWheel( object sender, MouseWheelEventArgs e )
	{
		var delta = e.Delta / 30.0f;

		if ( delta > 0 )
		{
			delta = MathF.Max( 1, delta );
		}
		else
		{
			delta = MathF.Min( -1, delta );
		}

		HeaderDataViewer_ScrollBar.Value -= delta;

		HeaderDataViewer.ScrollIndex = (int) HeaderDataViewer_ScrollBar.Value;
	}

	private void HeaderDataViewer_ScrollBar_Scroll( object sender, ScrollEventArgs e )
	{
		HeaderDataViewer.ScrollIndex = (int) e.NewValue;
	}

	private void SessionInfoViewer_MouseWheel( object sender, MouseWheelEventArgs e )
	{
		var delta = e.Delta / 30.0f;

		if ( delta > 0 )
		{
			delta = MathF.Max( 1, delta );
		}
		else
		{
			delta = MathF.Min( -1, delta );
		}

		SessionInfoViewer_ScrollBar.Value -= delta;

		SessionInfoViewer.ScrollIndex = (int) SessionInfoViewer_ScrollBar.Value;
	}

	private void SessionInfoViewer_ScrollBar_Scroll( object sender, ScrollEventArgs e )
	{
		SessionInfoViewer.ScrollIndex = (int) e.NewValue;
	}

	private void TelemetryDataViewer_MouseWheel( object sender, MouseWheelEventArgs e )
	{
		var delta = e.Delta / 30.0f;

		if ( delta > 0 )
		{
			delta = MathF.Max( 1, delta );
		}
		else
		{
			delta = MathF.Min( -1, delta );
		}

		TelemetryDataViewer_ScrollBar.Value -= delta;

		TelemetryDataViewer.ScrollIndex = (int) TelemetryDataViewer_ScrollBar.Value;
	}

	private void TelemetryDataViewer_ScrollBar_Scroll( object sender, ScrollEventArgs e )
	{
		TelemetryDataViewer.ScrollIndex = (int) e.NewValue;
	}

	private void AppTopmostWindowEnabled_Click( object sender, RoutedEventArgs e )
	{
		Components.DataContext.Instance.Settings.AppTopmostWindowEnabled = !Components.DataContext.Instance.Settings.AppTopmostWindowEnabled;
	}

	public void Tick( App app )
	{
		// header data

		HeaderDataViewer.InvalidateVisual();

		HeaderDataViewer_ScrollBar.Maximum = HeaderDataViewer.NumTotalLines - HeaderDataViewer.NumVisibleLines;
		HeaderDataViewer_ScrollBar.ViewportSize = HeaderDataViewer.NumVisibleLines;

		if ( HeaderDataViewer.NumVisibleLines >= HeaderDataViewer.NumTotalLines )
		{
			HeaderDataViewer.ScrollIndex = 0;
			HeaderDataViewer_ScrollBar.Visibility = Visibility.Collapsed;
		}
		else
		{
			HeaderDataViewer_ScrollBar.Visibility = Visibility.Visible;
		}

		// session information

		SessionInfoViewer.InvalidateVisual();

		SessionInfoViewer_ScrollBar.Maximum = SessionInfoViewer.NumTotalLines - SessionInfoViewer.NumVisibleLines;
		SessionInfoViewer_ScrollBar.ViewportSize = SessionInfoViewer.NumVisibleLines;

		if ( SessionInfoViewer.NumVisibleLines >= SessionInfoViewer.NumTotalLines )
		{
			SessionInfoViewer.ScrollIndex = 0;
			SessionInfoViewer_ScrollBar.Visibility = Visibility.Collapsed;
		}
		else
		{
			SessionInfoViewer_ScrollBar.Visibility = Visibility.Visible;
		}

		// telemetry data

		TelemetryDataViewer.InvalidateVisual();

		TelemetryDataViewer_ScrollBar.Maximum = TelemetryDataViewer.NumTotalLines - TelemetryDataViewer.NumVisibleLines;
		TelemetryDataViewer_ScrollBar.ViewportSize = TelemetryDataViewer.NumVisibleLines;

		if ( TelemetryDataViewer.NumVisibleLines >= TelemetryDataViewer.NumTotalLines )
		{
			TelemetryDataViewer.ScrollIndex = 0;
			TelemetryDataViewer_ScrollBar.Visibility = Visibility.Collapsed;
		}
		else
		{
			TelemetryDataViewer_ScrollBar.Visibility = Visibility.Visible;
		}
	}
}
