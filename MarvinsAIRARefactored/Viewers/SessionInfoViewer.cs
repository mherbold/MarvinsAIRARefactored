
using System.Collections;
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

public class SessionInfoViewer : Control
{
	public int NumTotalLines { get; private set; } = 0;
	public int NumVisibleLines { get; private set; } = 0;
	public int ScrollIndex { private get; set; } = 0;

	private static readonly CultureInfo cultureInfo = CultureInfo.GetCultureInfo( "en-us" );
	private static readonly Typeface typeface = new( "Consolas" );

	private static readonly SolidColorBrush _oddLineBrush = Brushes.Transparent;
	private static readonly SolidColorBrush _evenLineBrush = new( Color.FromArgb( 255, 34, 34, 34 ) );
	private static readonly SolidColorBrush _foregroundBrush = new( Color.FromArgb( 255, 220, 220, 220 ) );

	private const double _lineHeight = 30.0;
	private const double _fontSize = 20.0;
	private const double _yOffset = -2.0;

	private const double _firstColumnWidth = 390.0;
	private const double _indentWidth = 32.0;

	private ScrollBar? _scrollBar = null;

	static SessionInfoViewer()
	{
		DefaultStyleKeyProperty.OverrideMetadata( typeof( SessionInfoViewer ), new FrameworkPropertyMetadata( typeof( SessionInfoViewer ) ) );

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

		var sessionInfo = irsdk.Data.SessionInfo;

		if ( !irsdk.IsConnected || sessionInfo == null )
		{
			return;
		}

		var origin = new Point( 20, _yOffset );
		var lineIndex = 0;
		var stopDrawing = false;

		foreach ( var propertyInfo in sessionInfo.GetType().GetProperties() )
		{
			DrawSessionInfo( drawingContext, propertyInfo.Name, propertyInfo.GetValue( sessionInfo ), 0, ref origin, ref lineIndex, ref stopDrawing );
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

	private void DrawSessionInfo( DrawingContext drawingContext, string propertyName, object? valueAsObject, int indent, ref Point origin, ref int lineIndex, ref bool stopDrawing )
	{
		var isSimpleValue = valueAsObject is null || valueAsObject is string || valueAsObject is int || valueAsObject is float || valueAsObject is double;

		if ( valueAsObject is not null )
		{
			if ( lineIndex >= ScrollIndex && !stopDrawing )
			{
				var brush = ( lineIndex & 1 ) == 1 ? _oddLineBrush : _evenLineBrush;

				drawingContext.DrawRectangle( brush, null, new Rect( 0, origin.Y - _yOffset, ActualWidth, _lineHeight ) );

				origin.X = 20 + indent * _indentWidth;

				var formattedText = new FormattedText( propertyName, cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, _foregroundBrush, 1.25 )
				{
					LineHeight = _lineHeight
				};

				drawingContext.DrawText( formattedText, origin );

				if ( isSimpleValue )
				{
					origin.X = _firstColumnWidth + indent * _indentWidth;

					formattedText = new FormattedText( valueAsObject.ToString(), cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, _foregroundBrush, 1.25 )
					{
						LineHeight = _lineHeight
					};

					drawingContext.DrawText( formattedText, origin );
				}

				origin.Y += _lineHeight;

				if ( origin.Y >= ActualHeight )
				{
					stopDrawing = true;
				}
			}

			lineIndex++;
		}

		if ( !isSimpleValue )
		{
			if ( valueAsObject is IList list )
			{
				var index = 0;

				foreach ( var item in list )
				{
					DrawSessionInfo( drawingContext, index.ToString(), item, indent + 1, ref origin, ref lineIndex, ref stopDrawing );

					index++;
				}
			}
			else
			{
#pragma warning disable CS8602
				foreach ( var propertyInfo in valueAsObject.GetType().GetProperties() )
				{
					DrawSessionInfo( drawingContext, propertyInfo.Name, propertyInfo.GetValue( valueAsObject ), indent + 1, ref origin, ref lineIndex, ref stopDrawing );
				}
#pragma warning restore CS8602
			}
		}
	}
}
