
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;

using Brushes = System.Windows.Media.Brushes;

namespace MarvinsAIRARefactored.Windows;

public partial class DeltaMonitorWindow : Window
{
	private const int UpdateInterval = 1;

	// must match TimingMarkers.MaxNumMarkers - the marker index returned by TimingMarkers is clamped below this
	private const int MaxNumMarkers = 3000;

	private int _updateCounter = UpdateInterval + 3;

	private bool _isDraggable = false;

	private readonly OverlayWindowScaler _scaler;

	// Sample data shown while "make all overlay windows visible and draggable" is enabled, so the window can be positioned and scaled
	private const string SampleLapTime = "1:23.45";
	private const string SampleSelfDelta = "-0.12s";
	private const string SampleLeaderDelta = "+0.34s";

	// the session time at which the player most recently crossed the start/finish line (start of the current lap)
	private double _playerLapStart = 0;

	// the player's elapsed lap time at each marker for the lap currently being driven
	private readonly double[] _selfCurrentElapsed = new double[ MaxNumMarkers ];

	// the player's elapsed lap time at each marker for the last completed lap (the reference lap)
	private readonly double[] _selfReferenceElapsed = new double[ MaxNumMarkers ];

	private int _lastSessionNum = -1;

	public DeltaMonitorWindow()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[DeltaMonitorWindow] Constructor >>>" );

		InitializeComponent();

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		_scaler = new OverlayWindowScaler( this, () => settings.OverlaysDeltaMonitorWindowScale, value => settings.OverlaysDeltaMonitorWindowScale = value );

		var rectangle = settings.OverlaysDeltaMonitorWindowPosition;

		Left = rectangle.Location.X;
		Top = rectangle.Location.Y;

		WindowStartupLocation = WindowStartupLocation.Manual;

		MakeDraggable();

		Show();

