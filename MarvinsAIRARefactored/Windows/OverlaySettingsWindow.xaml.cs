
using System.Globalization;
using System.Windows;

using MarvinsAIRARefactored.Controls;

using WinFormsCursor = System.Windows.Forms.Cursor;
using WinFormsScreen = System.Windows.Forms.Screen;

using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace MarvinsAIRARefactored.Windows;

// Right-click popup (shown while overlays are draggable) for customizing an overlay window's background color and opacity.
// Box overlays use the full color + opacity mode; the Grip-O-Meter uses opacity only (colorEnabled = false).
public partial class OverlaySettingsWindow : Window
{
	private readonly bool _colorEnabled;
	private readonly Func<string>? _getColor;
	private readonly Action<string>? _setColor;
	private readonly string _defaultColor;
	private readonly Func<float> _getOpacity;
	private readonly Action<float> _setOpacity;
	private readonly float _defaultOpacity;

	private bool _updating;

	public OverlaySettingsWindow( bool colorEnabled, Func<string>? getColor, Action<string>? setColor, string defaultColor, Func<float> getOpacity, Action<float> setOpacity, float defaultOpacity )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[OverlaySettingsWindow] Constructor >>>" );

		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		_colorEnabled = colorEnabled;
		_getColor = getColor;
		_setColor = setColor;
		_defaultColor = defaultColor;
		_getOpacity = getOpacity;
		_setOpacity = setOpacity;
		_defaultOpacity = defaultOpacity;

		_updating = true;

		if ( _colorEnabled )
		{
			var defaultColorParsed = ParseColor( _defaultColor );

			R_MairaKnob.DefaultValue = defaultColorParsed.R / 255f;
			G_MairaKnob.DefaultValue = defaultColorParsed.G / 255f;
			B_MairaKnob.DefaultValue = defaultColorParsed.B / 255f;

			R_MairaKnob.ValueChangedCallback = OnColorChannelChanged;
			G_MairaKnob.ValueChangedCallback = OnColorChannelChanged;
			B_MairaKnob.ValueChangedCallback = OnColorChannelChanged;

			var color = ParseColor( _getColor?.Invoke() ?? _defaultColor );

			SetChannelKnob( R_MairaKnob, color.R / 255f );
			SetChannelKnob( G_MairaKnob, color.G / 255f );
			SetChannelKnob( B_MairaKnob, color.B / 255f );

			UpdateHexText();
		}
		else
		{
			// opacity-only mode (Grip-O-Meter): hide the color controls but keep the opacity knob in its first-column slot
			Hex_MairaTextBox.Visibility = Visibility.Collapsed;
			R_MairaKnob.Visibility = Visibility.Collapsed;
			G_MairaKnob.Visibility = Visibility.Collapsed;
			B_MairaKnob.Visibility = Visibility.Collapsed;
		}

		Opacity_MairaKnob.DefaultValue = _defaultOpacity;
		Opacity_MairaKnob.ValueChangedCallback = OnOpacityChanged;

		SetOpacityKnob( _getOpacity() );

		_updating = false;

