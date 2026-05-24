using MarvinsAIRARefactored.Classes;
using IRSDKSharper;

namespace MarvinsAIRARefactored.Components;

/// <summary>
/// Detects iRacing race events from telemetry and dispatches localized TTS commentary
/// through the TextToSpeech service using per-role voice slots.
/// </summary>
public sealed class Commentary
{
	// Voice slot indices (matches VoiceSlotSettings.CreateDefaults order)
	private const int SlotCrewChief = 0;
	private const int SlotSpotter = 1;
	private const int SlotSportscaster1 = 2;
	private const int SlotSportscaster2 = 3;
	private const int SlotPitReporter = 4;

	// Fuel warning threshold: warn when estimated laps remaining drops to this level
	private const float FuelWarningLapsThreshold = 3.0f;

	// Close-battle gap threshold in seconds (F2Time)
	private const float CloseBattleGapSeconds = 1.0f;

	// Minimum time between the same event type to avoid spam (seconds)
	private const double OvertakeCooldown = 15.0;
	private const double CloseBattleCooldown = 30.0;
	private const double FuelWarningCooldown = 60.0;
	private const double TireWarningCooldown = 60.0;
	private const double PitWindowOpenCooldown = 60.0;
	private const double PitEntryExitCooldown = 10.0;
	private const double IncidentCooldown = 20.0;

	// Commentary templates loaded per-language
	private readonly CommentaryTemplates _templates = new();

	/// <summary>Read-only access to the loaded commentary templates, used by the UI for cache pre-generation.</summary>
	public CommentaryTemplates Templates => _templates;

	// Cooldown timestamps keyed by event name
	private readonly Dictionary<string, double> _cooldowns = [];

	// Per-car state tracked between ticks
	private readonly int[] _prevCarIdxPosition = new int[ IRacingSdkConst.MaxNumCars ];
	private readonly int[] _prevCarIdxLapCompleted = new int[ IRacingSdkConst.MaxNumCars ];
	private readonly bool[] _prevCarIdxOnPitRoad = new bool[ IRacingSdkConst.MaxNumCars ];
	private readonly float[] _prevCarIdxBestLapTime = new float[ IRacingSdkConst.MaxNumCars ];

	// Player state tracked between ticks
	private float _prevBestLapTimeInSession = float.MaxValue;
	private int _prevPlayerIncidentCount = 0;
	private bool _prevPitsOpen = false;
	private bool _fuelWarningSent = false;

	// Whether the session is currently in a racing state (prevents commentary in warmup etc.)
	private bool _isRacingActive = false;

	public void Initialize( string? language = null )
	{
		var app = App.Instance!;

		language ??= DataContext.DataContext.Instance.Settings.TtsLanguage;

		_templates.Initialize( language );

		app.Logger.WriteLine( $"[Commentary] Initialized, language={_templates.LoadedLanguage}" );
	}

	/// <summary>Called by Simulator.cs whenever SessionState changes.</summary>
	public void SessionStateChanged( IRacingSdkEnum.SessionState newState )
	{
		if ( !IsCommentaryEnabled() )
		{
			return;
		}

		var settings = DataContext.DataContext.Instance.Settings;

		if ( newState == IRacingSdkEnum.SessionState.Racing )
		{
			_isRacingActive = true;

			if ( settings.CommentarySessionStartEnd )
			{
				EnqueueRandom( "SessionStart", SlotSportscaster1 );
			}
		}
		else if ( newState == IRacingSdkEnum.SessionState.Checkered || newState == IRacingSdkEnum.SessionState.CoolDown )
		{
			if ( _isRacingActive && settings.CommentarySessionStartEnd )
			{
				var app = App.Instance!;
				var sim = app.Simulator;

				// Try to resolve the leader's name for the session-end call
				string leaderName = ResolveLeaderName( sim );
				string text = ResolvePhrase( "SessionEnd", ("{driver}", leaderName) );

				Enqueue( SlotSportscaster1, text );
			}

			_isRacingActive = false;
			ResetPerSessionState();
		}
	}

	/// <summary>Called each timer tick by the App worker thread.</summary>
	public void Tick( App app )
	{
		if ( !IsCommentaryEnabled() || !_isRacingActive )
		{
			return;
		}

		var sim = app.Simulator;
		var settings = DataContext.DataContext.Instance.Settings;
		double now = sim.SessionTime;

		// --- Caution / Red flag (flag-based) ---
		CheckFlags( sim, settings, now );

		// --- Per-car events ---
		CheckPerCarEvents( sim, settings, now );

		// --- Fuel warning ---
		CheckFuelWarning( sim, settings, now );

		// --- Pit window opening ---
		CheckPitWindowOpen( sim, settings, now );

		// --- Tire wear warning (use worn lap-time degradation as proxy) ---
		CheckTireWarning( sim, settings, now );

		// --- Spotter calls ---
		CheckSpotterCalls( sim );

		// Advance per-car state for next frame
		CopyToPrev( sim );
	}

