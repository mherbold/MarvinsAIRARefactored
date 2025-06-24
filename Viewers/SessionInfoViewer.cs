
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using FlowDirection = System.Windows.FlowDirection;
using Point = System.Windows.Point;

namespace MarvinsAIRARefactored.Viewers;

public class SessionInfoViewer : Control
{
	public int NumTotalLines { get; private set; } = 0;
	public int NumVisibleLines { get; private set; } = 0;
	public int ScrollIndex { private get; set; } = 0;

	private readonly CultureInfo cultureInfo = CultureInfo.GetCultureInfo( "en-us" );
	private readonly Typeface typeface = new( "Courier New" );

	private readonly SolidColorBrush _oddLineBrush = new( Color.FromArgb( 224, 32, 32, 32 ) );
	private readonly SolidColorBrush _evenLineBrush = new( Color.FromArgb( 224, 48, 48, 48 ) );

	private const double _firstColumnWidth = 320;
	private const double _indentWidth = 32;
	private const double _lineHeight = 24;
	private const double _fontSize = 15;

	static SessionInfoViewer()
	{
		DefaultStyleKeyProperty.OverrideMetadata( typeof( SessionInfoViewer ), new FrameworkPropertyMetadata( typeof( SessionInfoViewer ) ) );
	}
	protected override void OnRender( DrawingContext drawingContext )
	{
		var app = App.Instance!;

		var irsdk = app.Simulator.IRSDK;

		var sessionInfo = irsdk.Data.SessionInfo;

		if ( !irsdk.IsConnected ||  sessionInfo == null  )
		{
			return;
		}

		var point = new Point( 10, 0 );
		var lineIndex = 0;
		var stopDrawing = false;

		foreach ( var propertyInfo in sessionInfo.GetType().GetProperties() )
		{
			DrawSessionInfo( drawingContext, propertyInfo.Name, propertyInfo.GetValue( sessionInfo ), 0, ref point, ref lineIndex, ref stopDrawing );
		}

		NumTotalLines = lineIndex;
		NumVisibleLines = (int) Math.Floor( ActualHeight / _lineHeight );
	}

	private void DrawSessionInfo( DrawingContext drawingContext, string propertyName, object? valueAsObject, int indent, ref Point point, ref int lineIndex, ref bool stopDrawing )
	{
		var isSimpleValue =   valueAsObject is null  ||  valueAsObject is string  ||  valueAsObject is int  ||  valueAsObject is float  ||  valueAsObject is double  ;

		if (  lineIndex >= ScrollIndex  && !stopDrawing )
		{
			var brush = ( lineIndex & 1 ) == 1 ? _oddLineBrush : _evenLineBrush;

			drawingContext.DrawRectangle( brush, null, new Rect( 0, point.Y, ActualWidth, _lineHeight ) );

			point.X = 10 + indent * _indentWidth;

			var formattedText = new FormattedText( propertyName, cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, Brushes.White, 1.25 )
			{
				LineHeight = _lineHeight
			};

			drawingContext.DrawText( formattedText, point );

			if ( valueAsObject is null )
			{
				point.X = _firstColumnWidth + indent * _indentWidth;

				formattedText = new FormattedText( "(null)", cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, Brushes.White, 1.25 )
				{
					LineHeight = _lineHeight
				};

				drawingContext.DrawText( formattedText, point );
			}
			else if ( isSimpleValue )
			{
				point.X = _firstColumnWidth + indent * _indentWidth;

				formattedText = new FormattedText( valueAsObject.ToString(), cultureInfo, FlowDirection.LeftToRight, typeface, _fontSize, Brushes.White, 1.25 )
				{
					LineHeight = _lineHeight
				};

				drawingContext.DrawText( formattedText, point );
			}

			point.Y += _lineHeight;

			if ( point.Y >= ActualHeight )
			{
				stopDrawing = true;
			}
		}

		lineIndex++;

		if ( !isSimpleValue )
		{
			if ( valueAsObject is IList list )
			{
				var index = 0;

				foreach ( var item in list )
				{
					DrawSessionInfo( drawingContext, index.ToString(), item, indent + 1, ref point, ref lineIndex, ref stopDrawing );

					index++;
				}
			}
			else
			{
#pragma warning disable CS8602
				foreach ( var propertyInfo in valueAsObject.GetType().GetProperties() )
				{
					DrawSessionInfo( drawingContext, propertyInfo.Name, propertyInfo.GetValue( valueAsObject ), indent + 1, ref point, ref lineIndex, ref stopDrawing );
				}
#pragma warning restore CS8602
			}
		}
	}
}
