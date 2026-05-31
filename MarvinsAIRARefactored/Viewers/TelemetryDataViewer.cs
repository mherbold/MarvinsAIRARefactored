
using System.Globalization;
using System.Windows;
using System.Windows.Media;

using Brushes = System.Windows.Media.Brushes;
using Control = System.Windows.Controls.Control;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using Point = System.Windows.Point;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;

using IRSDKSharper;

using AppDataContext = MarvinsAIRARefactored.DataContext.DataContext;

namespace MarvinsAIRARefactored.Viewers;

public class TelemetryDataViewer : Control
{
	public int NumTotalLines { get; private set; } = 0;
	public int NumVisibleLines { get; private set; } = 0;
	public int ScrollIndex { private get; set; } = 0;

	private static readonly CultureInfo _cultureInfo = CultureInfo.GetCultureInfo( "en-us" );
	private static readonly Typeface typeface = new( "Consolas" );

	private static readonly SolidColorBrush _oddLineBrush = Brushes.Transparent;
	private static readonly SolidColorBrush _evenLineBrushDark = new( Color.FromArgb( 255, 34, 34, 34 ) );
	private static readonly SolidColorBrush _evenLineBrushLight = new( Color.FromArgb( 255, 210, 210, 210 ) );
	private static readonly SolidColorBrush _foregroundBrushDark = new( Color.FromArgb( 255, 220, 220, 220 ) );
	private static readonly SolidColorBrush _foregroundBrushLight = new( Color.FromArgb( 255, 20, 20, 20 ) );
	private static readonly SolidColorBrush _indexBrush = new( Color.FromArgb( 255, 128, 128, 128 ) );
	private static readonly SolidColorBrush _greenBrush = new( Color.FromArgb( 255, 46, 255, 145 ) );
	private static readonly SolidColorBrush _orangeBrush = new( Color.FromArgb( 255, 255, 91, 46 ) );

	private const double _lineHeight = 30.0;
	private const double _fontSize = 20.0;
	private const double _yOffset = -2.0;

	private const double _firstColumnWidth = 330.0;

	private ScrollBar? _scrollBar = null;

	static TelemetryDataViewer()
	{
		DefaultStyleKeyProperty.OverrideMetadata( typeof( TelemetryDataViewer ), new FrameworkPropertyMetadata( typeof( TelemetryDataViewer ) ) );

		_oddLineBrush.Freeze();
		_evenLineBrushDark.Freeze();
		_evenLineBrushLight.Freeze();
		_foregroundBrushDark.Freeze();
		_foregroundBrushLight.Freeze();
		_indexBrush.Freeze();
		_greenBrush.Freeze();
		_orangeBrush.Freeze();
	}

	public void Initialize( ScrollBar scrollBar )
	{
		_scrollBar = scrollBar;
	}