		app.Logger.WriteLine( "[OverlaySettingsWindow] <<< Constructor" );
	}

	protected override void OnContentRendered( EventArgs e )
	{
		base.OnContentRendered( e );

		// position the popup near the mouse cursor, nudged back on-screen if it would overflow the working area
		var cursorPosition = WinFormsCursor.Position;

		var workingArea = WinFormsScreen.FromPoint( cursorPosition ).WorkingArea;

		Left = Math.Clamp( cursorPosition.X, workingArea.Left, Math.Max( workingArea.Left, workingArea.Right - (int) ActualWidth ) );
		Top = Math.Clamp( cursorPosition.Y, workingArea.Top, Math.Max( workingArea.Top, workingArea.Bottom - (int) ActualHeight ) );
	}

	private void OnColorChannelChanged( float value )
	{
		if ( _updating )
		{
			return;
		}

		_updating = true;

		SetChannelKnob( R_MairaKnob, R_MairaKnob.Value );
		SetChannelKnob( G_MairaKnob, G_MairaKnob.Value );
		SetChannelKnob( B_MairaKnob, B_MairaKnob.Value );

		_setColor?.Invoke( ComposeHex() );

		UpdateHexText();

		_updating = false;
	}

	private void OnOpacityChanged( float value )
	{
		if ( _updating )
		{
			return;
		}

		_updating = true;

		SetOpacityKnob( Opacity_MairaKnob.Value );

		_setOpacity( Opacity_MairaKnob.Value );

		_updating = false;
	}

	private void Reset_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		_updating = true;

		if ( _colorEnabled )
		{
			var color = ParseColor( _defaultColor );

			SetChannelKnob( R_MairaKnob, color.R / 255f );
			SetChannelKnob( G_MairaKnob, color.G / 255f );
			SetChannelKnob( B_MairaKnob, color.B / 255f );

			_setColor?.Invoke( ComposeHex() );

			UpdateHexText();
		}

		SetOpacityKnob( _defaultOpacity );

		_setOpacity( _defaultOpacity );

		_updating = false;
	}

	private void Close_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Close();
	}

	private void SetChannelKnob( MairaKnob knob, float channelValue )
	{
		channelValue = Math.Clamp( channelValue, 0f, 1f );

		knob.Value = channelValue;
		knob.ValueString = ( (int) Math.Round( channelValue * 255f ) ).ToString( CultureInfo.CurrentCulture );
	}

	private void SetOpacityKnob( float opacityValue )
	{
		opacityValue = Math.Clamp( opacityValue, 0f, 1f );

		Opacity_MairaKnob.Value = opacityValue;
		Opacity_MairaKnob.ValueString = $"{opacityValue * 100f:F0}{MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "Percent" ]}";
	}

	private string ComposeHex()
	{
		var r = (int) Math.Round( Math.Clamp( R_MairaKnob.Value, 0f, 1f ) * 255f );
		var g = (int) Math.Round( Math.Clamp( G_MairaKnob.Value, 0f, 1f ) * 255f );
		var b = (int) Math.Round( Math.Clamp( B_MairaKnob.Value, 0f, 1f ) * 255f );

		return $"#{r:X2}{g:X2}{b:X2}";
	}

	private void Hex_MairaTextBox_ValueChanged( object sender, RoutedEventArgs e )
	{
		if ( _updating || !_colorEnabled )
		{
			return;
		}

		if ( !TryParseHexColor( Hex_MairaTextBox.Value, out var color ) )
		{
			return;
		}

		_updating = true;

		SetChannelKnob( R_MairaKnob, color.R / 255f );
		SetChannelKnob( G_MairaKnob, color.G / 255f );
		SetChannelKnob( B_MairaKnob, color.B / 255f );

		_setColor?.Invoke( ComposeHex() );

		_updating = false;
	}

	private void UpdateHexText()
	{
		Hex_MairaTextBox.Value = ComposeHex();
	}

	private static bool TryParseHexColor( string? text, out Color color )
	{
		color = Colors.Black;

		if ( string.IsNullOrWhiteSpace( text ) )
		{
			return false;
		}

		var hex = text.Trim();

		if ( !hex.StartsWith( '#' ) )
		{
			hex = "#" + hex;
		}

		// only accept a complete #RRGGBB value so partial typing does not snap the knobs around
		if ( hex.Length != 7 )
		{
			return false;
		}

		try
		{
			color = (Color) ColorConverter.ConvertFromString( hex );

			return true;
		}
		catch
		{
			return false;
		}
	}

	private static Color ParseColor( string hexColor )
	{
		try
		{
			return (Color) ColorConverter.ConvertFromString( hexColor );
		}
		catch
		{
			return Colors.Black;
		}
	}
}
