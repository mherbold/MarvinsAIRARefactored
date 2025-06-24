
using System.Globalization;
using System.Windows;
using System.Windows.Media;

using Brushes = System.Windows.Media.Brushes;
using Control = System.Windows.Controls.Control;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using Point = System.Windows.Point;

using IRSDKSharper;

namespace MarvinsAIRARefactored.Viewers;

public class TelemetryDataViewer : Control
{
	public int NumTotalLines { get; private set; } = 0;
	public int NumVisibleLines { get; private set; } = 0;
	public int ScrollIndex { private get; set; } = 0;

	private readonly CultureInfo cultureInfo = CultureInfo.GetCultureInfo( "en-us" );
	private readonly Typeface typeface = new( "Courier New" );

	private readonly SolidColorBrush _oddLineBrush = new( Color.FromArgb( 224, 32, 32, 32 ) );
	private readonly SolidColorBrush _evenLineBrush = new( Color.FromArgb( 224, 48, 48, 48 ) );

	private const double _firstColumnWidth = 320;
	private const double _lineHeight = 24;
	private const double _fontSize = 15;

	static TelemetryDataViewer()
	{
		DefaultStyleKeyProperty.OverrideMetadata( typeof( TelemetryDataViewer ), new FrameworkPropertyMetadata( typeof( TelemetryDataViewer ) ) );
	}

	protected override void OnRender( DrawingContext drawingContext )
	{
		var app = App.Instance!;

		var irsdk = app.Simulator.IRSDK;

		if ( !irsdk.IsConnected )
		{
			return;
		}

		var point = new Point( 10, 0 );
		var lineIndex = 0;
		var stopDrawing = false;

		foreach ( var keyValuePair in irsdk.Data.TelemetryDataProperties )
		{
			for ( var valueIndex = 0; valueIndex < keyValuePair.Value.Count; valueIndex++ )
			{
				if (  lineIndex >= ScrollIndex  && !stopDrawing )
				{
					var brush = ( lineIndex & 1 ) == 1 ? _oddLineBrush : _evenLineBrush;

					drawingContext.DrawRectangle( brush, null, new Rect( 0, point.Y, ActualWidth, _lineHeight ) );

					var offset = keyValuePair.Value.Offset + valueIndex * IRacingSdkConst.VarTypeBytes[ (int) keyValuePair.Value.VarType ];

					var formattedText = new FormattedText( offset.ToString(), cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, Brushes.White, 1.25 )
					{
						LineHeight = _lineHeight
					};

					drawingContext.DrawText( formattedText, point );

					point.X += 60;

					formattedText = new FormattedText( keyValuePair.Value.Name, cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, Brushes.White, 1.25 )
					{
						LineHeight = _lineHeight
					};

					drawingContext.DrawText( formattedText, point );

					point.X += _firstColumnWidth;

					if ( keyValuePair.Value.Count > 1 )
					{
						formattedText = new FormattedText( valueIndex.ToString(), cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, Brushes.White, 1.25 )
						{
							LineHeight = _lineHeight
						};

						drawingContext.DrawText( formattedText, point );
					}

					point.X += 60;

					var valueAsString = string.Empty;
					var bitsAsString = string.Empty;
					
					brush = Brushes.White;

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
									brush = valueAsBool ? Brushes.LightGreen : Brushes.OrangeRed;
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

					formattedText = new FormattedText( valueAsString, cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, brush, 1.25 )
					{
						LineHeight = _lineHeight
					};

					drawingContext.DrawText( formattedText, point );

					point.X += 200;

					formattedText = new FormattedText( keyValuePair.Value.Unit, cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, Brushes.White, 1.25 )
					{
						LineHeight = _lineHeight
					};

					drawingContext.DrawText( formattedText, point );

					point.X += 200;

					var desc = keyValuePair.Value.Desc;
					var originalDescLength = desc.Length;

					if ( bitsAsString != string.Empty )
					{
						desc += $" ({bitsAsString})";
					}

					formattedText = new FormattedText( desc, cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, Brushes.White, 1.25 )
					{
						LineHeight = _lineHeight
					};

					if ( bitsAsString != string.Empty )
					{
						formattedText.SetForegroundBrush( Brushes.OrangeRed, originalDescLength, desc.Length - originalDescLength );
					}

					drawingContext.DrawText( formattedText, point );

					point.X = 10;
					point.Y += _lineHeight;

					if ( point.Y >= ActualHeight )
					{
						stopDrawing = true;
					}
				}

				lineIndex++;
			}
		}

		NumTotalLines = lineIndex;
		NumVisibleLines = (int) Math.Floor( ActualHeight / _lineHeight );
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
