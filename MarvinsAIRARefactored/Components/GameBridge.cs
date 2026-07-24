
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.GameBridges;

using static MarvinsAIRARefactored.Windows.MainWindow;

namespace MarvinsAIRARefactored.Components;

public partial class GameBridge
{
	private const int UpdateInterval = 60;

	public LeMansUltimateBridge LeMansUltimate { get; } = new();
	public AssettoCorsaBridge AssettoCorsa { get; } = new();
	public AssettoCorsaCompetizioneBridge AssettoCorsaCompetizione { get; } = new();
	public AssettoCorsaEvoBridge AssettoCorsaEvo { get; } = new();
	public AssettoCorsaRallyBridge AssettoCorsaRally { get; } = new();
	public RFactor2Bridge RFactor2 { get; } = new();
	public RaceRoomBridge RaceRoom { get; } = new();

	public IReadOnlyList<GameBridgeAdapter> Adapters { get; }

	public GameBridgeAdapter? ActiveAdapter { get; private set; } = null;

	public GameBridgeCapabilities ActiveCapabilities => ( ActiveAdapter == null ) ? GameBridgeCapabilities.All : ActiveAdapter.Capabilities;

	public bool IsBridgeActive { get => ActiveAdapter != null; }

	private int _updateCounter = UpdateInterval;
	private bool _transitioning = false;

