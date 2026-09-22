
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

	// true while a bridge is active but the game's physics output has stopped advancing (paused, in a menu) -
	// the bridge keeps committing frames with the last held torque in that state, so the racing wheel watches
	// this flag to fade the FFB out instead of grinding the frozen torque into the soft lock
	public bool TelemetryDataIsStale { get; private set; } = false;

	private const double StaleDataTimeoutSeconds = 0.25;

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

	// called from the playout timer worker thread at ~360 Hz, immediately before the racing wheel update,
	// so bridge sub-samples are taken on the precise kernel timer and the freshest torque reading is in the
	// frame with near zero added latency. Exceptions must never propagate into the worker thread (it treats
	// them as fatal), so a failing bridge is logged and deactivated instead.
	public void Pump( double totalSeconds )
	{
		var adapter = ActiveAdapter;

		if ( adapter == null )
		{
			TelemetryDataIsStale = false;

			return;
		}

		try
		{
			adapter.Pump( totalSeconds );

			TelemetryDataIsStale = ( adapter.LastDataActivitySeconds != double.MinValue ) && ( totalSeconds - adapter.LastDataActivitySeconds >= StaleDataTimeoutSeconds );
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

				// the bridges are pumped from the playout timer worker thread, but that timer only runs
				// while the simulator is connected - and the simulator cannot connect until the bridge has
				// pumped its first frames, so the timer is resumed here to break the circle
				app.PlayoutTimer.Suspend = false;
			}
			catch ( Exception exception )
			{
				app.Logger.WriteLine( $"[GameBridge] Exception caught while activating the {adapter.GameName} bridge: {exception.Message.Trim()}" );

				adapter.Stop();

				if ( !app.Simulator.IsConnected )
				{
					app.PlayoutTimer.Suspend = true;
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