	// -------------------------------------------------------------------------
	// Event detection helpers
	// -------------------------------------------------------------------------

	private void CheckFlags( Simulator sim, DataContext.Settings settings, double now )
	{
		var flags = sim.SessionFlags;

		if ( settings.CommentaryCaution )
		{
			bool cautionNow = flags.HasFlag( IRacingSdkEnum.Flags.Caution ) || flags.HasFlag( IRacingSdkEnum.Flags.CautionWaving );

			if ( cautionNow && !_cooldowns.TryGetValue( "Caution", out double lastCaution ) || ( cautionNow && now - _cooldowns.GetValueOrDefault( "Caution" ) > 60.0 ) )
			{
				_cooldowns[ "Caution" ] = now;
				EnqueueRandom( "Caution", SlotSportscaster2 );
			}
		}

		if ( flags.HasFlag( IRacingSdkEnum.Flags.Red ) )
		{
			if ( !_cooldowns.TryGetValue( "RedFlag", out double last ) || now - last > 30.0 )
			{
				_cooldowns[ "RedFlag" ] = now;
				EnqueueRandom( "RedFlag", SlotSportscaster1 );
			}
		}
	}

	private void CheckPerCarEvents( Simulator sim, DataContext.Settings settings, double now )
	{
		int playerCarIdx = sim.PlayerCarIdx;

		for ( int i = 0; i < IRacingSdkConst.MaxNumCars; i++ )
		{
			int curPos = sim.CarIdxPosition[ i ];
			int prevPos = _prevCarIdxPosition[ i ];

			// Skip cars that are not active (position 0 means not in use)
			if ( curPos <= 0 || prevPos <= 0 )
			{
				continue;
			}

			// Overtake detection: position improved (lower is better)
			if ( settings.CommentaryOvertake && curPos < prevPos )
			{
				int overtakingCarIdx = i;

				// Find who was displaced (the car that now has prevPos)
				int displacedCarIdx = FindCarAtPosition( sim, prevPos, i );

				if ( displacedCarIdx >= 0 && !WasOnCooldown( "Overtake", now, OvertakeCooldown ) )
				{
					_cooldowns[ "Overtake" ] = now;

					string attacker = ResolveDriverName( overtakingCarIdx );
					string defender = ResolveDriverName( displacedCarIdx );
					int slot = ( overtakingCarIdx == playerCarIdx ) ? SlotSportscaster1 : SlotSportscaster2;
					string text = ResolvePhrase( "Overtake",
						("{attacker}", attacker),
						("{defender}", defender),
						("{position}", curPos.ToString()) );
					Enqueue( slot, text );
				}
			}

			// Pit stop entry
			if ( settings.CommentaryPitStop )
			{
				bool onPitNow = sim.CarIdxOnPitRoad[ i ];
				bool onPitPrev = _prevCarIdxOnPitRoad[ i ];

				if ( onPitNow && !onPitPrev && !WasOnCooldown( $"PitEntry_{i}", now, PitEntryExitCooldown ) )
				{
					_cooldowns[ $"PitEntry_{i}" ] = now;
					string driver = ResolveDriverName( i );
					string text = ResolvePhrase( "PitStopEntry", ("{driver}", driver) );
					int slot = ( i == playerCarIdx ) ? SlotCrewChief : SlotPitReporter;
					Enqueue( slot, text );
				}
				else if ( !onPitNow && onPitPrev && !WasOnCooldown( $"PitExit_{i}", now, PitEntryExitCooldown ) )
				{
					_cooldowns[ $"PitExit_{i}" ] = now;
					string driver = ResolveDriverName( i );
					string text = ResolvePhrase( "PitStopExit", ("{driver}", driver) );
					int slot = ( i == playerCarIdx ) ? SlotCrewChief : SlotPitReporter;
					Enqueue( slot, text );
				}
			}

			// Fastest lap: new personal best that is also the session-wide best
			if ( settings.CommentaryFastestLap )
			{
				float best = sim.CarIdxBestLapTime[ i ];
				float prevBest = _prevCarIdxBestLapTime[ i ];

				if ( best > 0f && best < prevBest && best < _prevBestLapTimeInSession )
				{
					_prevBestLapTimeInSession = best;

					if ( !WasOnCooldown( "FastestLap", now, 20.0 ) )
					{
						_cooldowns[ "FastestLap" ] = now;
						string driver = ResolveDriverName( i );
						string lapTime = FormatLapTime( best );
						string text = ResolvePhrase( "FastestLap",
							("{driver}", driver),
							("{lapTime}", lapTime) );
						Enqueue( SlotSportscaster1, text );
					}
				}
			}

			// Incident: new incident for the player's car
			if ( settings.CommentaryIncident && i == playerCarIdx )
			{
				int incidents = sim.PlayerCarMyIncidentCount;

				if ( incidents > _prevPlayerIncidentCount && !WasOnCooldown( "Incident", now, IncidentCooldown ) )
				{
					_cooldowns[ "Incident" ] = now;
					int delta = incidents - _prevPlayerIncidentCount;
					string text = ResolvePhrase( "Incident",
						("{driver}", ResolveDriverName( i )),
						("{seconds}", ( delta * 1 ).ToString()) );
					Enqueue( SlotCrewChief, text );
					_prevPlayerIncidentCount = incidents;
				}
			}
		}

		// Close battle: player vs. car directly ahead within CloseBattleGapSeconds
		if ( settings.CommentaryCloseBattle )
		{
			float gap = sim.CarDistAhead;

			if ( gap > 0f && gap < 50f ) // 50 m ~ roughly 1 sec gap depending on speed
			{
				// Refine using F2Time gap for the player's position if available
				float f2 = sim.CarIdxF2Time[ playerCarIdx ];

				if ( f2 > 0f && f2 < CloseBattleGapSeconds && !WasOnCooldown( "CloseBattle", now, CloseBattleCooldown ) )
				{
					_cooldowns[ "CloseBattle" ] = now;
					int aheadIdx = FindCarAtPosition( sim, sim.PlayerCarPosition - 1, playerCarIdx );
					string driver1 = ResolveDriverName( playerCarIdx );
					string driver2 = aheadIdx >= 0 ? ResolveDriverName( aheadIdx ) : "the car ahead";
					string text = ResolvePhrase( "CloseBattle",
						("{driver1}", driver1),
						("{driver2}", driver2) );
					Enqueue( SlotSportscaster2, text );
				}
			}
		}
	}