	protected override void OnRender( DrawingContext drawingContext )
	{
		var app = App.Instance;

		if ( app == null )
		{
			return;
		}

		var irsdk = app.Simulator.IRSDK;

		if ( !irsdk.IsConnected )
		{
			return;
		}

		var origin = new Point( 20, _yOffset );
		var lineIndex = 0;
		var stopDrawing = false;

		var isDark = !AppDataContext.Instance.Settings.AppLightThemeEnabled;

		var evenLineBrush = isDark ? _evenLineBrushDark : _evenLineBrushLight;
		var foregroundBrush = isDark ? _foregroundBrushDark : _foregroundBrushLight;

		foreach ( var keyValuePair in irsdk.Data.TelemetryDataProperties )
		{
			for ( var valueIndex = 0; valueIndex < keyValuePair.Value.Count; valueIndex++ )
			{
				if ( lineIndex >= ScrollIndex && !stopDrawing )
				{
					var brush = ( lineIndex & 1 ) == 1 ? _oddLineBrush : evenLineBrush;

					drawingContext.DrawRectangle( brush, null, new Rect( 0, origin.Y - _yOffset, ActualWidth, _lineHeight ) );

					var offset = keyValuePair.Value.Offset + valueIndex * IRacingSdkConst.VarTypeBytes[ (int) keyValuePair.Value.VarType ];

					var formattedText = new FormattedText( offset.ToString(), _cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, _indexBrush, 1.25 )
					{
						LineHeight = _lineHeight
					};

					drawingContext.DrawText( formattedText, origin );

					origin.X += 60;

					formattedText = new FormattedText( keyValuePair.Value.Name, _cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, foregroundBrush, 1.25 )
					{
						LineHeight = _lineHeight
					};

					drawingContext.DrawText( formattedText, origin );

					origin.X += _firstColumnWidth;

					if ( keyValuePair.Value.Count > 1 )
					{
						formattedText = new FormattedText( valueIndex.ToString(), _cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, _indexBrush, 1.25 )
						{
							LineHeight = _lineHeight
						};

						drawingContext.DrawText( formattedText, origin );
					}

					origin.X += 60;

					var valueAsString = string.Empty;
					var bitsAsString = string.Empty;

					brush = foregroundBrush;

					switch ( keyValuePair.Value.Unit )
					{
						case "irsdk_TrkLoc":
							valueAsString = GetString<IRacingSdkEnum.TrkLoc>( irsdk, keyValuePair.Value, valueIndex );
							break;

						case "irsdk_TrkSurf":
							valueAsString = GetString<IRacingSdkEnum.TrkSurf>( irsdk, keyValuePair.Value, valueIndex );
							break;

						case "irsdk_SessionState":
							valueAsString = GetString<IRacingSdkEnum.SessionState>( irsdk, keyValuePair.Value, valueIndex );
							break;

						case "irsdk_CarLeftRight":
							valueAsString = GetString<IRacingSdkEnum.CarLeftRight>( irsdk, keyValuePair.Value, valueIndex );
							break;

						case "irsdk_PitSvStatus":
							valueAsString = GetString<IRacingSdkEnum.PitSvStatus>( irsdk, keyValuePair.Value, valueIndex );
							break;

						case "irsdk_PaceMode":
							valueAsString = GetString<IRacingSdkEnum.PaceMode>( irsdk, keyValuePair.Value, valueIndex );
							break;

						default:

							switch ( keyValuePair.Value.VarType )
							{
								case IRacingSdkEnum.VarType.Char:
									valueAsString = $"         {irsdk.Data.GetChar( keyValuePair.Value, valueIndex )}";
									break;

								case IRacingSdkEnum.VarType.Bool:
									var valueAsBool = irsdk.Data.GetBool( keyValuePair.Value, valueIndex );
									valueAsString = valueAsBool ? "         T" : "         F";
									brush = valueAsBool ? _greenBrush : _orangeBrush;
									break;

								case IRacingSdkEnum.VarType.Int:
									valueAsString = $"{irsdk.Data.GetInt( keyValuePair.Value, valueIndex ),10:N0}";
									break;

								case IRacingSdkEnum.VarType.BitField:
									valueAsString = $"0x{irsdk.Data.GetBitField( keyValuePair.Value, valueIndex ):X8}";

									switch ( keyValuePair.Value.Unit )
									{
										case "irsdk_EngineWarnings":
											bitsAsString = GetString<IRacingSdkEnum.EngineWarnings>( irsdk, keyValuePair.Value, valueIndex );
											break;

										case "irsdk_Flags":
											bitsAsString = GetString<IRacingSdkEnum.Flags>( irsdk, keyValuePair.Value, valueIndex );
											break;

										case "irsdk_CameraState":
											bitsAsString = GetString<IRacingSdkEnum.CameraState>( irsdk, keyValuePair.Value, valueIndex );
											break;

										case "irsdk_PitSvFlags":
											bitsAsString = GetString<IRacingSdkEnum.PitSvFlags>( irsdk, keyValuePair.Value, valueIndex );
											break;

										case "irsdk_PaceFlags":
											bitsAsString = GetString<IRacingSdkEnum.PaceFlags>( irsdk, keyValuePair.Value, valueIndex );
											break;
									}

									break;

								case IRacingSdkEnum.VarType.Float:
									valueAsString = $"{irsdk.Data.GetFloat( keyValuePair.Value, valueIndex ),15:N4}";
									break;

								case IRacingSdkEnum.VarType.Double:
									valueAsString = $"{irsdk.Data.GetDouble( keyValuePair.Value, valueIndex ),15:N4}";
									break;
							}

							break;
					}

					formattedText = new FormattedText( valueAsString, _cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, brush, 1.25 )
					{
						LineHeight = _lineHeight
					};

					drawingContext.DrawText( formattedText, origin );

					origin.X += 210;

					formattedText = new FormattedText( keyValuePair.Value.Unit, _cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, foregroundBrush, 1.25 )
					{
						LineHeight = _lineHeight
					};

					drawingContext.DrawText( formattedText, origin );

					origin.X += 240;

					var desc = keyValuePair.Value.Desc;
					var originalDescLength = desc.Length;

					if ( bitsAsString != string.Empty )
					{
						desc += $" ({bitsAsString})";
					}

					formattedText = new FormattedText( desc, _cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, foregroundBrush, 1.25 )
					{
						LineHeight = _lineHeight
					};

					if ( bitsAsString != string.Empty )
					{
						formattedText.SetForegroundBrush( _orangeBrush, originalDescLength, desc.Length - originalDescLength );
					}

					drawingContext.DrawText( formattedText, origin );

					origin.X = 20;
					origin.Y += _lineHeight;

					if ( origin.Y >= ActualHeight )
					{
						stopDrawing = true;
					}
				}

				lineIndex++;
			}
		}

		NumTotalLines = lineIndex;
		NumVisibleLines = (int) Math.Floor( ActualHeight / _lineHeight );

		if ( _scrollBar != null )
		{
			_scrollBar.Maximum = NumTotalLines - NumVisibleLines;
			_scrollBar.ViewportSize = NumVisibleLines;

			if ( NumVisibleLines >= NumTotalLines )
			{
				ScrollIndex = 0;

				_scrollBar.Visibility = Visibility.Collapsed;
			}
			else
			{
				_scrollBar.Visibility = Visibility.Visible;
			}
		}
	}

	private static string GetString<T>( IRacingSdk irsdk, IRacingSdkDatum var, int index ) where T : Enum
	{
		if ( var.VarType == IRacingSdkEnum.VarType.Int )
		{
			var enumValue = (T) (object) irsdk.Data.GetInt( var, index );

			return enumValue.ToString();
		}
		else
		{
			var bits = irsdk.Data.GetBitField( var, index );

			var bitsString = string.Empty;

			foreach ( uint bitMask in Enum.GetValues( typeof( T ) ) )
			{
				if ( ( bits & bitMask ) != 0 )
				{
					if ( bitsString != string.Empty )
					{
						bitsString += " | ";
					}

					bitsString += Enum.GetName( typeof( T ), bitMask );
				}
			}

			return bitsString;
		}
	}
}
