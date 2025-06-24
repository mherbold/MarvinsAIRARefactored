
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace MarvinsAIRARefactored.Windows;

public partial class CursorCountdownOverlay : Window
{
	private static CursorCountdownOverlay? _instance = null;
	private static readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds( 20 ) };

	private double _progress = 1.0;

	public CursorCountdownOverlay()
	{
		InitializeComponent();

		_positionTimer.Tick += UpdateCursorPosition;
	}

	public static void Start()
	{
		_instance ??= new CursorCountdownOverlay();

		_instance._progress = 1.0;

		_instance.UpdateCursorPosition( null, EventArgs.Empty );
		_instance.UpdateArc();
		_instance.Show();

		_positionTimer.Start();
	}

	public static void Stop()
	{
		if ( _instance == null )
		{
			return;
		}

		_positionTimer.Stop();

		_instance.Close();

		_instance = null;
	}

	protected override void OnClosed( EventArgs e )
	{
		base.OnClosed( e );

		_positionTimer.Stop();
	}

	public static void UpdateProgress( double progress )
	{
		if ( _instance == null )
		{
			return;
		}

		_instance._progress = progress;

		_instance.UpdateArc();
	}

	private void UpdateArc()
	{
		var angle = 360.0 * _progress;
		var radius = Width / 2 - 8;

		var center = new Point( Width / 2, Height / 2 );

		if ( _progress <= 0 )
		{
			ArcPath.Data = null;
			return;
		}

		Point startPoint = new( center.X + radius * Math.Sin( 0 ), center.Y - radius * Math.Cos( 0 ) );
		Point endPoint = new( center.X + radius * Math.Sin( angle * Math.PI / 180 ), center.Y - radius * Math.Cos( angle * Math.PI / 180 ) );

		bool isLargeArc = angle > 180;

		var segment = new ArcSegment( endPoint, new Size( radius, radius ), 0, isLargeArc, SweepDirection.Clockwise, true );

		var figure = new PathFigure
		{
			StartPoint = startPoint,
			Segments = [ segment ],
			IsClosed = false
		};

		ArcPath.Data = new PathGeometry( [ figure ] );
	}

	private void UpdateCursorPosition( object? sender, EventArgs e )
	{
		System.Drawing.Point cursor = Control.MousePosition;

		Left = cursor.X - Width / 2;
		Top = cursor.Y - Height / 2;
	}
}
