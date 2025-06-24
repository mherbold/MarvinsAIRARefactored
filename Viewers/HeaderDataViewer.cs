
using System.Globalization;
using System.Windows;
using System.Windows.Media;

using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using FlowDirection = System.Windows.FlowDirection;
using Point = System.Windows.Point;

namespace MarvinsAIRARefactored.Viewers;

public class HeaderDataViewer : Control
{
	public int NumTotalLines { get; private set; } = 0;
	public int NumVisibleLines { get; private set; } = 0;
	public int ScrollIndex { private get; set; } = 0;

	private readonly CultureInfo _cultureInfo = CultureInfo.GetCultureInfo( "en-us" );
	private readonly Typeface _typeface = new( "Courier New" );

	private readonly SolidColorBrush _oddLineBrush = new( Color.FromArgb( 224, 32, 32, 32 ) );
	private readonly SolidColorBrush _evenLineBrush = new( Color.FromArgb( 224, 48, 48, 48 ) );

	private const double _firstColumnWidth = 200;
	private const double _lineHeight = 24;
	private const double _fontSize = 15;

	static HeaderDataViewer()
	{
		DefaultStyleKeyProperty.OverrideMetadata( typeof( HeaderDataViewer ), new FrameworkPropertyMetadata( typeof( HeaderDataViewer ) ) );
	}

	protected override void OnRender( DrawingContext drawingContext )
	{
		var app = App.Instance!;

		var irsdk = app.Simulator.IRSDK;

		if ( !irsdk.IsConnected )
		{
			return;
		}

		var dictionary = new Dictionary<string, int>()
			{
				{ "Version", irsdk.Data.Version },
				{ "Status", irsdk.Data.Status },
				{ "TickRate", irsdk.Data.TickRate },
				{ "SessionInfoUpdate", irsdk.Data.SessionInfoUpdate },
				{ "SessionInfoLength", irsdk.Data.SessionInfoLength },
				{ "SessionInfoOffset", irsdk.Data.SessionInfoOffset },
				{ "VarCount", irsdk.Data.VarCount },
				{ "VarHeaderOffset", irsdk.Data.VarHeaderOffset },
				{ "BufferCount", irsdk.Data.BufferCount },
				{ "BufferLength", irsdk.Data.BufferLength },
				{ "TickCount", irsdk.Data.TickCount },
				{ "Offset", irsdk.Data.Offset },
				{ "FramesDropped", irsdk.Data.FramesDropped }
			};

		var point = new Point( 10, 0 );
		var lineIndex = 0;

		foreach ( var keyValuePair in dictionary )
		{
			if ( lineIndex >= ScrollIndex )
			{
				var brush = ( lineIndex & 1 ) == 1 ? _oddLineBrush : _evenLineBrush;

				drawingContext.DrawRectangle( brush, null, new Rect( 0, point.Y, ActualWidth, _lineHeight ) );

				var formattedText = new FormattedText( keyValuePair.Key, _cultureInfo, FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.White, 1.25 )
				{
					LineHeight = _lineHeight
				};

				drawingContext.DrawText( formattedText, point );

				point.X += _firstColumnWidth;

				formattedText = new FormattedText( keyValuePair.Value.ToString(), _cultureInfo, FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.White, 1.25 )
				{
					LineHeight = _lineHeight
				};

				drawingContext.DrawText( formattedText, point );

				point.X = 10;
				point.Y += _lineHeight;

				if ( point.Y >= ActualHeight )
				{
					break;
				}
			}

			lineIndex++;
		}

		NumTotalLines = dictionary.Count;
		NumVisibleLines = (int) Math.Floor( ActualHeight / _lineHeight );
	}
}
