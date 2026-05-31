using IRSDKSharper;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Components;

/// <summary>
/// Detects iRacing race events from telemetry and dispatches localized commentary
/// through the TextToSpeech service using per-role voice slots.
/// </summary>
public sealed class Commentary
{
	// Voice slot indices (matches VoiceSlotSettings.CreateDefaults order)
	private const int SlotSpotter = 1;

	// Priority levels — lower number = higher priority
	private const int PriorityFlag      = 1;
	private const int PriorityProximity = 3;

	// Commentary templates loaded per-language
	private readonly CommentaryTemplates _templates = new();

	/// <summary>Read-only access to the loaded commentary templates, used by the UI for cache pre-generation.</summary>
	public CommentaryTemplates Templates => _templates;

	// Spotter state tracked between ticks
	private IRacingSdkEnum.CarLeftRight _prevCarLeftRight = IRacingSdkEnum.CarLeftRight.Clear;
	private double _lastCarLeftRightReminderTime = 0.0;

	// Flag state tracked between ticks
	private IRacingSdkEnum.Flags _prevSessionFlags = 0;
	private uint _prevPlayerCarIdxSessionFlags = 0;

	// Whether the session is currently in a racing state (prevents commentary in warmup etc.)
	private bool _isRacingActive = false;

	public void Initialize( string? language = null )
	{
		var app = App.Instance!;

		language ??= DataContext.DataContext.Instance.Settings.CommentaryElevenLabsLanguage;

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

		if ( newState == IRacingSdkEnum.SessionState.Racing || newState == IRacingSdkEnum.SessionState.Checkered )
		{
			_isRacingActive = true;
		}
		else
		{
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
		var now = sim.SessionTime;

		// --- Flag calls (higher priority — checked first) ---
		CheckFlagCalls( sim );

		// --- Spotter calls ---
		CheckSpotterCalls( sim, now );
	}

	// -------------------------------------------------------------------------
	// Event detection helpers
	// -------------------------------------------------------------------------

	private void CheckFlagCalls( Simulator sim )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		if ( !settings.CommentarySpotterEnabled || !settings.CommentarySpotterFlagCalls )
		{
			return;
		}

		var sessionFlags    = sim.SessionFlags;
		var playerIdx       = sim.PlayerCarIdx;
		var playerCarFlags  = ( playerIdx >= 0 && playerIdx < sim.CarIdxSessionFlags.Length )
			? sim.CarIdxSessionFlags[ playerIdx ]
			: 0u;

		var sessionRaised = (IRacingSdkEnum.Flags) ( (uint) sessionFlags & ~(uint) _prevSessionFlags );
		var playerRaised  = playerCarFlags & ~_prevPlayerCarIdxSessionFlags;

		_prevSessionFlags          = sessionFlags;
		_prevPlayerCarIdxSessionFlags = playerCarFlags;

		// Process flag bits in priority order.
		// Flag announcements queue up without interrupting already-playing spotter speech
		// so that multiple flags back-to-back are heard in sequence.

		if ( HasFlag( sessionRaised, IRacingSdkEnum.Flags.StartReady ) || HasFlag( playerRaised, IRacingSdkEnum.Flags.StartReady ) )
		{
			EnqueueFlag( "SpotterFlagStartReady" );
		}

		if ( HasFlag( sessionRaised, IRacingSdkEnum.Flags.OneLapToGreen ) || HasFlag( playerRaised, IRacingSdkEnum.Flags.OneLapToGreen ) )
		{
			EnqueueFlag( "SpotterFlagOneLapToGreen" );
		}

		// StartGo and Green are treated as the same event — both trigger the "green flag" announcement.
		if ( HasFlag( sessionRaised, IRacingSdkEnum.Flags.StartGo ) || HasFlag( playerRaised, IRacingSdkEnum.Flags.StartGo )
			|| HasFlag( sessionRaised, IRacingSdkEnum.Flags.Green ) || HasFlag( playerRaised, IRacingSdkEnum.Flags.Green ) )
		{
			EnqueueFlag( "SpotterFlagGreen" );
		}

		if ( HasFlag( sessionRaised, IRacingSdkEnum.Flags.YellowWaving ) || HasFlag( playerRaised, IRacingSdkEnum.Flags.YellowWaving ) )
		{
			EnqueueFlag( "SpotterFlagYellowWaving" );
		}

		if ( HasFlag( sessionRaised, IRacingSdkEnum.Flags.CautionWaving ) || HasFlag( playerRaised, IRacingSdkEnum.Flags.CautionWaving ) )
		{
			EnqueueFlag( "SpotterFlagCautionWaving" );
		}

		if ( HasFlag( sessionRaised, IRacingSdkEnum.Flags.Debris ) || HasFlag( playerRaised, IRacingSdkEnum.Flags.Debris ) )
		{
			EnqueueFlag( "SpotterFlagDebris" );
		}

		if ( HasFlag( sessionRaised, IRacingSdkEnum.Flags.Blue ) || HasFlag( playerRaised, IRacingSdkEnum.Flags.Blue ) )
		{
			EnqueueFlag( "SpotterFlagBlue" );
		}

		if ( HasFlag( sessionRaised, IRacingSdkEnum.Flags.White ) || HasFlag( playerRaised, IRacingSdkEnum.Flags.White ) )
		{
			EnqueueFlag( "SpotterFlagWhite" );
		}

		if ( HasFlag( sessionRaised, IRacingSdkEnum.Flags.Checkered ) || HasFlag( playerRaised, IRacingSdkEnum.Flags.Checkered ) )
		{
			EnqueueFlag( "SpotterFlagCheckered" );
		}

		if ( HasFlag( playerRaised, IRacingSdkEnum.Flags.Black ) )
		{
			EnqueueFlag( "SpotterFlagBlack" );
		}

		if ( HasFlag( playerRaised, IRacingSdkEnum.Flags.Disqualify ) )
		{
			EnqueueFlag( "SpotterFlagDisqualify" );
		}

		if ( HasFlag( playerRaised, IRacingSdkEnum.Flags.Repair ) )
		{
			EnqueueFlag( "SpotterFlagRepair" );
		}
	}

