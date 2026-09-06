
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Windows.Win32;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.DataContext;
using MarvinsAIRARefactored.Windows;

using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaKnob : UserControl
{
	private const int ResetHoldMilliseconds = 1000;

	private static SolidColorBrush _curveBackgroundBrush = new( Color.FromArgb( 255, 49, 49, 49 ) );
	private static SolidColorBrush _curveGridLinesBrush = new( Color.FromArgb( 255, 68, 68, 68 ) );
	private static readonly SolidColorBrush _curveForegroundBrush = new( Color.FromArgb( 255, 255, 91, 46 ) );

	private static Pen _curveGridLinesPen = new( _curveGridLinesBrush, 1.5 );
	private static Pen _curveForegroundPen = new( _curveForegroundBrush, 6 );

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MairaKnob, object?> _instances = new();

	public static void UpdateThemeColors( bool lightTheme )
	{
		_curveBackgroundBrush = lightTheme
			? new SolidColorBrush( Color.FromArgb( 255, 230, 230, 230 ) )
			: new SolidColorBrush( Color.FromArgb( 255, 49, 49, 49 ) );

		_curveGridLinesBrush = lightTheme
			? new SolidColorBrush( Color.FromArgb( 255, 190, 190, 190 ) )
			: new SolidColorBrush( Color.FromArgb( 255, 68, 68, 68 ) );

		_curveGridLinesPen = new Pen( _curveGridLinesBrush, 1.5 );

		// shared across every knob's drawing — must stay frozen so WPF never tracks inheritance contexts on them
		_curveBackgroundBrush.Freeze();
		_curveGridLinesBrush.Freeze();
		_curveGridLinesPen.Freeze();

		foreach ( var instance in _instances )
		{
			instance.Key.UpdateKnobVisual();
		}
	}

	private System.Drawing.Point _draggingCenter;

	private readonly DispatcherTimer _resetDispatcherTimer = new() { Interval = TimeSpan.FromMilliseconds( 20 ) };
	private DateTime _resetStartTime;
	private bool _isResetting;
	private bool _isEditingValue;
	private bool _isEditingPercent;
	private bool _isEditingScaled;
	private float _valueBeforeEdit;

	static MairaKnob()
	{
		_curveBackgroundBrush.Freeze();
		_curveGridLinesBrush.Freeze();
		_curveGridLinesPen.Freeze();
		_curveForegroundBrush.Freeze();
		_curveForegroundPen.Freeze();
	}

	public MairaKnob()
	{
		InitializeComponent();

		_resetDispatcherTimer.Tick += ResetDispatcherTimer_Tick;

		_instances.Add( this, null );
	}

	#region User Control Events

	private void Middle_MairaButton_PreviewMouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		if ( e.LeftButton == MouseButtonState.Pressed )
		{
			IsDragging = true;

			Middle_MairaButton.IsPressed = true;

			PInvoke.GetCursorPos( out _draggingCenter );

			_ = PInvoke.ShowCursor( false );

			Mouse.Capture( (MairaButton) sender );
		}
	}

	private void Middle_MairaButton_PreviewMouseLeftButtonUp( object sender, MouseButtonEventArgs e )
	{
		if ( IsDragging && ( e.ChangedButton == MouseButton.Left ) )
		{
			EndDrag();
		}
	}

	private void Middle_MairaButton_PreviewMouseMove( object sender, MouseEventArgs e )
	{
		if ( IsDragging )
		{
			PInvoke.GetCursorPos( out System.Drawing.Point current );

			var delta = ( current.X - _draggingCenter.X ) + ( current.Y - _draggingCenter.Y );

			if ( delta != 0 )
			{
				var amount = (float) delta;

				if ( Reverse )
				{
					amount = -amount;
				}

				AdjustValue( amount, DragStepSize );

				PInvoke.SetCursorPos( _draggingCenter.X, _draggingCenter.Y );
			}
		}
	}

	private void Middle_MairaButton_LostMouseCapture( object sender, MouseEventArgs e )
	{
		if ( IsDragging )
		{
			EndDrag();
		}
	}

	private void Plus_MairaMappableButton_Click( object sender, RoutedEventArgs e ) => AdjustValue( Reverse ? -1f : 1f, EffectiveClickStepSize );
	private void Minus_MairaMappableButton_Click( object sender, RoutedEventArgs e ) => AdjustValue( Reverse ? 1f : -1f, EffectiveClickStepSize );

	private void Value_TextBlock_PreviewMouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		if ( Keyboard.Modifiers != ModifierKeys.None )
		{
			return;
		}

		e.Handled = true;

		BeginValueEdit();
	}

	private void Label_TextBlock_PreviewMouseRightButtonDown( object sender, MouseButtonEventArgs e )
	{
		var app = App.Instance!;

		e.Handled = true;

		if ( ContextSwitches != null )
		{
			app.Logger.WriteLine( "[MairaKnob] Showing update context switches window" );

			var updateContextSwitchesWindow = new UpdateContextSwitchesWindow( ContextSwitches )
			{
				Owner = app.MainWindow
			};

			updateContextSwitchesWindow.ShowDialog();
		}
	}

	private void Value_TextBlock_PreviewMouseRightButtonDown( object sender, MouseButtonEventArgs e )
	{
		if ( ( Keyboard.Modifiers != ModifierKeys.None ) || ( DefaultValue == null ) )
		{
			return;
		}

		e.Handled = true;

		_resetStartTime = DateTime.Now;
		_isResetting = true;

		_resetDispatcherTimer.Start();

		Mouse.OverrideCursor = Cursors.None;

		CursorCountdownOverlay.Start();
	}

	private void Value_TextBlock_PreviewMouseRightButtonUp( object sender, MouseButtonEventArgs e ) => CancelReset();
	private void Value_TextBlock_MouseLeave( object sender, MouseEventArgs e ) => CancelReset();

	#endregion

	#region Dependency Properties

	public static readonly DependencyProperty IsDraggingProperty = DependencyProperty.Register( nameof( IsDragging ), typeof( bool ), typeof( MairaKnob ), new PropertyMetadata( false ) );

	public bool IsDragging
	{
		get => (bool) GetValue( IsDraggingProperty );
		set => SetValue( IsDraggingProperty, value );
	}

	public static readonly DependencyProperty LabelProperty = DependencyProperty.Register( nameof( Label ), typeof( string ), typeof( MairaKnob ), new PropertyMetadata( string.Empty ) );

	public string Label
	{
		get => (string) GetValue( LabelProperty );
		set => SetValue( LabelProperty, value );
	}

	public static readonly DependencyProperty ValueProperty = DependencyProperty.Register( nameof( Value ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( 0f, OnValueChanged ) );

	public float Value
	{
		get => (float) GetValue( ValueProperty );
		set => SetValue( ValueProperty, value );
	}

	public static readonly DependencyProperty ValueStringProperty = DependencyProperty.Register( nameof( ValueString ), typeof( string ), typeof( MairaKnob ), new PropertyMetadata( "0" ) );

	public string ValueString
	{
		get => (string) GetValue( ValueStringProperty );
		set => SetValue( ValueStringProperty, value );
	}

	// The increment applied per +/- button press (mouse click or mapped input). For knobs wired to a
	// MappableActionCatalog action this is only a fallback - the universal Settings.KnobStepSizes value
	// is used instead (see EffectiveClickStepSize), so it matters only for knobs with no mapped action.
	// Value range metadata (the knob does NOT clamp with these — the bound view-model does). Used only to infer
	// percent-vs-absolute editing when the display carries no number (e.g. a localized "OFF"). NaN when unbound.
	public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register( nameof( Minimum ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( float.NaN ) );

	public float Minimum
	{
		get => (float) GetValue( MinimumProperty );
		set => SetValue( MinimumProperty, value );
	}

	public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register( nameof( Maximum ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( float.NaN ) );

	public float Maximum
	{
		get => (float) GetValue( MaximumProperty );
		set => SetValue( MaximumProperty, value );
	}

	public static readonly DependencyProperty ClickStepSizeProperty = DependencyProperty.Register( nameof( ClickStepSize ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( 0.01f ) );

	public float ClickStepSize
	{
		get => (float) GetValue( ClickStepSizeProperty );
		set => SetValue( ClickStepSizeProperty, value );
	}

	// The increment applied while dragging the knob. Always taken straight from XAML (never from the
	// catalog), so a knob can drag at a much finer resolution than its +/- buttons step.
	public static readonly DependencyProperty DragStepSizeProperty = DependencyProperty.Register( nameof( DragStepSize ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( 0.01f ) );

	public float DragStepSize
	{
		get => (float) GetValue( DragStepSizeProperty );
		set => SetValue( DragStepSizeProperty, value );
	}

	// Displayed-units per stored-unit for knobs whose direct-edit unit differs from what they store (e.g. the
	// prediction horizons store frames / 360 Hz sub-ticks but the value shows milliseconds). NaN (the default)
	// keeps editing in the stored unit; when set, BeginValueEdit shows the number from the display string and
	// CommitValueEdit converts the typed value back (÷ scale) and snaps it to ClickStepSize.
	public static readonly DependencyProperty EditUnitScaleProperty = DependencyProperty.Register( nameof( EditUnitScale ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( float.NaN ) );

	public float EditUnitScale
	{
		get => (float) GetValue( EditUnitScaleProperty );
		set => SetValue( EditUnitScaleProperty, value );
	}

	public static readonly DependencyProperty ValueChangedCallbackProperty = DependencyProperty.Register( nameof( ValueChangedCallback ), typeof( Action<float> ), typeof( MairaKnob ) );

	public Action<float> ValueChangedCallback
	{
		get => (Action<float>) GetValue( ValueChangedCallbackProperty );
		set => SetValue( ValueChangedCallbackProperty, value );
	}

	public static readonly DependencyProperty ContextSwitchesProperty = DependencyProperty.Register( nameof( ContextSwitches ), typeof( ContextSwitches ), typeof( MairaKnob ), new PropertyMetadata( null ) );

	public ContextSwitches ContextSwitches
	{
		get => (ContextSwitches) GetValue( ContextSwitchesProperty );
		set => SetValue( ContextSwitchesProperty, value );
	}

	public static readonly DependencyProperty PlusButtonMappingsProperty = DependencyProperty.Register( nameof( PlusButtonMappings ), typeof( ButtonMappings ), typeof( MairaKnob ) );

	public ButtonMappings PlusButtonMappings
	{
		get => (ButtonMappings) GetValue( PlusButtonMappingsProperty );
		set => SetValue( PlusButtonMappingsProperty, value );
	}

	public static readonly DependencyProperty MinusButtonMappingsProperty = DependencyProperty.Register( nameof( MinusButtonMappings ), typeof( ButtonMappings ), typeof( MairaKnob ) );

	public ButtonMappings MinusButtonMappings
	{
		get => (ButtonMappings) GetValue( MinusButtonMappingsProperty );
		set => SetValue( MinusButtonMappingsProperty, value );
	}

	public static readonly DependencyProperty ShowCurveProperty = DependencyProperty.Register( nameof( ShowCurve ), typeof( bool ), typeof( MairaKnob ), new PropertyMetadata( false, OnShowCurveChanged ) );

	public bool ShowCurve
	{
		get => (bool) GetValue( ShowCurveProperty );
		set => SetValue( ShowCurveProperty, value );
	}

	public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register( nameof( DefaultValue ), typeof( float? ), typeof( MairaKnob ), new PropertyMetadata( null ) );

	public float? DefaultValue
	{
		get => (float?) GetValue( DefaultValueProperty );
		set => SetValue( DefaultValueProperty, value );
	}

	public static readonly DependencyProperty ReverseProperty = DependencyProperty.Register( nameof( Reverse ), typeof( bool ), typeof( MairaKnob ), new PropertyMetadata( false ) );

	public bool Reverse
	{
		get => (bool) GetValue( ReverseProperty );
		set => SetValue( ReverseProperty, value );
	}

	#endregion

	#region Dependency Property Changed Events

	private static void OnValueChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaKnob mairaKnob )
		{
			mairaKnob.UpdateKnobVisual();
		}
	}

	private static void OnShowCurveChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaKnob mairaKnob )
		{
			mairaKnob.UpdateKnobVisual();
		}
	}

	#endregion

	#region Logic

	// amount is the signed number of steps to apply (drag passes the pixel delta, the +/- buttons pass +/-1);
	// stepSize is the per-step magnitude. Clicks pass EffectiveClickStepSize, drags pass DragStepSize.
	private void AdjustValue( float amount, float stepSize )
	{
		float oldValue = Value;
		float newValue = oldValue + amount * stepSize;

		Value = newValue;

		ValueChangedCallback?.Invoke( newValue );
	}

	// The step size to apply per +/- press (mouse click or mapped input). For knobs wired to a mappable
	// action (which is every knob on the racing wheel / steering effects / pedals / etc. pages) this is the
	// universal, profile-independent value from Settings.KnobStepSizes - the same value used by mapped input -
	// so the on-screen buttons and the mapped button always move the knob by the same amount. Knobs with no
	// mapped action fall back to the XAML ClickStepSize. Dragging never uses this - it uses DragStepSize.
	private float EffectiveClickStepSize
	{
		get
		{
			var stepSizeKey = ResolveStepSizeKey();

			if ( ( stepSizeKey != null ) && MappableActionCatalog.TryGetDefaultStepSize( stepSizeKey, out var defaultStepSize ) )
			{
				return MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.GetKnobStepSize( stepSizeKey, defaultStepSize );
			}

			return ClickStepSize;
		}
	}

	// Recover the knob's settings property base name (e.g. "RacingWheelStrength") from its PlusButtonMappings
	// binding path (e.g. "Settings.RacingWheelStrengthPlusButtonMappings"), which by convention is the
	// MappableActionCatalog key for this knob. Resolved once and cached; null when the knob has no such binding.
	private bool _stepSizeKeyResolved;
	private string? _stepSizeKey;

	private string? ResolveStepSizeKey()
	{
		if ( _stepSizeKeyResolved )
		{
			return _stepSizeKey;
		}

		_stepSizeKeyResolved = true;

		if ( BindingOperations.GetBinding( this, PlusButtonMappingsProperty ) is System.Windows.Data.Binding binding )
		{
			var path = binding.Path?.Path ?? string.Empty;

			var leaf = path.Contains( '.' ) ? path[ ( path.LastIndexOf( '.' ) + 1 ).. ] : path;

			const string suffix = "PlusButtonMappings";

			if ( leaf.EndsWith( suffix, StringComparison.Ordinal ) && ( leaf.Length > suffix.Length ) )
			{
				_stepSizeKey = leaf[ ..^suffix.Length ];
			}
		}

		return _stepSizeKey;
	}

	private void EndDrag()
	{
		IsDragging = false;

		Middle_MairaButton.IsPressed = false;

		_ = PInvoke.ShowCursor( true );

		Mouse.Capture( null );
	}

	private void UpdateKnobVisual()
	{
		if ( ShowCurve )
		{
			var renderTargetWidth = (int) Curve_Image.Width * 2;
			var renderTargetHeight = (int) Curve_Image.Height * 2;

			var power = MathZ.CurveToPower( Value );

			var dv = new DrawingVisual();

			using ( var dc = dv.RenderOpen() )
			{
				dc.DrawRectangle( _curveBackgroundBrush, null, new Rect( 0, 0, renderTargetWidth, renderTargetHeight ) );
				/*
				for ( var x = renderTargetWidth / 4; x < renderTargetWidth; x += renderTargetWidth / 4 )
				{
					dc.DrawLine( _curveGridLinesPen, new Point( x, 0 ), new Point( x, renderTargetHeight ) );
				}

				for ( var y = renderTargetWidth / 4; y < renderTargetHeight; y += renderTargetHeight / 4 )
				{
					dc.DrawLine( _curveGridLinesPen, new Point( 0, y ), new Point( renderTargetWidth, y ) );
				}
				*/
				var geometry = new StreamGeometry();

				using ( var ctx = geometry.Open() )
				{
					for ( var x = 3; x < renderTargetWidth - 3; x++ )
					{
						float xf = ( x - 3 ) / (float) ( renderTargetWidth - 6 );
						float yf = MathF.Pow( xf, power );

						int y = renderTargetHeight - 4 - (int) ( yf * ( renderTargetHeight - 7 ) );

						if ( x == 3 )
						{
							ctx.BeginFigure( new Point( x, y ), false, false );
						}
						else
						{
							ctx.LineTo( new Point( x, y ), true, false );
						}
					}
				}

				dc.DrawGeometry( null, _curveForegroundPen, geometry );
			}

			var renderTargetBitmap = new RenderTargetBitmap( renderTargetWidth, renderTargetHeight, 96.0, 96.0, PixelFormats.Pbgra32 );

			renderTargetBitmap.Render( dv );

			// freeze so the Image never registers itself as the bitmap's inheritance context —
			// unfrozen sources crash with ArgumentException in Freezable.RemoveContextInformation on theme swaps
			renderTargetBitmap.Freeze();

			Curve_Image.Source = renderTargetBitmap;
			Curve_Grid.Visibility = Visibility.Visible;
		}
		else
		{
			Curve_Grid.Visibility = Visibility.Collapsed;
		}
	}

	private void ResetDispatcherTimer_Tick( object? sender, EventArgs e )
	{
		if ( !_isResetting )
		{
			return;
		}

		var elapsed = ( DateTime.Now - _resetStartTime ).TotalMilliseconds;
		var progress = 1 - Math.Min( 1, elapsed / ResetHoldMilliseconds );

		CursorCountdownOverlay.UpdateProgress( progress );

		if ( elapsed >= ResetHoldMilliseconds )
		{
			if ( DefaultValue != null )
			{
				Value = (float) DefaultValue;
			}

			CancelReset();
		}
	}

	private void CancelReset()
	{
		_isResetting = false;

		_resetDispatcherTimer.Stop();

		CursorCountdownOverlay.Stop();

		Mouse.OverrideCursor = null;
	}

	private void BeginValueEdit()
	{
		if ( _isEditingValue )
		{
			return;
		}

		_isEditingValue = true;
		_isEditingScaled = !float.IsNaN( EditUnitScale ) && ( EditUnitScale > 0f );
		_isEditingPercent = !_isEditingScaled && IsPercentValueString();
		_valueBeforeEdit = Value;

		string editText;

		if ( _isEditingScaled )
		{
			// edit in the DISPLAYED unit (e.g. ms): show exactly the number the user is looking at (the first
			// number in the display string), so clicking "33.3 ms" opens "33.3" — never the raw stored frame count
			editText = FirstDisplayNumber();
		}
		else
		{
			var editValue = _isEditingPercent ? ( Value * 100f ) : Value;

			editText = editValue.ToString( "0.###", CultureInfo.CurrentCulture );
		}

		Value_TextBox.Text = editText;

		Value_TextBlock.Visibility = Visibility.Collapsed;
		Value_TextBox.Visibility = Visibility.Visible;

		Value_TextBox.Focus();
		Value_TextBox.SelectAll();
	}

	private bool IsPercentValueString()
	{
		var percentSuffix = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "Percent" ];

		if ( string.IsNullOrWhiteSpace( percentSuffix ) )
		{
			return false;
		}

		var valueString = ValueString ?? string.Empty;

		// the edit round-trips the FIRST number in the display, so percent mode is decided by what follows that
		// first number — not by the end of the string, which composite displays like "50% (13.4 Hz)" would fail
		var match = Regex.Match( valueString, @"[-+]?\d+(?:[.,]\d+)?" );

		if ( match.Success )
		{
			return valueString[ ( match.Index + match.Length ).. ].StartsWith( percentSuffix, StringComparison.CurrentCulture );
		}

		// no number at all — a localized text display like "OFF"; fall back to the knob range (fraction-based
		// percent knobs live inside -1..1, every absolute-unit knob has a wider range). NaN (range not bound,
		// the legacy settings knobs) compares false, keeping their previous not-percent behavior for "OFF".
		return ( Maximum <= 1f ) && ( Minimum >= -1f );
	}

	private void Value_TextBox_KeyDown( object sender, KeyEventArgs e )
	{
		if ( e.Key == Key.Enter )
		{
			e.Handled = true;

			CommitValueEdit();

			return;
		}

		if ( e.Key == Key.Escape )
		{
			e.Handled = true;

			CancelValueEdit();

			return;
		}
	}

	private void Value_TextBox_LostKeyboardFocus( object sender, KeyboardFocusChangedEventArgs e )
	{
		if ( _isEditingValue )
		{
			CommitValueEdit();
		}
	}

	private void CommitValueEdit()
	{
		if ( !_isEditingValue )
		{
			return;
		}

		var text = Value_TextBox.Text?.Trim() ?? string.Empty;

		var parsed = TryParseFirstFloat( text, out var newValue );

		if ( parsed )
		{
			if ( _isEditingPercent )
			{
				newValue /= 100f;
			}
			else if ( _isEditingScaled )
			{
				// convert the typed display unit (e.g. ms) back to the stored unit, then snap to a whole step so
				// the horizon lands on an exact frame / sub-tick (this also cleans up any fractional drag value)
				newValue /= EditUnitScale;

				var step = ( ClickStepSize > 0f ) ? ClickStepSize : 1f;

				newValue = MathF.Round( newValue / step ) * step;
			}

			Value = newValue;

			ValueChangedCallback?.Invoke( newValue );
		}
		else
		{
			Value = _valueBeforeEdit;
		}

		EndValueEdit();
	}

	private void CancelValueEdit()
	{
		if ( !_isEditingValue )
		{
			return;
		}

		Value = _valueBeforeEdit;

		EndValueEdit();
	}

	private void EndValueEdit()
	{
		_isEditingValue = false;
		_isEditingPercent = false;
		_isEditingScaled = false;

		Value_TextBox.Visibility = Visibility.Collapsed;
		Value_TextBlock.Visibility = Visibility.Visible;

		Keyboard.ClearFocus();
	}

	// The first number in the current display string (e.g. "33.3" from "33.3 ms"), or "0" when the display carries no
	// number (a localized text value like "OFF"). Used by scaled editing so the edit box opens on exactly what
	// the user sees.
	private string FirstDisplayNumber()
	{
		var match = Regex.Match( ValueString ?? string.Empty, @"[-+]?\d+(?:[.,]\d+)?" );

		return match.Success ? match.Value : "0";
	}

	private static bool TryParseFirstFloat( string text, out float value )
	{
		var match = Regex.Match( text, @"[-+]?\d+(?:[.,]\d+)?" );

		if ( !match.Success )
		{
			value = 0f;

			return false;
		}

		var token = match.Value;

		if ( float.TryParse( token, NumberStyles.Float, CultureInfo.CurrentCulture, out value ) )
		{
			return true;
		}

		token = token.Replace( ',', '.' );

		return float.TryParse( token, NumberStyles.Float, CultureInfo.InvariantCulture, out value );
	}

	#endregion
}
