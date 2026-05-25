
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

using Brushes = System.Windows.Media.Brushes;

using IRSDKSharper;
using PInvoke;

using static PInvoke.User32;

using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.Windows;

public partial class GapMonitorWindow : Window
{
	private const int UpdateInterval = 1;

	private int _updateCounter = UpdateInterval + 3;

	private bool _isDraggable = false;

	private int _prevAheadCarIdx = -1;
	private int _prevBehindCarIdx = -1;

	private int _prevAheadMarkerIndex = -1;
	private int _prevBehindMarkerIndex = -1;

	private double _prevAheadDelta = double.NaN;
	private double _prevBehindDelta = double.NaN;

	private const int DeltaHistorySize = 5;

	private readonly double[] _aheadDeltaHistory = new double[ DeltaHistorySize ];
	private int _aheadDeltaHistoryIndex = 0;
	private int _aheadDeltaHistoryCount = 0;

	private readonly double[] _behindDeltaHistory = new double[ DeltaHistorySize ];
	private int _behindDeltaHistoryIndex = 0;
	private int _behindDeltaHistoryCount = 0;

	public GapMonitorWindow()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GapMonitorWindow] Constructor >>>" );

		InitializeComponent();

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var rectangle = settings.OverlaysGapMonitorWindowPosition;

		Left = rectangle.Location.X;
		Top = rectangle.Location.Y;

		WindowStartupLocation = WindowStartupLocation.Manual;

		MakeDraggable();

		Show();

		app.Logger.WriteLine( "[GapMonitorWindow] <<< Constructor" );
	}

	private void Window_LocationChanged( object sender, EventArgs e )
	{
		if ( IsVisible && ( WindowState == WindowState.Normal ) )
		{
			var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

			var rectangle = settings.OverlaysGapMonitorWindowPosition;

			rectangle.Location = new System.Drawing.Point( (int) RestoreBounds.Left, (int) RestoreBounds.Top );

			settings.OverlaysGapMonitorWindowPosition = rectangle;
		}
	}

	public void ResetWindow()
	{
		Left = 0;
		Top = 0;
	}

	public void MakeDraggable()
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		_isDraggable = settings.OverlaysMakeGapMonitorDraggable;

		var hwnd = new WindowInteropHelper( this ).Handle;

		var exStyle = User32.GetWindowLong( hwnd, WindowLongIndexFlags.GWL_EXSTYLE );

		if ( _isDraggable )
		{
			_ = SetWindowLong( hwnd, WindowLongIndexFlags.GWL_EXSTYLE, (SetWindowLongFlags) ( (uint) exStyle & (uint) ~SetWindowLongFlags.WS_EX_TRANSPARENT ) );
		}
		else
		{
			_ = SetWindowLong( hwnd, WindowLongIndexFlags.GWL_EXSTYLE, (SetWindowLongFlags) ( (uint) exStyle | (uint) SetWindowLongFlags.WS_EX_TRANSPARENT ) );
		}
	}

	protected override void OnMouseLeftButtonDown( MouseButtonEventArgs e )
	{
		if ( _isDraggable )
		{
			DragMove();
		}
	}

	public void Tick( App app )
	{
		if ( Visibility == Visibility.Visible )
		{
			_updateCounter--;

			if ( _updateCounter <= 0 )
			{
				_updateCounter = UpdateInterval;

				// protect against invalid player car index
				if ( ( app.Simulator.PlayerCarIdx < 0 ) || ( app.Simulator.PlayerCarIdx >= IRacingSdkConst.MaxNumCars ) )
				{
					return;
				}

				// find the closest car ahead and behind based on relative lap distance percentage
				var aheadCarIdx = -1;
				var behindCarIdx = -1;

				var aheadLapDistPct = 0f;
				var behindLapDistPct = 0f;

				Drivers.DriverInfo? aheadDriver = null;
				Drivers.DriverInfo? behindDriver = null;

				for ( var carIdx = 0; carIdx < IRacingSdkConst.MaxNumCars; carIdx++ )
				{
					// exclude player's own car
					if ( carIdx == app.Simulator.PlayerCarIdx )
					{
						continue;
					}

					// get the driver for this car
					if ( !app.Drivers.TryGetDriverByCarIdx( carIdx, out var driver ) || ( driver is null ) )
					{
						continue;
					}

					// ignore the pace car and spectators and cars on pit road
					if ( driver.IsSpectator || driver.CarIsPaceCar || app.Simulator.CarIdxOnPitRoad[ carIdx ] )
					{
						continue;
					}

					// make lapDistPct relative to the player's lap distance percentage
					var lapDistPct = app.Simulator.CarIdxLapDistPct[ carIdx ] - app.Simulator.LapDistPct;

					if ( lapDistPct < -0.5f )
					{
						lapDistPct += 1f;
					}
					else if ( lapDistPct > 0.5f )
					{
						lapDistPct -= 1f;
					}

					if ( lapDistPct > 0f ) // this car is ahead
					{
						if ( ( aheadCarIdx == -1 ) || ( lapDistPct < aheadLapDistPct ) )
						{
							aheadCarIdx = carIdx;
							aheadLapDistPct = lapDistPct;
							aheadDriver = driver;
						}
					}
					else if ( lapDistPct < 0f ) // this car is behind
					{
						if ( ( behindCarIdx == -1 ) || ( lapDistPct > behindLapDistPct ) )
						{
							behindCarIdx = carIdx;
							behindLapDistPct = lapDistPct;
							behindDriver = driver;
						}
					}
				}

				// fill out car and driver info for car ahead
				if ( ( aheadCarIdx != -1 ) && ( aheadDriver is not null ) )
				{
					AheadCarNumberText.Text = $"#{aheadDriver.CarNumber}";
					AheadIRatingText.Text = aheadDriver.IRating > 0 ? aheadDriver.IRating.ToString() : string.Empty;
					AheadDriverText.Text = aheadDriver.UserName;

					if ( app.Simulator.CarIdxPosition[ aheadCarIdx ] > 0 )
					{
						AheadPositionText.Text = $"P{app.Simulator.CarIdxPosition[ aheadCarIdx ]}";
					}
					else
					{
						AheadPositionText.Text = string.Empty;
					}

					// color code the position text
					var playerLap = app.Simulator.Lap;
					var otherLap = app.Simulator.CarIdxLap[ aheadCarIdx ];

					if ( ( playerLap == 0 ) || ( otherLap == 0 ) )
					{
						AheadPositionText.Foreground = Brushes.White;
					}
					else
					{
						var playerCombined = playerLap + app.Simulator.LapDistPct;
						var otherCombined = otherLap + app.Simulator.CarIdxLapDistPct[ aheadCarIdx ];
						var diff = otherCombined - playerCombined;

						if ( ( diff >= -0.5f ) && ( diff <= 0.5f ) )
						{
							AheadPositionText.Foreground = Brushes.White;
						}
						else if ( diff > 0.5f )
						{
							AheadPositionText.Foreground = Brushes.Red;
						}
						else
						{
							AheadPositionText.Foreground = Brushes.Cyan;
						}
					}
				}
				else
				{
					AheadCarNumberText.Text = string.Empty;
					AheadPositionText.Text = string.Empty;
					AheadIRatingText.Text = string.Empty;
					AheadDriverText.Text = string.Empty;
				}

				// fill out car and driver info for car behind
				if ( ( behindCarIdx != -1 ) && ( behindDriver is not null ) )
				{
					BehindCarNumberText.Text = $"#{behindDriver.CarNumber}";
					BehindIRatingText.Text = behindDriver.IRating > 0 ? behindDriver.IRating.ToString() : string.Empty;
					BehindDriverText.Text = behindDriver.UserName;

					if ( app.Simulator.CarIdxPosition[ behindCarIdx ] > 0 )
					{
						BehindPositionText.Text = $"P{app.Simulator.CarIdxPosition[ behindCarIdx ]}";
					}
					else
					{
						BehindPositionText.Text = string.Empty;
					}

					// color code the position text
					var playerLap = app.Simulator.Lap;
					var otherLap = app.Simulator.CarIdxLap[ behindCarIdx ];

					if ( ( playerLap == 0 ) || ( otherLap == 0 ) )
					{
						BehindPositionText.Foreground = Brushes.White;
					}
					else
					{
						var playerCombined = playerLap + app.Simulator.LapDistPct;
						var otherCombined = otherLap + app.Simulator.CarIdxLapDistPct[ behindCarIdx ];
						var diff = otherCombined - playerCombined;

						if ( ( diff >= -0.5f ) && ( diff <= 0.5f ) )
						{
							BehindPositionText.Foreground = Brushes.White;
						}
						else if ( diff > 0.5f )
						{
							BehindPositionText.Foreground = Brushes.Red;
						}
						else
						{
							BehindPositionText.Foreground = Brushes.Cyan;
						}
					}
				}
				else
				{
					BehindCarNumberText.Text = string.Empty;
					BehindPositionText.Text = string.Empty;
					BehindIRatingText.Text = string.Empty;
					BehindDriverText.Text = string.Empty;
				}

				// reset some stuff if the car ahead/behind has changed since the last tick
				if ( aheadCarIdx != _prevAheadCarIdx )
				{
					_prevAheadMarkerIndex = -1;
					_prevAheadDelta = double.NaN;

					_aheadDeltaHistoryIndex = 0;
					_aheadDeltaHistoryCount = 0;

					Array.Clear( _aheadDeltaHistory );
				}

				if ( behindCarIdx != _prevBehindCarIdx )
				{
					_prevBehindMarkerIndex = -1;
					_prevBehindDelta = double.NaN;

					_behindDeltaHistoryIndex = 0;
					_behindDeltaHistoryCount = 0;

					Array.Clear( _behindDeltaHistory );
				}

				// get the closest ahead car's times at the player's most recently passed marker and calculate the time difference
				var aheadIsInvalid = true;

				if ( aheadCarIdx != -1 )
				{
					if ( app.TimingMarkers.TryGetMarkerTimeAtLapPct( aheadCarIdx, app.Simulator.LapDistPct, out var markerIndex, out var aheadTime ) )
					{
						if ( app.TimingMarkers.TryGetMarkerTimeAtLapPct( app.Simulator.PlayerCarIdx, app.Simulator.LapDistPct, out var _, out var playerTime ) )
						{
							var delta = playerTime - aheadTime;

							if ( delta > 0 )
							{
								aheadIsInvalid = false;

								AheadDeltaText.Text = $"{delta:F2}s";

								if ( markerIndex != _prevAheadMarkerIndex )
								{
									if ( !double.IsNaN( _prevAheadDelta ) )
									{
										var change = delta - _prevAheadDelta;

										_aheadDeltaHistory[ _aheadDeltaHistoryIndex ] = change;
										_aheadDeltaHistoryIndex = ( _aheadDeltaHistoryIndex + 1 ) % DeltaHistorySize;

										if ( _aheadDeltaHistoryCount < DeltaHistorySize )
										{
											_aheadDeltaHistoryCount++;
										}

										var totalChange = 0.0;

										for ( var k = 0; k < _aheadDeltaHistoryCount; k++ )
										{
											var idx = ( _aheadDeltaHistoryIndex - 1 - k + DeltaHistorySize ) % DeltaHistorySize;

											totalChange += _aheadDeltaHistory[ idx ];
										}

										AheadDeltaText.Foreground = ( totalChange >= -0.025 && totalChange <= 0.025 ) ? Brushes.White : ( totalChange <= 0 ) ? Brushes.LightGreen : Brushes.Salmon;
									}
									else
									{
										AheadDeltaText.Foreground = Brushes.White;
									}

									_prevAheadMarkerIndex = markerIndex;
									_prevAheadDelta = delta;
								}
							}
						}
					}
				}

				if ( aheadIsInvalid )
				{
					AheadDeltaText.Text = string.Empty;

					_prevAheadMarkerIndex = -1;
					_prevAheadDelta = double.NaN;

					_aheadDeltaHistoryIndex = 0;
					_aheadDeltaHistoryCount = 0;

					Array.Clear( _aheadDeltaHistory );
				}

				// get the closest behind car's times at that car's most recently passed marker and calculate the time difference
				var behindIsInvalid = true;

				if ( behindCarIdx != -1 )
				{
					if ( app.TimingMarkers.TryGetMarkerTimeAtLapPct( app.Simulator.PlayerCarIdx, app.Simulator.CarIdxLapDistPct[ behindCarIdx ], out var markerIndex, out var playerTime ) )
					{
						if ( app.TimingMarkers.TryGetMarkerTimeAtLapPct( behindCarIdx, app.Simulator.CarIdxLapDistPct[ behindCarIdx ], out var _, out var behindTime ) )
						{
							var delta = behindTime - playerTime;

							if ( delta > 0 )
							{
								behindIsInvalid = false;

								BehindDeltaText.Text = $"{delta:F2}s";

								if ( markerIndex != _prevBehindMarkerIndex )
								{
									if ( !double.IsNaN( _prevBehindDelta ) )
									{
										var change = delta - _prevBehindDelta;

										_behindDeltaHistory[ _behindDeltaHistoryIndex ] = change;
										_behindDeltaHistoryIndex = ( _behindDeltaHistoryIndex + 1 ) % DeltaHistorySize;

										if ( _behindDeltaHistoryCount < DeltaHistorySize )
										{
											_behindDeltaHistoryCount++;
										}

										var totalChange = 0.0;

										for ( var k = 0; k < _behindDeltaHistoryCount; k++ )
										{
											var idx = ( _behindDeltaHistoryIndex - 1 - k + DeltaHistorySize ) % DeltaHistorySize;

											totalChange += _behindDeltaHistory[ idx ];
										}

										BehindDeltaText.Foreground = ( totalChange >= -0.025 && totalChange <= 0.025 ) ? Brushes.White : ( totalChange >= 0 ) ? Brushes.LightGreen : Brushes.Salmon;
									}
									else
									{
										BehindDeltaText.Foreground = Brushes.White;
									}

									_prevBehindMarkerIndex = markerIndex;
									_prevBehindDelta = delta;
								}
							}
						}
					}

					if ( behindIsInvalid )
					{
						BehindDeltaText.Text = string.Empty;

						_prevBehindMarkerIndex = -1;
						_prevBehindDelta = double.NaN;

						_behindDeltaHistoryIndex = 0;
						_behindDeltaHistoryCount = 0;

						Array.Clear( _behindDeltaHistory );
					}
				}

				// remember the closest ahead and behind car indices for the next tick
				_prevAheadCarIdx = aheadCarIdx;
				_prevBehindCarIdx = behindCarIdx;
			}
		}
	}
}