	public GameBridge()
	{
		Adapters = [ LeMansUltimate, AssettoCorsa, AssettoCorsaCompetizione, AssettoCorsaEvo, AssettoCorsaRally, RFactor2, RaceRoom ];
	}

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GameBridge] Initialize >>>" );

		UpdatePage();

		app.Logger.WriteLine( "[GameBridge] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GameBridge] Shutdown >>>" );

		var adapter = ActiveAdapter;

		ActiveAdapter = null;

		adapter?.Stop();

		app.Logger.WriteLine( "[GameBridge] <<< Shutdown" );
	}

	public bool IsEnabledInSettings( GameBridgeAdapter adapter )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		if ( adapter == LeMansUltimate )
		{
			return settings.GameBridgeLeMansUltimateEnabled;
		}
		else if ( adapter == AssettoCorsa )
		{
			return settings.GameBridgeAssettoCorsaEnabled;
		}
		else if ( adapter == AssettoCorsaCompetizione )
		{
			return settings.GameBridgeAssettoCorsaCompetizioneEnabled;
		}
		else if ( adapter == AssettoCorsaEvo )
		{
			return settings.GameBridgeAssettoCorsaEvoEnabled;
		}
		else if ( adapter == AssettoCorsaRally )
		{
			return settings.GameBridgeAssettoCorsaRallyEnabled;
		}
		else if ( adapter == RFactor2 )
		{
			return settings.GameBridgeRFactor2Enabled;
		}
		else if ( adapter == RaceRoom )
		{
			return settings.GameBridgeRaceRoomRacingExperienceEnabled;
		}

		return false;
	}

	// called from the multimedia timer worker thread at ~500 Hz, immediately before the racing wheel update,
	// so bridge sub-samples are taken on the precise kernel timer and the freshest torque reading is in the
	// frame with near zero added latency. Exceptions must never propagate into the worker thread (it treats
	// them as fatal), so a failing bridge is logged and deactivated instead.
	public void Pump( double totalSeconds )
	{
		var adapter = ActiveAdapter;

		if ( adapter == null )
		{
			return;
		}

		try
		{
			adapter.Pump( totalSeconds );
		}
		catch ( Exception exception )
		{
			var app = App.Instance!;

			app.Logger.WriteLine( $"[GameBridge] Exception caught while pumping the {adapter.GameName} bridge: {exception.Message.Trim()}" );

			Deactivate( app );
		}
	}

	public void Tick( App app )
	{
		UpdateSteeringPassthrough( app );

		ActiveAdapter?.Tick( app );

		_updateCounter--;

		if ( _updateCounter > 0 )
		{
			return;
		}

		_updateCounter = UpdateInterval;

		// keep the vJoy driver warning and the sweep button's blink fresh while the game bridge page is
		// showing - the fault is only discovered after the passthrough first tries to initialize the
		// device, and the sweep can be cancelled by the toggles rather than by its own button
		if ( MairaAppMenuPopup.CurrentAppPage == AppPage.GameBridge )
		{
			_gameBridgePage.UpdateVJoyStatus( app );
			_gameBridgePage.UpdateSweepButton( app );
		}

		if ( _transitioning )
		{
			return;
		}

		if ( ActiveAdapter != null )
		{
			if ( !IsEnabledInSettings( ActiveAdapter ) || !ActiveAdapter.IsGameRunning )
			{
				Deactivate( app );
			}
		}
		else
		{
			foreach ( var adapter in Adapters )
			{
				if ( adapter.IsImplemented && IsEnabledInSettings( adapter ) && adapter.IsGameRunning )
				{
					Activate( app, adapter );

					break;
				}
			}
		}
	}

	// When enabled, the wheelbase's steering axis is passed through to the vJoy device's X axis every tick.
	// This is for games that provide no way to disable their built-in force feedback (Assetto Corsa, for
	// example): the user binds the game's steering input to the vJoy X axis instead of the real wheel, so the
	// game sends its force feedback to the vJoy device (which discards it) and MAIRA keeps sole control of
	// the real wheelbase. This tick runs BEFORE VirtualJoystick.Tick in the app worker loop, so the position
	// written here goes out to vJoy in the same frame.
	private bool _steeringPassthroughActive = false;
	private int _steeringPassthroughShutdownCountdown = 0;

	// the steering sweep continuously oscillates the vJoy axis (one full left-right-left cycle every two
	// seconds) - games like AC ignore controller input while their window is not in the foreground, so the
	// user arms the sweep in MAIRA, clicks over to the game, and the game then sees the axis moving
	public bool SteeringSweepActive { get; set; } = false;

	private const double SteeringSweepPeriodSeconds = 2.0;

	private double _steeringSweepPhase = 0.0;

	private void UpdateSteeringPassthrough( App app )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		if ( settings.GameBridgeSendSteeringToVJoy || settings.GameBridgeSteeringTestEnabled )
		{
			if ( !app.VirtualJoystick.Initialized && !app.VirtualJoystick.Faulted )
			{
				app.VirtualJoystick.Initialize();
			}

			// in steering test mode the vJoy axis is driven by the test buttons on the game bridge page
			// instead of the real wheel - this lets the user move ONLY the vJoy axis while binding the
			// steering input in the game, so the game cannot mistake the real wheel for the moving axis
			if ( !settings.GameBridgeSteeringTestEnabled )
			{
				SteeringSweepActive = false;

				app.VirtualJoystick.Steering = app.DirectInput.ForceFeedbackWheelPosition;
			}
			else if ( SteeringSweepActive )
			{
				_steeringSweepPhase += 2.0 * Math.PI / ( SteeringSweepPeriodSeconds * App.TimerTicksPerSecond );

				app.VirtualJoystick.Steering = (float) Math.Sin( _steeringSweepPhase );
			}
			else
			{
				_steeringSweepPhase = 0.0;
			}

			if ( !_steeringPassthroughActive && app.VirtualJoystick.Initialized )
			{
				app.Logger.WriteLine( "[GameBridge] vJoy steering passthrough started" );
			}

			_steeringPassthroughActive = true;
			_steeringPassthroughShutdownCountdown = 2;
		}
		else if ( _steeringPassthroughActive )
		{
			// on the way out, center the axis first and give VirtualJoystick.Tick one frame to actually push
			// the zero to the device before it is released
			app.VirtualJoystick.Steering = 0f;

			_steeringPassthroughShutdownCountdown--;

			if ( _steeringPassthroughShutdownCountdown <= 0 )
			{
				_steeringPassthroughActive = false;
				SteeringSweepActive = false;

				app.Logger.WriteLine( "[GameBridge] vJoy steering passthrough stopped" );

				if ( app.VirtualJoystick.Initialized )
				{
					app.VirtualJoystick.Shutdown();
				}
			}
		}
	}

	private void Activate( App app, GameBridgeAdapter adapter )
	{
		app.Logger.WriteLine( $"[GameBridge] Activating the {adapter.GameName} bridge" );

		_transitioning = true;

		Task.Run( () =>
		{
			try
			{
				adapter.Start();

				app.Simulator.SetDataSource( adapter.DataSource );

				ActiveAdapter = adapter;

				// the bridges are pumped from the multimedia timer worker thread, but that timer only runs
				// while the simulator is connected - and the simulator cannot connect until the bridge has
				// pumped its first frames, so the timer is resumed here to break the circle
				app.MultimediaTimer.Suspend = false;
			}
			catch ( Exception exception )
			{
				app.Logger.WriteLine( $"[GameBridge] Exception caught while activating the {adapter.GameName} bridge: {exception.Message.Trim()}" );

				adapter.Stop();

				if ( !app.Simulator.IsConnected )
				{
					app.MultimediaTimer.Suspend = true;
				}
			}
			finally
			{
				_transitioning = false;

				UpdatePage();
			}
		} );
	}

	private void Deactivate( App app )
	{
		var adapter = ActiveAdapter;

		if ( adapter == null )
		{
			return;
		}

		app.Logger.WriteLine( $"[GameBridge] Deactivating the {adapter.GameName} bridge" );

		_transitioning = true;

		ActiveAdapter = null;

		Task.Run( () =>
		{
			try
			{
				app.Simulator.SetDataSource( null );

				adapter.Stop();
			}
			catch ( Exception exception )
			{
				app.Logger.WriteLine( $"[GameBridge] Exception caught while deactivating the {adapter.GameName} bridge: {exception.Message.Trim()}" );
			}
			finally
			{
				_transitioning = false;

				UpdatePage();
			}
		} );
	}

	private static void UpdatePage()
	{
		var app = App.Instance!;

		app.Dispatcher.Invoke( () =>
		{
			_gameBridgePage.Update();
		} );
	}
}