	private static bool HasFlag( IRacingSdkEnum.Flags raised, IRacingSdkEnum.Flags flag ) =>
		( raised & flag ) != 0;

	private static bool HasFlag( uint raised, IRacingSdkEnum.Flags flag ) =>
		( raised & (uint) flag ) != 0;

	/// <summary>
	/// Enqueues a flag announcement at high priority without interrupting already-playing speech,
	/// so multiple sequential flag events are heard one after another.
	/// </summary>
	private void EnqueueFlag( string eventKey )
	{
		string? phrase = _templates.GetRandomPhrase( eventKey );

		if ( phrase is not null )
		{
			App.Instance?.TextToSpeech.Enqueue( SlotSpotter, phrase, PriorityFlag );
		}
	}

	private void CheckSpotterCalls( Simulator sim, double now )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		if ( !settings.CommentarySpotterCarProximity )
		{
			return;
		}

		// Only call proximity when conditions are appropriate for live racing
		if ( sim.IsReplayPlaying || !sim.IsOnTrack || sim.OnPitRoad )
		{
			// Reset proximity state so calls fire fresh when conditions become eligible again.
			// Flag tracking state is intentionally NOT reset here — flag bits must only
			// be re-announced when the iRacing SDK actually raises them again.
			ResetProximityState();
			return;
		}

		var carLeftRight = sim.CarLeftRight;