	private void CheckFuelWarning( Simulator sim, DataContext.Settings settings, double now )
	{
		if ( !settings.CommentaryCrewFuelWarning )
		{
			return;
		}

		float fuelUse = sim.FuelUsePerHour;
		float fuelLevel = sim.FuelLevel;

		if ( fuelUse <= 0f || fuelLevel <= 0f )
		{
			return;
		}

		float hoursRemaining = fuelLevel / fuelUse;
		float lapTimeSeconds = sim.LapLastLapTime > 0f ? sim.LapLastLapTime : sim.LapBestLapTime;

		if ( lapTimeSeconds <= 0f )
		{
			return;
		}

		float lapsRemaining = hoursRemaining * 3600f / lapTimeSeconds;

		if ( lapsRemaining <= FuelWarningLapsThreshold && !_fuelWarningSent && !WasOnCooldown( "FuelWarning", now, FuelWarningCooldown ) )
		{
			_fuelWarningSent = true;
			_cooldowns[ "FuelWarning" ] = now;
			string text = ResolvePhrase( "CrewFuelWarning", ("{laps}", ( (int) MathF.Ceiling( lapsRemaining ) ).ToString()) );
			Enqueue( SlotCrewChief, text );
		}
		else if ( lapsRemaining > FuelWarningLapsThreshold + 1f )
		{
			// Reset so the warning fires again if fuel drops again (e.g. after a pit stop)
			_fuelWarningSent = false;
		}
	}

	private void CheckPitWindowOpen( Simulator sim, DataContext.Settings settings, double now )
	{
		if ( !settings.CommentaryCrewPitWindowOpen )
		{
			return;
		}

		bool pitsOpen = sim.PitsOpen;

		if ( pitsOpen && !_prevPitsOpen && !WasOnCooldown( "PitWindowOpen", now, PitWindowOpenCooldown ) )
		{
			_cooldowns[ "PitWindowOpen" ] = now;
			EnqueueRandom( "CrewPitWindowOpen", SlotCrewChief );
		}

		_prevPitsOpen = pitsOpen;
	}

	private void CheckTireWarning( Simulator sim, DataContext.Settings settings, double now )
	{
		if ( !settings.CommentaryCrewTireWarning )
		{
			return;
		}

		// Proxy: if the player's last lap is significantly slower than their best, tires may be worn
		float best = sim.LapBestLapTime;
		float last = sim.LapLastLapTime;

		if ( best <= 0f || last <= 0f )
		{
			return;
		}

		float degradation = ( last - best ) / best;

		if ( degradation > 0.015f && !WasOnCooldown( "TireWarning", now, TireWarningCooldown ) )
		{
			_cooldowns[ "TireWarning" ] = now;
			EnqueueRandom( "CrewTireWarning", SlotCrewChief );
		}
	}