		app.Logger.WriteLine( "[DeltaMonitorWindow] <<< Constructor" );
	}

	private void Window_LocationChanged( object sender, EventArgs e )
	{
		if ( IsVisible && ( WindowState == WindowState.Normal ) )
		{
			var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

			var rectangle = settings.OverlaysDeltaMonitorWindowPosition;

			rectangle.Location = new System.Drawing.Point( (int) RestoreBounds.Left, (int) RestoreBounds.Top );

			settings.OverlaysDeltaMonitorWindowPosition = rectangle;
		}
	}

	public void ResetWindow()
	{
		Left = 0;
		Top = 0;
	}

	public void MakeDraggable()
	{
		_isDraggable = App.Instance!.OverlaysDraggable;

		ScaleIcon.Visibility = _isDraggable ? Visibility.Visible : Visibility.Collapsed;

		if ( _isDraggable )
		{
			ShowSampleData();
		}

		var hwnd = new WindowInteropHelper( this ).Handle;

		var exStyle = PInvoke.GetWindowLong( (HWND) hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE );

		if ( _isDraggable )
		{
			exStyle &= ~(int) WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
		}
		else
		{
			exStyle |= (int) WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
		}

		_ = PInvoke.SetWindowLong( (HWND) hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle );
	}

	protected override void OnMouseLeftButtonDown( MouseButtonEventArgs e )
	{
		if ( _isDraggable )
		{
			DragMove();
		}
	}

	private void Window_PreviewMouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		if ( ReferenceEquals( e.OriginalSource, ScaleIcon ) )
		{
			_scaler.Start();

			e.Handled = true;
		}
	}

	private void Window_PreviewMouseLeftButtonUp( object sender, MouseButtonEventArgs e )
	{
		if ( _scaler.IsScaling )
		{
			_scaler.Stop();

			e.Handled = true;
		}
	}

	private void Window_MouseMove( object sender, System.Windows.Input.MouseEventArgs e )
	{
		_scaler.Update();
	}

	private void ShowSampleData()
	{
		LapTimeText.Text = SampleLapTime;
		LapTimeText.Foreground = Brushes.White;

		SelfDeltaText.Text = SampleSelfDelta;
		SelfDeltaText.Foreground = Brushes.LightGreen;

		LeaderDeltaText.Text = SampleLeaderDelta;
		LeaderDeltaText.Foreground = Brushes.Salmon;
	}

	private void ClearReadouts()
	{
		LapTimeText.Text = string.Empty;
		SelfDeltaText.Text = string.Empty;
		LeaderDeltaText.Text = string.Empty;
	}

	// "+1.23s" when slower, "-1.23s" when faster, "0.00s" when even
	private static string FormatDelta( double delta )
	{
		return $"{delta.ToString( "+0.00;-0.00;0.00" )}s";
	}

	private static string FormatLapTime( double seconds )
	{
		var minutes = (int) ( seconds / 60.0 );
		var remainingSeconds = seconds - ( minutes * 60.0 );

		return $"{minutes}:{remainingSeconds:00.00}";
	}

	private static System.Windows.Media.Brush DeltaBrush( double delta )
	{
		if ( Math.Abs( delta ) < 0.005 )
		{
			return Brushes.White;
		}

		return ( delta < 0 ) ? Brushes.LightGreen : Brushes.Salmon;
	}

	// the leader of the player's car class is the same-class car with the lowest (best) overall race position
	private static int FindClassLeaderCarIdx( App app, int playerCarIdx )
	{
		// determine the player's car class
		if ( !app.Drivers.TryGetDriverByCarIdx( playerCarIdx, out var playerDriver ) || ( playerDriver is null ) )
		{
			return -1;
		}

		var playerCarClassId = playerDriver.CarClassID;

		var positions = app.Simulator.CarIdxPosition;

		var leaderCarIdx = -1;
		var leaderPosition = int.MaxValue;

		for ( var carIdx = 0; carIdx < positions.Length; carIdx++ )
		{
			var position = positions[ carIdx ];

			// a valid overall race position is 1-based; 0 means the car has no position yet
			if ( position <= 0 )
			{
				continue;
			}

			if ( !app.Drivers.TryGetDriverByCarIdx( carIdx, out var driver ) || ( driver is null ) )
			{
				continue;
			}

			// only consider cars in the player's class, and ignore the pace car and spectators
			if ( ( driver.CarClassID != playerCarClassId ) || driver.CarIsPaceCar || driver.IsSpectator )
			{
				continue;
			}

			if ( position < leaderPosition )
			{
				leaderPosition = position;
				leaderCarIdx = carIdx;
			}
		}

		return leaderCarIdx;
	}

	public void Tick( App app )
	{
		// while sample data is being shown for overlay setup, leave the displayed values alone
		if ( app.OverlaysDraggable )
		{
			return;
		}

		if ( Visibility != Visibility.Visible )
		{
			return;
		}

		_updateCounter--;

		if ( _updateCounter > 0 )
		{
			return;
		}

		_updateCounter = UpdateInterval;

		var simulator = app.Simulator;

		// reset the reference lap data whenever the session changes
		if ( simulator.SessionNum != _lastSessionNum )
		{
			_lastSessionNum = simulator.SessionNum;

			_playerLapStart = 0;

			Array.Clear( _selfCurrentElapsed );
			Array.Clear( _selfReferenceElapsed );
		}

		// protect against invalid player car index
		var playerCarIdx = simulator.PlayerCarIdx;

		if ( ( playerCarIdx < 0 ) || ( playerCarIdx >= simulator.CarIdxLapDistPct.Length ) )
		{
			ClearReadouts();

			return;
		}

		var lapDistPct = simulator.LapDistPct;

		// the time the player most recently crossed the start/finish line is the start of the current lap
		if ( !app.TimingMarkers.TryGetLapStartTimes( playerCarIdx, out var playerLapStart, out var _ ) )
		{
			ClearReadouts();

			return;
		}

		// when the start/finish crossing time changes, a new lap has begun - snapshot the lap we just finished as the reference lap
		if ( ( _playerLapStart > 0 ) && ( playerLapStart != _playerLapStart ) )
		{
			Array.Copy( _selfCurrentElapsed, _selfReferenceElapsed, _selfCurrentElapsed.Length );

			Array.Clear( _selfCurrentElapsed );
		}

		_playerLapStart = playerLapStart;

		// (1) current running lap time
		var runningLapTime = simulator.SessionTime - playerLapStart;

		LapTimeText.Text = ( runningLapTime >= 0 ) ? FormatLapTime( runningLapTime ) : string.Empty;

		// the player's elapsed lap time at the most recently passed marker on the current lap
		if ( !app.TimingMarkers.TryGetMarkerTimeAtLapPct( playerCarIdx, lapDistPct, out var markerIndex, out var playerMarkerTime ) )
		{
			SelfDeltaText.Text = string.Empty;
			LeaderDeltaText.Text = string.Empty;

			return;
		}

		var playerElapsedAtMarker = playerMarkerTime - playerLapStart;

		// record this lap's elapsed time at this marker so it can serve as next lap's reference
		if ( ( markerIndex >= 0 ) && ( markerIndex < _selfCurrentElapsed.Length ) && ( playerElapsedAtMarker >= 0 ) )
		{
			_selfCurrentElapsed[ markerIndex ] = playerElapsedAtMarker;
		}

		// (2) delta against the player's own last completed lap at the same marker
		var referenceElapsed = ( ( markerIndex >= 0 ) && ( markerIndex < _selfReferenceElapsed.Length ) ) ? _selfReferenceElapsed[ markerIndex ] : 0;

		if ( ( referenceElapsed > 0 ) && ( playerElapsedAtMarker >= 0 ) )
		{
			var selfDelta = playerElapsedAtMarker - referenceElapsed;

			SelfDeltaText.Text = FormatDelta( selfDelta );
			SelfDeltaText.Foreground = DeltaBrush( selfDelta );
		}
		else
		{
			SelfDeltaText.Text = string.Empty;
		}

		// (3) delta against the current class leader at the same marker
		var leaderCarIdx = FindClassLeaderCarIdx( app, playerCarIdx );

		if ( leaderCarIdx == playerCarIdx )
		{
			// the player is the class leader, so there is nothing to compare against
			LeaderDeltaText.Text = FormatDelta( 0 );
			LeaderDeltaText.Foreground = DeltaBrush( 0 );
		}
		else if ( ( leaderCarIdx >= 0 ) &&
				  ( playerElapsedAtMarker >= 0 ) &&
				  app.TimingMarkers.TryGetLapStartTimes( leaderCarIdx, out var leaderLapStart, out var leaderPreviousLapStart ) &&
				  app.TimingMarkers.TryGetMarkerTimeAtLapPct( leaderCarIdx, lapDistPct, out var _, out var leaderMarkerTime ) )
		{
			var leaderElapsedAtMarker = -1.0;

			if ( leaderMarkerTime >= leaderLapStart )
			{
				// the leader has reached this marker on their current lap (live reference)
				leaderElapsedAtMarker = leaderMarkerTime - leaderLapStart;
			}
			else if ( ( leaderPreviousLapStart > 0 ) && ( leaderMarkerTime >= leaderPreviousLapStart ) )
			{
				// the leader hasn't reached this marker yet this lap (e.g. they are lapping us) - fall back to their last completed lap.
				// the previous lap start is tracked per car by TimingMarkers, so this works immediately even right after the leader changes.
				leaderElapsedAtMarker = leaderMarkerTime - leaderPreviousLapStart;
			}

			if ( leaderElapsedAtMarker >= 0 )
			{
				var leaderDelta = playerElapsedAtMarker - leaderElapsedAtMarker;

				LeaderDeltaText.Text = FormatDelta( leaderDelta );
				LeaderDeltaText.Foreground = DeltaBrush( leaderDelta );
			}
			else
			{
				LeaderDeltaText.Text = string.Empty;
			}
		}
		else
		{
			LeaderDeltaText.Text = string.Empty;
		}
	}
}