		if ( carLeftRight != _prevCarLeftRight )
		{
			_prevCarLeftRight = carLeftRight;

			// Interrupt only if a lower-priority (proximity) call is playing.
			// If a flag announcement is currently playing, let it finish.
			var playingPriority = App.Instance?.TextToSpeech.GetSlotPlayingPriority( SlotSpotter ) ?? int.MaxValue;

			if ( playingPriority > PriorityFlag )
			{
				App.Instance?.TextToSpeech.InterruptSlot( SlotSpotter );
			}

			switch ( carLeftRight )
			{
				case IRacingSdkEnum.CarLeftRight.CarLeft:
					EnqueueRandom( "SpotterCarLeft", SlotSpotter, PriorityProximity );
					break;

				case IRacingSdkEnum.CarLeftRight.CarRight:
					EnqueueRandom( "SpotterCarRight", SlotSpotter, PriorityProximity );
					break;

				case IRacingSdkEnum.CarLeftRight.CarLeftRight:
					EnqueueRandom( "SpotterThreeWideMiddle", SlotSpotter, PriorityProximity );
					break;

				case IRacingSdkEnum.CarLeftRight.TwoCarsLeft:
					EnqueueRandom( "SpotterThreeWideRight", SlotSpotter, PriorityProximity );
					break;

				case IRacingSdkEnum.CarLeftRight.TwoCarsRight:
					EnqueueRandom( "SpotterThreeWideLeft", SlotSpotter, PriorityProximity );
					break;

				case IRacingSdkEnum.CarLeftRight.Clear:
					EnqueueRandom( "SpotterClear", SlotSpotter, PriorityProximity );
					break;
			}

			// Reset reminder timer so the interval starts fresh after each state change
			_lastCarLeftRightReminderTime = now;

			return;
		}

		// Periodic proximity reminder while car is alongside
		if ( carLeftRight == IRacingSdkEnum.CarLeftRight.Clear )
		{
			return;
		}

		var interval = (double) settings.CommentarySpotterCarProximityReminderInterval;

		if ( now - _lastCarLeftRightReminderTime < interval )
		{
			return;
		}

		// Skip the reminder if the spotter is still speaking — don't interrupt
		if ( App.Instance?.TextToSpeech.IsSlotPlaying( SlotSpotter ) == true )
		{
			return;
		}

		_lastCarLeftRightReminderTime = now;

		// Randomly choose between "Still there" and repeating the current proximity phrase
		var reminderKey = carLeftRight switch
		{
			IRacingSdkEnum.CarLeftRight.CarLeft => "SpotterCarLeft",
			IRacingSdkEnum.CarLeftRight.CarRight => "SpotterCarRight",
			IRacingSdkEnum.CarLeftRight.CarLeftRight => "SpotterThreeWideMiddle",
			IRacingSdkEnum.CarLeftRight.TwoCarsLeft => "SpotterThreeWideRight",
			IRacingSdkEnum.CarLeftRight.TwoCarsRight => "SpotterThreeWideLeft",
			_ => "SpotterCarLeft"
		};

		var useStillThere = Random.Shared.NextDouble() < 0.5;

		EnqueueRandom( useStillThere ? "SpotterStillThere" : reminderKey, SlotSpotter, PriorityProximity );
	}

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	private static bool IsCommentaryEnabled()
	{
		var settings = DataContext.DataContext.Instance.Settings;

		return settings.CommentaryEnabled;
	}

	private void EnqueueRandom( string eventKey, int slotIndex, int priority = 3 )
	{
		string? phrase = _templates.GetRandomPhrase( eventKey );

		if ( phrase is not null )
		{
			Enqueue( slotIndex, phrase, priority );
		}
	}

	private static void Enqueue( int slotIndex, string text, int priority = 3 )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
		{
			return;
		}

		App.Instance?.TextToSpeech.Enqueue( slotIndex, text, priority );
	}

	private void ResetPerSessionState()
	{
		ResetProximityState();

		_prevSessionFlags = 0;
		_prevPlayerCarIdxSessionFlags = 0;
	}

	private void ResetProximityState()
	{
		_prevCarLeftRight = IRacingSdkEnum.CarLeftRight.Clear;
		_lastCarLeftRightReminderTime = 0.0;
	}
}