	private void CheckSpotterCalls( Simulator sim )
	{
		var carLeftRight = sim.CarLeftRight;

		switch ( carLeftRight )
		{
			case IRacingSdkEnum.CarLeftRight.CarLeft:
				EnqueueRandom( "SpotterCarLeft", SlotSpotter );
				break;

			case IRacingSdkEnum.CarLeftRight.CarRight:
				EnqueueRandom( "SpotterCarRight", SlotSpotter );
				break;

			case IRacingSdkEnum.CarLeftRight.CarLeftRight:
				EnqueueRandom( "SpotterOverlap", SlotSpotter );
				break;

			case IRacingSdkEnum.CarLeftRight.Clear:
				// Only say "clear" when transitioning from a side car state — handled by the
				// built-in iRacing spotter; we avoid redundancy unless commentary spotter is preferred.
				break;
		}
	}

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	private bool IsCommentaryEnabled()
	{
		var settings = DataContext.DataContext.Instance.Settings;
		return settings.TtsEnabled && settings.CommentaryEnabled;
	}

	private bool WasOnCooldown( string key, double now, double cooldownSeconds )
	{
		return _cooldowns.TryGetValue( key, out double last ) && now - last < cooldownSeconds;
	}

	private void EnqueueRandom( string eventKey, int slotIndex )
	{
		string? phrase = _templates.GetRandomPhrase( eventKey );

		if ( phrase is not null )
		{
			Enqueue( slotIndex, phrase );
		}
	}

	private void Enqueue( int slotIndex, string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
		{
			return;
		}

		App.Instance?.TextToSpeech.Enqueue( slotIndex, text );
	}

	/// <summary>Resolves a phrase template by substituting all provided token pairs.</summary>
	private string ResolvePhrase( string eventKey, params (string token, string value)[] substitutions )
	{
		string? phrase = _templates.GetRandomPhrase( eventKey );

		if ( phrase is null )
		{
			return string.Empty;
		}

		foreach ( var (token, value) in substitutions )
		{
			phrase = phrase.Replace( token, value, StringComparison.OrdinalIgnoreCase );
		}

		return phrase;
	}

	private static string ResolveDriverName( int carIdx )
	{
		var app = App.Instance!;

		if ( app.Drivers.TryGetDriverByCarIdx( carIdx, out var driver ) && driver is not null )
		{
			return driver.UserName ?? $"Car {carIdx}";
		}

		return $"Car {carIdx}";
	}

	private static string ResolveLeaderName( Simulator sim )
	{
		// Find the car in position 1
		for ( int i = 0; i < IRacingSdkConst.MaxNumCars; i++ )
		{
			if ( sim.CarIdxPosition[ i ] == 1 )
			{
				return ResolveDriverName( i );
			}
		}

		return "the leader";
	}

	private static int FindCarAtPosition( Simulator sim, int position, int excludeIdx )
	{
		for ( int i = 0; i < IRacingSdkConst.MaxNumCars; i++ )
		{
			if ( i != excludeIdx && sim.CarIdxPosition[ i ] == position )
			{
				return i;
			}
		}

		return -1;
	}

	private static string FormatLapTime( float seconds )
	{
		if ( seconds <= 0f )
		{
			return "N/A";
		}

		int mins = (int) ( seconds / 60f );
		float secs = seconds - mins * 60f;

		return mins > 0 ? $"{mins}:{secs:00.000}" : $"{secs:0.000}";
	}

	private void CopyToPrev( Simulator sim )
	{
		Array.Copy( sim.CarIdxPosition, _prevCarIdxPosition, IRacingSdkConst.MaxNumCars );
		Array.Copy( sim.CarIdxLapCompleted, _prevCarIdxLapCompleted, IRacingSdkConst.MaxNumCars );
		Array.Copy( sim.CarIdxOnPitRoad, _prevCarIdxOnPitRoad, IRacingSdkConst.MaxNumCars );
		Array.Copy( sim.CarIdxBestLapTime, _prevCarIdxBestLapTime, IRacingSdkConst.MaxNumCars );
	}

	private void ResetPerSessionState()
	{
		_prevBestLapTimeInSession = float.MaxValue;
		_prevPlayerIncidentCount = 0;
		_prevPitsOpen = false;
		_fuelWarningSent = false;
		_cooldowns.Clear();
		Array.Clear( _prevCarIdxPosition );
		Array.Clear( _prevCarIdxLapCompleted );
		Array.Clear( _prevCarIdxOnPitRoad );
		Array.Clear( _prevCarIdxBestLapTime );
	}
}
