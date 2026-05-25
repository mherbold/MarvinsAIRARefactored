
using System.Globalization;
using System.Windows;
using System.Windows.Media;

using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using FlowDirection = System.Windows.FlowDirection;
using Point = System.Windows.Point;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;

namespace MarvinsAIRARefactored.Viewers;

public class HeaderDataViewer : Control
{
	public int NumTotalLines { get; private set; } = 0;
	public int NumVisibleLines { get; private set; } = 0;
	public int ScrollIndex { private get; set; } = 0;

	private static readonly CultureInfo _cultureInfo = CultureInfo.GetCultureInfo( "en-us" );
	private static readonly Typeface typeface = new( "Consolas" );

	private static readonly SolidColorBrush _oddLineBrush = Brushes.Transparent;
	private static readonly SolidColorBrush _evenLineBrush = new( Color.FromArgb( 255, 34, 34, 34 ) );
	private static readonly SolidColorBrush _foregroundBrush = new( Color.FromArgb( 255, 220, 220, 220 ) );

	private const double _lineHeight = 30.0;
	private const double _fontSize = 20.0;
	private const double _yOffset = -2.0;

	private const double _firstColumnWidth = 210.0;

	private ScrollBar? _scrollBar = null;

	static HeaderDataViewer()
	{
		DefaultStyleKeyProperty.OverrideMetadata( typeof( HeaderDataViewer ), new FrameworkPropertyMetadata( typeof( HeaderDataViewer ) ) );

		_oddLineBrush.Freeze();
		_evenLineBrush.Freeze();
		_foregroundBrush.Freeze();
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

		var origin = new Point( 20, _yOffset );
		var lineIndex = 0;

		foreach ( var keyValuePair in dictionary )
		{
			if ( lineIndex >= ScrollIndex )
			{
				var brush = ( lineIndex & 1 ) == 1 ? _oddLineBrush : _evenLineBrush;

				drawingContext.DrawRectangle( brush, null, new Rect( 0, origin.Y - _yOffset, ActualWidth, _lineHeight ) );

				var formattedText = new FormattedText( keyValuePair.Key, _cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, _foregroundBrush, 1.25 )
				{
					LineHeight = _lineHeight
				};

				drawingContext.DrawText( formattedText, origin );

				var valueOrigin = new Point( origin.X + _firstColumnWidth, origin.Y );

				formattedText = new FormattedText( keyValuePair.Value.ToString( _cultureInfo ), _cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, _foregroundBrush, 1.25 )
				{
					LineHeight = _lineHeight
				};

				drawingContext.DrawText( formattedText, valueOrigin );

				origin.Y += _lineHeight;

				if ( origin.Y >= ActualHeight )
				{
					break;
				}
			}

			lineIndex++;
		}

		NumTotalLines = dictionary.Count;
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
}
