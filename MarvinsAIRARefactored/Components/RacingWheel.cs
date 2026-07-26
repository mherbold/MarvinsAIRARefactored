
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

using CsvHelper;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.FFB;
using MarvinsAIRARefactored.Windows;

using static MarvinsAIRARefactored.Windows.MainWindow;

namespace MarvinsAIRARefactored.Components;

public class RacingWheel
{
	public enum Algorithm
	{
		Native60Hz,
		Native360Hz,
		DetailBooster,
		DeltaLimiter,
		DetailBoosterOn60Hz,
		DeltaLimiterOn60Hz,
		SlewAndTotalCompression,
		MultiAdjustmentToolkit
	};

	public enum MultiFFBSourceOptions
	{
		Native60Hz,
		Native360Hz,
		Hybrid10,
		HybridVariable30,
		DefaultsNative60Hz,
		DefaultsNative360Hz,
		DefaultsHybrid10,
		DefaultsHybridVariable30,
		PresetBoostDetail,
		PresetReduceDetail,
		PresetReduceBigBumps,
		PresetBasicFFB,
		PresetBalancedFFB,
		_Dummy1_,
		_Dummy2_
	};

	public enum VibrationPattern
	{
		None,
		SineWave,
		SquareWave,
		TriangleWave,
		SawtoothWaveIn,
		SawtoothWaveOut
	};

	public enum ConstantForceDirection
	{
		None,
		DecreaseForce,
		IncreaseForce,
	};

	private const int UpdateInterval = 6;
	private const int PlayoutWindowLastIndex = Simulator.SamplesPerFrame360Hz + 1;

	private const float UnsuspendTimeMS = 1000f;
	private const float FadeInTimeMS = 2000f;
	private const float FadeOutTimeMS = 750f;
	private const float TestSignalTimeMS = 2000f;

	// was 0.01 at the old ~500 Hz tick rate — scaled ×(500/360) to keep the same peak-torque attack time at 360 Hz
	private const float PeakTorqueLerpAlpha = 0.0139f;

	// the playout clock nominally spans one 60 Hz frame (~16.7 ms); past this much extra we count an underrun
	private const float PlayoutUnderrunToleranceMS = 4f;

	// Commentary voice slot used for MAIRA system announcements (matches VoiceSlotSettings.CreateDefaults order).
	private const int CommentarySlotMaira = 5;

	private Guid? _currentRacingWheelGuid = null;

	private bool _isSuspended = true;
	private bool _usingSteeringWheelTorqueData = false;

	public Guid? NextRacingWheelGuid { private get; set; } = null;
	public bool SuspendForceFeedback { get; private set; } = true; // true if we want to suspend FFB (for various reasons)
	public bool ResetForceFeedback { private get; set; } = false; // set to true manually (via reset button)
	public bool UseSteeringWheelTorqueData { private get; set; } = false; // false if simulator is disconnected or if driver is not on track
	public bool ActivateCrashProtection { private get; set; } = false; // set to true to activate crash protection
	public bool ActivateCurbProtection { private get; set; } = false; // set to true to activate curb protection
	public bool PlayTestSignal { private get; set; } = false; // set to true manually (via test button)
	public bool ClearPeakTorque { private get; set; } = false; // set to clear peak torque
	public bool AutoSetMaxForce { private get; set; } = false; // set to auto-set the max force setting
	public bool UpdateAlgorithmPreview { private get; set; } = true; // set to update the algorithm preview

	// preview horizontal zoom-out (Ctrl+wheel on the preview graph): draw every Nth recorded sample — 1 = 100%
	// (every sample), 2 = 50%, ... 20 = 5%. Drawing only; the replay still processes every sample so module
	// state stays sample-accurate. In-memory view state, never serialized.
	public const int MaxAlgorithmPreviewSkip = 20;

	public int AlgorithmPreviewSkip { private get; set; } = 1;

	public float AutoTorque { get => _autoTorque; }
	public float OutputTorque { get => _outputTorque; }
	public bool IsFFBClipping { get => !_isSuspended && MathF.Abs( _outputTorque ) >= 0.99f; }
	public bool CrashProtectionIsActive { get => _liveEngine.CrashProtectionActive; }
	public bool CurbProtectionIsActive { get => _liveEngine.CurbProtectionActive; }
	public bool FadingIsActive { get => _fadeTimerMS > 0f; }

	// Crash/curb protection thresholds published by the live engine's protection modules; Simulator reads these
	// to decide when to trigger. Off = long/lat g-force >= 20 (disabled) / shock velocity 0 — same "disabled"
	// semantics as the old settings guards.
	public float CrashProtectionLongGForceThreshold { get => _liveEngine.CrashLongGForceThreshold; }
	public float CrashProtectionLatGForceThreshold { get => _liveEngine.CrashLatGForceThreshold; }
	public float CurbProtectionShockVelocityThreshold { get => _liveEngine.CurbShockVelocityThreshold; }

	private float _unsuspendTimerMS = 0f;
	private float _fadeTimerMS = 0f;
	private float _testSignalTimerMS = 0f;

	private float _outputTorque = 0f;
	private float _peakTorque = 0f;
	private float _autoTorque = 0f;

	private float _lastUnfadedOutputTorque = 0f;

	// Rising-edge trackers for the protection chat/voice announcements (side effects stay out of the modules).
	private bool _lastCrashProtectionActive = false;
	private bool _lastCurbProtectionActive = false;

	// 60 Hz → 500 Hz handoff. ProcessTelemetryFrame (telemetry thread) runs the FFB graph over the six 360 Hz
	// samples into the _burst* arrays, then publishes them to the _staged* arrays under the _stagedSeq seqlock
	// (odd = writing, even = published). UpdatePlayout (multimedia timer thread) copies a published block into
	// the _consume* scratch, verifies the sequence didn't change, and only then shifts it into its private
	// playout window — so a torn copy is simply discarded (≤ 2 ms of held torque), never played.

	private readonly float[] _burstOutputTorque = new float[ Simulator.SamplesPerFrame360Hz ];
	private readonly float[] _burstInputTorque = new float[ Simulator.SamplesPerFrame360Hz ];
	private readonly float[] _burstLFEMagnitude = new float[ Simulator.SamplesPerFrame360Hz ];

	private readonly float[] _stagedOutputTorque = new float[ Simulator.SamplesPerFrame360Hz ];
	private readonly float[] _stagedInputTorque = new float[ Simulator.SamplesPerFrame360Hz ];
	private readonly float[] _stagedLFEMagnitude = new float[ Simulator.SamplesPerFrame360Hz ];
	private float _stagedInputTorque60Hz = 0f;
	private int _stagedSeq = 0;

	private readonly float[] _consumeOutputTorque = new float[ Simulator.SamplesPerFrame360Hz ];
	private readonly float[] _consumeInputTorque = new float[ Simulator.SamplesPerFrame360Hz ];
	private readonly float[] _consumeLFEMagnitude = new float[ Simulator.SamplesPerFrame360Hz ];

	// playout-thread private state — [0] = previous frame's last sample (Hermite left tangent), [1..6] = the six
	// processed samples, [7] = duplicate of the newest (same window layout the old input resampler used)
	private readonly float[] _playoutTorqueWindow = new float[ Simulator.SamplesPerFrame360Hz + 2 ];
	private readonly float[] _playoutInputTorque = new float[ Simulator.SamplesPerFrame360Hz ];
	private readonly float[] _playoutLFEMagnitude = new float[ Simulator.SamplesPerFrame360Hz ];
	private float _playoutInputTorque60Hz = 0f;
	private int _lastConsumedSeq = 0;
	private float _playoutElapsedMilliseconds = 0f;
	private bool _playoutUnderrunLatched = false;
	private float _underrunLogTimerMS = 0f;

	// cross-thread control flags
	private volatile bool _ffbActive = false;               // written by UpdatePlayout (owns the device state), read by ProcessTelemetryFrame
	private volatile bool _producerFaulted = false;         // set by ProcessTelemetryFrame's catch, converted to a soft-lock restart by UpdatePlayout

	// diagnostics — late telemetry frames (playout ran out of data) and discarded torn handoffs
	public int PlayoutUnderrunTickCount { get; private set; } = 0;
	public int PlayoutUnderrunEpisodeCount { get; private set; } = 0;
	public int PlayoutTornHandoffCount { get; private set; } = 0;

	// preview bitmap width when no recording is loaded — with a recording it's one pixel per recorded sample
	private const int DefaultAlgorithmPreviewWidth = 3840;

	private readonly GraphBase _algorithmPreviewGraphBase = new();

	// Milestone 3: the modular FFB graph now drives all wheel processing. _liveEngine (volatile) is evaluated by
	// the telemetry-thread frame burst (ProcessTelemetryFrame, 6 × 360 Hz ticks per frame) and rebuilt/swapped on
	// the UI thread on graph/context changes (the reader picks up the new reference next frame, same one-tick
	// tolerance as the old _lastAlgorithm reset).
	// _previewEngine is driven only by Tick (dispatcher) over a recording for the editor preview.
	private volatile FFBGraphEngine _liveEngine = new();
	private readonly FFBGraphEngine _previewEngine = new();

	private int _updateCounter = UpdateInterval + 4;

#if DEBUG

	/// <summary>
	/// Rolling min/avg/max timing for one hot path, reported to the log once per second. Each instance is owned
	/// by exactly one thread (burst = telemetry thread, playout = multimedia timer thread) — no synchronization.
	/// The burst reporter also logs GC collection deltas, the "are we allocation-free" verification signal.
	/// </summary>
	private sealed class PerfStats
	{
		private readonly string _label;
		private readonly bool _includeGCCounts;

		private long _minTicks = long.MaxValue;
		private long _maxTicks = 0;
		private long _sumTicks = 0;
		private int _count = 0;
		private long _reportStartTimestamp = 0;

		private int _lastGen0Count = 0;
		private int _lastGen1Count = 0;
		private int _lastGen2Count = 0;

		public PerfStats( string label, bool includeGCCounts )
		{
			_label = label;
			_includeGCCounts = includeGCCounts;
		}

		public void Update( long elapsedTicks, long timestamp )
		{
			_minTicks = Math.Min( _minTicks, elapsedTicks );
			_maxTicks = Math.Max( _maxTicks, elapsedTicks );
			_sumTicks += elapsedTicks;
			_count++;

			if ( _reportStartTimestamp == 0 )
			{
				_reportStartTimestamp = timestamp;
			}
			else if ( ( timestamp - _reportStartTimestamp ) >= Stopwatch.Frequency )
			{
				var app = App.Instance!;

				var microsecondsPerTick = 1_000_000.0 / Stopwatch.Frequency;

				var minMicroseconds = _minTicks * microsecondsPerTick;
				var avgMicroseconds = _sumTicks * microsecondsPerTick / Math.Max( 1, _count );
				var maxMicroseconds = _maxTicks * microsecondsPerTick;

				if ( _includeGCCounts )
				{
					var gen0Count = GC.CollectionCount( 0 );
					var gen1Count = GC.CollectionCount( 1 );
					var gen2Count = GC.CollectionCount( 2 );

					app.Logger.WriteLine( $"[RacingWheel] {_label} µs min/avg/max = {minMicroseconds:F0}/{avgMicroseconds:F0}/{maxMicroseconds:F0} | GC 0/1/2 = {gen0Count - _lastGen0Count}/{gen1Count - _lastGen1Count}/{gen2Count - _lastGen2Count}" );

					_lastGen0Count = gen0Count;
					_lastGen1Count = gen1Count;
					_lastGen2Count = gen2Count;
				}
				else
				{
					app.Logger.WriteLine( $"[RacingWheel] {_label} µs min/avg/max = {minMicroseconds:F0}/{avgMicroseconds:F0}/{maxMicroseconds:F0}" );
				}

				_minTicks = long.MaxValue;
				_maxTicks = 0;
				_sumTicks = 0;
				_count = 0;
				_reportStartTimestamp = timestamp;
			}
		}
	}

	private readonly PerfStats _burstPerfStats = new( "Burst", includeGCCounts: true );
	private readonly PerfStats _playoutPerfStats = new( "Playout", includeGCCounts: false );

#endif

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[RacingWheel] Initialize >>>" );

		app.Graph.SetLayerColors( Graph.LayerIndex.InputTorque60Hz, 1f, 0f, 0f );
		app.Graph.SetLayerColors( Graph.LayerIndex.InputTorque, 1f, 0f, 0f );
		app.Graph.SetLayerColors( Graph.LayerIndex.InputLFE, 0f, 0f, 1f );
		app.Graph.SetLayerColors( Graph.LayerIndex.OutputTorque, 0f, 1f, 1f );

		_algorithmPreviewGraphBase.Initialize( MainWindow._racingWheelPage.AlgorithmPreview_Image );

		app.Logger.WriteLine( "[RacingWheel] <<< Initialize" );
	}

	public float GetCurrentAutoTorque()
	{
		return _autoTorque;
	}

	/// <summary>
	/// Rebuild the live FFB graph engine from the currently selected FFB graph (which carries the vibration
	/// generator modules too) and swap the volatile reference. UI thread only (structure edits / graph
	/// selection / per-context reload). Tolerant of a missing selection or empty graph dictionary (e.g. during
	/// settings load before the built-ins are ensured) — leaves the current engine in place.
	/// </summary>
	public void RebuildLiveEngine()
	{
		var settings = DataContext.DataContext.Instance.Settings;

		if ( settings.RacingWheelFFBGraphs.TryGetValue( settings.RacingWheelSelectedFFBGraphName, out var graph ) )
		{
			var engine = new FFBGraphEngine();

			engine.Rebuild( graph );

			_liveEngine = engine;
		}
	}

	/// <summary>Zero the live engine's per-tick internal state (called on a per-context value reload). UI thread only.</summary>
	public void ResetLiveEngineState()
	{
		_liveEngine.ResetState();
	}

	/// <summary>
	/// Edit-time atomic write of a single module setting value into BOTH the live and preview engines. UI thread
	/// only (the graph editor knob/switch/choice view-models). Atomic float writes the 360 Hz reader tolerates.
	/// </summary>
	public void SetEngineValue( string moduleId, string key, float value )
	{
		_liveEngine.SetValue( moduleId, key, value );
		_previewEngine.SetValue( moduleId, key, value );
	}

	public void SetEngineTestActive( string moduleId, bool active )
	{
		_liveEngine.SetTestActive( moduleId, active );
		_previewEngine.SetTestActive( moduleId, active );
	}

	public static void SendChatMessage( string? groupKey, string labelKey, string? value = null )
	{
		var app = App.Instance!;

		var localization = DataContext.DataContext.Instance.Localization;

		if ( DataContext.DataContext.Instance.Settings.RacingWheelSendChatMessages && ( app.Simulator.UserName != string.Empty ) )
		{
			var playerName = app.Simulator.UserName;

			playerName = playerName.Replace( " ", "." );

			var label = ( groupKey == null ) ? localization[ labelKey ] : $"{localization[ groupKey ]} {localization[ labelKey ]}";

			app.ChatQueue.SendMessage( $"/{playerName} (MAIRA) {label}", value );
		}
	}

	/// <summary>
	/// Speaks a MAIRA system announcement for the given commentary event key through the MAIRA voice slot.
	/// Enqueue() already no-ops unless commentary is enabled and the MAIRA voice slot is enabled with a voice
	/// selected, so this respects "as long as the voice is enabled" without any extra gating here.
	/// </summary>
	private static void SpeakMairaAnnouncement( string eventKey )
	{
		var app = App.Instance!;

		// No stacking: the MAIRA voice slot is used only for these protection announcements, so if one is already
		// being spoken or is still queued, skip this one rather than piling another phrase on top of it.
		if ( app.TextToSpeech.IsSlotBusy( CommentarySlotMaira ) )
		{
			return;
		}

		var phrase = app.Commentary.Templates.GetRandomPhrase( eventKey );

		if ( phrase != null )
		{
			app.TextToSpeech.Enqueue( CommentarySlotMaira, phrase, priority: 1 );
		}
	}

	/// <summary>
	/// The 60 Hz FFB producer — called from Simulator.OnTelemetryData (telemetry thread) each time a telemetry
	/// frame with its six 360 Hz torque samples arrives. Runs the whole FFB graph six times at the fixed 360 Hz
	/// tick on the raw samples (no input interpolation), applies fade per sample, and publishes the processed
	/// samples to UpdatePlayout through the _stagedSeq seqlock.
	/// </summary>
	public void ProcessTelemetryFrame()
	{
		var app = App.Instance!;

		try
		{
			// easy reference to settings

			var settings = DataContext.DataContext.Instance.Settings;

			// snapshot the volatile live engine once for the whole frame (it may be swapped from the UI thread)

			var engine = _liveEngine;

			// consume the one-shot triggers even while suspended so they don't go stale-active — the crash/curb
			// pulses are set by Simulator earlier in this same telemetry frame

			var crashProtectionTriggered = ActivateCrashProtection;
			ActivateCrashProtection = false;

			var curbProtectionTriggered = ActivateCurbProtection;
			ActivateCurbProtection = false;

			if ( PlayTestSignal )
			{
				_testSignalTimerMS = TestSignalTimeMS;

				app.Logger.WriteLine( "[RacingWheel] Sending test signal" );

				PlayTestSignal = false;
			}

			// check if we want to fade in or out the steering wheel torque data

			if ( UseSteeringWheelTorqueData != _usingSteeringWheelTorqueData )
			{
				_usingSteeringWheelTorqueData = UseSteeringWheelTorqueData;

				if ( settings.RacingWheelFadeEnabled )
				{
					if ( _usingSteeringWheelTorqueData )
					{
						app.Logger.WriteLine( "[RacingWheel] Requesting fade in of steering wheel torque data" );

						_fadeTimerMS = FadeInTimeMS;
					}
					else
					{
						app.Logger.WriteLine( "[RacingWheel] Requesting fade out of steering wheel torque data" );

						_fadeTimerMS = FadeOutTimeMS;
					}
				}
			}

			// the playout thread owns the device state — while force feedback is suspended there is nothing to
			// process or publish

			if ( !_ffbActive )
			{
				return;
			}

			// check if we want to auto set max force

			if ( AutoSetMaxForce )
			{
				AutoSetMaxForce = false;
				ClearPeakTorque = true;

				settings.RacingWheelMaxForce = _autoTorque;

				app.Logger.WriteLine( $"[RacingWheel] Max force auto set to {_autoTorque}" );
			}

			// check if we want to clear the peak torque

			if ( ClearPeakTorque )
			{
				_peakTorque = 0f;

				ClearPeakTorque = false;
			}

			var steeringWheelTorque_ST = app.Simulator.SteeringWheelTorque_ST;

			var steeringWheelTorque60Hz = _usingSteeringWheelTorqueData ? steeringWheelTorque_ST[ 5 ] : 0f;

			// run the FFB graph engine over the six raw 360 Hz samples at the fixed 360 Hz tick, applying fade per
			// sample, and capture the results into the burst arrays

			var maxForce = settings.RacingWheelMaxForce;

			var trackPeakTorque = app.Simulator.IsOnTrack && ( app.Simulator.PlayerTrackSurface == IRSDKSharper.IRacingSdkEnum.TrkLoc.OnTrack ) && ( app.Simulator.PlayerTrackSurfaceMaterial >= IRSDKSharper.IRacingSdkEnum.TrkSurf.Asphalt1Material ) && ( app.Simulator.PlayerTrackSurfaceMaterial <= IRSDKSharper.IRacingSdkEnum.TrkSurf.RacingDirt2Material );

			// snapshot the frame-constant context inputs once — only the raw torque sample, the LFE magnitude,
			// and the sample-0 protection pulses vary inside the burst loop

			var frameContext = new FrameContext( app, steeringWheelTorque60Hz, maxForce, _usingSteeringWheelTorqueData );

#if DEBUG
			var burstStartTimestamp = Stopwatch.GetTimestamp();
#endif

			for ( var sampleIndex = 0; sampleIndex < Simulator.SamplesPerFrame360Hz; sampleIndex++ )
			{
				var steeringWheelTorque360Hz = _usingSteeringWheelTorqueData ? steeringWheelTorque_ST[ sampleIndex ] : 0f;

				// test signal generator (its own vibration contribution; added to the vibration bus after the engine)

				var testSignalTorque = 0f;

				if ( _testSignalTimerMS > 0f )
				{
					_testSignalTimerMS -= FFBTickContext.TickDeltaMilliseconds;

					testSignalTorque += MathF.Cos( _testSignalTimerMS * MathF.Tau / 20f ) * MathF.Sin( _testSignalTimerMS * MathF.Tau / TestSignalTimeMS * 2f ) * 0.2f;
				}

				// update peak torque

				if ( trackPeakTorque )
				{
					_peakTorque = MathF.Max( _peakTorque, MathZ.Lerp( _peakTorque, MathF.Abs( steeringWheelTorque360Hz ), PeakTorqueLerpAlpha ) );
				}

				// grab the next LFE magnitude, fed into the tick context

				var inputLFEMagnitude = app.LFE.GetNextMagnitude( FFBTickContext.TickDeltaMilliseconds );

				// build the per-tick context and evaluate the whole FFB graph (algorithm + effects + vibration
				// generators) — the one-shot protection pulses fire on the first sample of the frame only

				var tickContext = BuildTickContext( in frameContext, steeringWheelTorque360Hz, inputLFEMagnitude, crashProtectionTriggered && ( sampleIndex == 0 ), curbProtectionTriggered && ( sampleIndex == 0 ), sampleIndex );

				engine.Process( in tickContext );

				// engine outputs: normalized main bus (post output curve/limiter) + normalized vibration bus, plus
				// the separately-computed test signal contribution

				var outputTorque = engine.MainOutput;

				var vibrationTorque = engine.VibrationOutput + testSignalTorque;

				// apply vibration effects and fade (vibration effects not played while fading out)

				if ( _fadeTimerMS > 0f )
				{
					if ( _usingSteeringWheelTorqueData )
					{
						outputTorque += vibrationTorque;

						outputTorque *= 1f - ( _fadeTimerMS / FadeInTimeMS );
					}
					else
					{
						outputTorque = _lastUnfadedOutputTorque * ( _fadeTimerMS / FadeOutTimeMS );
					}

					_fadeTimerMS -= FFBTickContext.TickDeltaMilliseconds;
				}
				else
				{
					_lastUnfadedOutputTorque = outputTorque;

					outputTorque += vibrationTorque;
				}

				_burstOutputTorque[ sampleIndex ] = outputTorque;
				_burstInputTorque[ sampleIndex ] = steeringWheelTorque360Hz / maxForce;
				_burstLFEMagnitude[ sampleIndex ] = inputLFEMagnitude;

				// update recording data

				app.RecordingManager.AddRecordingData( in tickContext, steeringWheelTorque60Hz );
			}

#if DEBUG
			var burstEndTimestamp = Stopwatch.GetTimestamp();

			_burstPerfStats.Update( burstEndTimestamp - burstStartTimestamp, burstEndTimestamp );
#endif

			// update auto torque

			_autoTorque = _peakTorque * settings.RacingWheelWheelForce / settings.RacingWheelAutoTarget;

			// fire the protection chat/voice announcements on the rising edge of each protection becoming active
			// (moved out of the modules; the message-enable settings stay global)

			if ( engine.CrashProtectionActive && !_lastCrashProtectionActive )
			{
				if ( settings.RacingWheelCrashProtectionMessagesEnabled )
				{
					SendChatMessage( null, "CrashProtectionActivated" );
				}

				SpeakMairaAnnouncement( "MairaCrashProtectionActive" );

				app.Logger.WriteLine( $"[RacingWheel] Crash protection activated (force reduction {engine.CrashProtectionForceReduction * 100f:F0}%, duration {engine.CrashProtectionDuration:F1} s)" );
			}
			else if ( !engine.CrashProtectionActive && _lastCrashProtectionActive )
			{
				app.Logger.WriteLine( "[RacingWheel] Crash protection deactivated" );
			}

			_lastCrashProtectionActive = engine.CrashProtectionActive;

			if ( engine.CurbProtectionActive && !_lastCurbProtectionActive )
			{
				if ( settings.RacingWheelCurbProtectionMessagesEnabled )
				{
					SendChatMessage( null, "CurbProtectionActivated" );
				}

				SpeakMairaAnnouncement( "MairaCurbProtectionActive" );
			}

			_lastCurbProtectionActive = engine.CurbProtectionActive;

			// publish the processed block to the playout thread (seqlock: odd = writing, even = published)

			var seq = _stagedSeq;

			Volatile.Write( ref _stagedSeq, seq + 1 );

			Interlocked.MemoryBarrier();

			for ( var i = 0; i < Simulator.SamplesPerFrame360Hz; i++ )
			{
				_stagedOutputTorque[ i ] = _burstOutputTorque[ i ];
				_stagedInputTorque[ i ] = _burstInputTorque[ i ];
				_stagedLFEMagnitude[ i ] = _burstLFEMagnitude[ i ];
			}

			_stagedInputTorque60Hz = steeringWheelTorque60Hz / maxForce;

			Volatile.Write( ref _stagedSeq, seq + 2 );

			// background flash color — clipping (red) trumps crash protection (orange) trumps curb protection (yellow)

			var clearColor = 0u;

			if ( engine.CurbProtectionActive )
			{
				clearColor = 0xFF606000;
			}

			if ( engine.CrashProtectionActive )
			{
				clearColor = 0xFF40260C;
			}

			for ( var i = 0; i < Simulator.SamplesPerFrame360Hz; i++ )
			{
				if ( MathF.Abs( _burstOutputTorque[ i ] ) >= 0.99f )
				{
					clearColor = 0xFF600000;

					break;
				}
			}

			app.Graph.SetClearColor( clearColor );
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[RacingWheel] Exception caught in ProcessTelemetryFrame: {exception.Message.Trim()}" );

			_producerFaulted = true;
		}
	}

	/// <summary>
	/// The 500 Hz playout — called from the multimedia timer worker thread. Owns the force feedback device state
	/// (suspend/resume/re-init), consumes the processed 360 Hz samples published by ProcessTelemetryFrame, and
	/// Hermite-interpolates them up to the wheel update rate. If the next telemetry frame is late the playout
	/// clock clamps at the newest sample (hold-last) and the underrun is counted.
	/// </summary>
	public void UpdatePlayout( float deltaMilliseconds )
	{
		var app = App.Instance!;

		try
		{
#if DEBUG
			var playoutStartTimestamp = Stopwatch.GetTimestamp();
#endif

			// easy reference to settings

			var settings = DataContext.DataContext.Instance.Settings;

			// a fault in the telemetry-thread producer restarts force feedback the same way a fault here does

			if ( _producerFaulted )
			{
				_producerFaulted = false;

				_unsuspendTimerMS = UnsuspendTimeMS;
			}

			// check if we want to suspend or unsuspend force feedback

			if ( SuspendForceFeedback != _isSuspended )
			{
				_isSuspended = SuspendForceFeedback;

				if ( _isSuspended )
				{
					app.Logger.WriteLine( "[RacingWheel] Requesting suspend of force feedback" );

					_unsuspendTimerMS = UnsuspendTimeMS;
				}
				else
				{
					app.Logger.WriteLine( "[RacingWheel] Requesting resumption of force feedback" );
				}

				_racingWheelPage.UpdateSteeringDeviceSection();
			}

			// check if we want to reset the racing wheel device

			if ( ResetForceFeedback )
			{
				ResetForceFeedback = false;

				if ( NextRacingWheelGuid == null )
				{
					NextRacingWheelGuid = _currentRacingWheelGuid;

					app.Logger.WriteLine( "[RacingWheel] Requesting reset of force feedback device" );
				}
			}

			// if power button is off, or suspend is requested, or unsuspend counter is still counting down, or if sim mode is not "full", then suspend the racing wheel force feedback

			if ( !settings.RacingWheelEnableForceFeedback || _isSuspended || ( _unsuspendTimerMS > 0f ) || ( app.Simulator.SimMode != "full" ) )
			{
				if ( _currentRacingWheelGuid != null )
				{
					app.Logger.WriteLine( "[RacingWheel] Suspending racing wheel force feedback" );

					app.DirectInput.ShutdownForceFeedback();

					_racingWheelPage.UpdateSteeringDeviceSection();

					NextRacingWheelGuid = _currentRacingWheelGuid;

					_currentRacingWheelGuid = null;
				}

				// stop the producer and clear the playout state so nothing stale plays when force feedback resumes
				// (the producer publishes nothing while _ffbActive is false, so syncing the sequence here skips any
				// block it may have been mid-publish when we suspended)

				_ffbActive = false;

				Array.Clear( _playoutTorqueWindow );
				Array.Clear( _playoutInputTorque );
				Array.Clear( _playoutLFEMagnitude );

				_playoutInputTorque60Hz = 0f;
				_playoutElapsedMilliseconds = 0f;
				_playoutUnderrunLatched = false;
				_lastConsumedSeq = Volatile.Read( ref _stagedSeq );

				_unsuspendTimerMS -= deltaMilliseconds;

				return;
			}

			// if next racing wheel guid is set then re-initialize force feedback

			if ( NextRacingWheelGuid != null )
			{
				if ( _currentRacingWheelGuid != null )
				{
					app.Logger.WriteLine( "[RacingWheel] Uninitializing racing wheel force feedback" );

					app.DirectInput.ShutdownForceFeedback();

					_racingWheelPage.UpdateSteeringDeviceSection();

					_currentRacingWheelGuid = null;
				}

				if ( NextRacingWheelGuid != Guid.Empty )
				{
					app.Logger.WriteLine( "[RacingWheel] Initializing racing wheel force feedback" );

					_currentRacingWheelGuid = NextRacingWheelGuid;

					NextRacingWheelGuid = null;

					app.DirectInput.InitializeForceFeedback( (Guid) _currentRacingWheelGuid );

					_racingWheelPage.UpdateSteeringDeviceSection();
				}

			}

			// let the telemetry-thread producer run

			_ffbActive = true;

			// update the playout clock

			_playoutElapsedMilliseconds += deltaMilliseconds;

			// consume a newly published block of processed 360 Hz samples (seqlock — see the field comments); a
			// torn copy is discarded and simply plays as up to 2 ms of held torque

			var stagedSeq = Volatile.Read( ref _stagedSeq );

			if ( ( stagedSeq != _lastConsumedSeq ) && ( ( stagedSeq & 1 ) == 0 ) )
			{
				for ( var i = 0; i < Simulator.SamplesPerFrame360Hz; i++ )
				{
					_consumeOutputTorque[ i ] = _stagedOutputTorque[ i ];
					_consumeInputTorque[ i ] = _stagedInputTorque[ i ];
					_consumeLFEMagnitude[ i ] = _stagedLFEMagnitude[ i ];
				}

				var inputTorque60Hz = _stagedInputTorque60Hz;

				Interlocked.MemoryBarrier();

				if ( Volatile.Read( ref _stagedSeq ) == stagedSeq )
				{
					_playoutTorqueWindow[ 0 ] = _playoutTorqueWindow[ 7 ];
					_playoutTorqueWindow[ 1 ] = _consumeOutputTorque[ 0 ];
					_playoutTorqueWindow[ 2 ] = _consumeOutputTorque[ 1 ];
					_playoutTorqueWindow[ 3 ] = _consumeOutputTorque[ 2 ];
					_playoutTorqueWindow[ 4 ] = _consumeOutputTorque[ 3 ];
					_playoutTorqueWindow[ 5 ] = _consumeOutputTorque[ 4 ];
					_playoutTorqueWindow[ 6 ] = _consumeOutputTorque[ 5 ];
					_playoutTorqueWindow[ 7 ] = _consumeOutputTorque[ 5 ];

					for ( var i = 0; i < Simulator.SamplesPerFrame360Hz; i++ )
					{
						_playoutInputTorque[ i ] = _consumeInputTorque[ i ];
						_playoutLFEMagnitude[ i ] = _consumeLFEMagnitude[ i ];
					}

					_playoutInputTorque60Hz = inputTorque60Hz;

					_playoutElapsedMilliseconds = 0f;
					_playoutUnderrunLatched = false;

					_lastConsumedSeq = stagedSeq;
				}
				else
				{
					PlayoutTornHandoffCount++;
				}
			}

			// get the next output torque sample — Hermite-interpolate the processed 360 Hz samples up to the wheel
			// update rate; the indices clamp at the end of the window, holding the newest sample if the next
			// telemetry frame is late

			var playoutIndex = 1f + ( _playoutElapsedMilliseconds * 360f / 1000f );

			var i1 = Math.Min( PlayoutWindowLastIndex, (int) MathF.Truncate( playoutIndex ) );
			var i2 = Math.Min( PlayoutWindowLastIndex, i1 + 1 );
			var i3 = Math.Min( PlayoutWindowLastIndex, i2 + 1 );
			var i0 = Math.Max( 0, i1 - 1 );

			var t = MathF.Min( 1f, playoutIndex - i1 );

			var m0 = _playoutTorqueWindow[ i0 ];
			var m1 = _playoutTorqueWindow[ i1 ];
			var m2 = _playoutTorqueWindow[ i2 ];
			var m3 = _playoutTorqueWindow[ i3 ];

			var outputTorque = MathZ.InterpolateHermite( m0, m1, m2, m3, t );

			// underrun tracking — count ticks where the playout clock ran meaningfully past the end of the frame

			_underrunLogTimerMS = MathF.Max( 0f, _underrunLogTimerMS - deltaMilliseconds );

			if ( _playoutElapsedMilliseconds > ( Simulator.SamplesPerFrame360Hz * FFBTickContext.TickDeltaMilliseconds ) + PlayoutUnderrunToleranceMS )
			{
				PlayoutUnderrunTickCount++;

				if ( !_playoutUnderrunLatched )
				{
					_playoutUnderrunLatched = true;

					PlayoutUnderrunEpisodeCount++;

					if ( _underrunLogTimerMS <= 0f )
					{
						_underrunLogTimerMS = 1000f;

						app.Logger.WriteLine( $"[RacingWheel] Playout underrun (late telemetry frame) — episode {PlayoutUnderrunEpisodeCount}" );
					}
				}
			}

			// update output torque for telemetry

			_outputTorque = outputTorque;

			// update force feedback torque

			app.DirectInput.UpdateForceFeedbackEffect( outputTorque );

			// update graph (input traces show the staged raw samples nearest the current playout position, so the
			// scrolling display keeps its full 500 Hz density)

			var displaySampleIndex = Math.Min( Simulator.SamplesPerFrame360Hz - 1, (int) ( _playoutElapsedMilliseconds * 360f / 1000f ) );

			app.Graph.UpdateLayer( Graph.LayerIndex.InputTorque60Hz, _playoutInputTorque60Hz );
			app.Graph.UpdateLayer( Graph.LayerIndex.InputTorque, _playoutInputTorque[ displaySampleIndex ] );
			app.Graph.UpdateLayer( Graph.LayerIndex.InputLFE, _playoutLFEMagnitude[ displaySampleIndex ] );
			app.Graph.UpdateLayer( Graph.LayerIndex.OutputTorque, outputTorque );

#if DEBUG
			var playoutEndTimestamp = Stopwatch.GetTimestamp();

			_playoutPerfStats.Update( playoutEndTimestamp - playoutStartTimestamp, playoutEndTimestamp );
#endif
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[RacingWheel] Exception caught: {exception.Message.Trim()}" );

			_unsuspendTimerMS = UnsuspendTimeMS;
		}
	}

	/// <summary>
	/// Frame-constant inputs for the 6-sample burst, snapshotted once per telemetry frame. Everything here is
	/// 60 Hz data that cannot change between the six samples of one frame, so re-reading it per sample was
	/// redundant work.
	/// </summary>
	private readonly struct FrameContext
	{
		public readonly float Torque60Hz;
		public readonly FFBTorqueFrame TorqueFrame;
		public readonly float MaxForce;
		public readonly float WheelPosition;
		public readonly float WheelVelocity;
		public readonly float UndersteerEffect;
		public readonly float OversteerEffect;
		public readonly float SeatOfPantsEffect;
		public readonly float SkidSlip;
		public readonly float RPM;
		public readonly float ShiftRPM;
		public readonly float RedlineRPM;
		public readonly bool EngineRunning;
		public readonly int Gear;
		public readonly int NumForwardGears;
		public readonly bool ABSActive;
		public readonly bool IsOnTrack;
		public readonly bool UsingTorqueData;
		public readonly float VelocityMS;
		public readonly float VelocityY;
		public readonly float SteeringWheelAngle;
		public readonly float SteeringWheelAngleMax;
		public readonly float SteeringWheelVelocity;
		public readonly float PitchRate;
		public readonly float WheelForce;

		public FrameContext( App app, float torque60Hz, float maxForce, bool usingTorqueData )
		{
			var simulator = app.Simulator;
			var steeringEffects = app.SteeringEffects;

			Torque60Hz = torque60Hz;
			MaxForce = maxForce;

			// the whole raw 360 Hz ST frame (zeros when torque data isn't live — matches the per-tick samples)
			if ( usingTorqueData )
			{
				var steeringWheelTorque_ST = simulator.SteeringWheelTorque_ST;

				for ( var i = 0; i < Simulator.SamplesPerFrame360Hz; i++ )
				{
					TorqueFrame[ i ] = steeringWheelTorque_ST[ i ];
				}
			}

			// Wheel position/velocity normalized to the car's steering lock, derived from iRacing's SteeringWheelAngle/
			// Velocity telemetry (proper radians / rad-per-second). This replaced our own DirectInput axis sampling;
			// normalizing by the car's half-lock also makes these relative to the real steering lock rather than the
			// wheel's fixed rotation range (halfLock is 0 off-car, so the values sit at 0 until in a session).
			var halfLock = simulator.SteeringWheelAngleMax * 0.5f;
			WheelPosition = ( halfLock > 0f ) ? simulator.SteeringWheelAngle / halfLock : 0f;
			WheelVelocity = ( halfLock > 0f ) ? simulator.SteeringWheelVelocity / halfLock : 0f;

			// the per-effect enable switches on the steering effects page gate the corresponding FFB modules: a
			// disabled effect feeds 0, so its force modules pass through and its vibration generators stay silent
			var settings = DataContext.DataContext.Instance.Settings;

			WheelForce = settings.RacingWheelWheelForce;

			UndersteerEffect = settings.SteeringEffectsUndersteerEnabled ? steeringEffects.UndersteerEffect : 0f;
			OversteerEffect = settings.SteeringEffectsOversteerEnabled ? steeringEffects.OversteerEffect : 0f;
			SeatOfPantsEffect = settings.SteeringEffectsSeatOfPantsEnabled ? steeringEffects.SeatOfPantsEffect : 0f;
			SkidSlip = steeringEffects.SkidSlip;
			RPM = simulator.RPM;
			ShiftRPM = simulator.ShiftLightsShiftRPM;
			RedlineRPM = simulator.RedlineRPM;
			EngineRunning = simulator.EngineRunning;
			Gear = simulator.Gear;
			NumForwardGears = simulator.NumForwardGears;
			ABSActive = simulator.BrakeABSactive;
			IsOnTrack = simulator.IsOnTrack;
			UsingTorqueData = usingTorqueData;
			VelocityMS = simulator.Velocity;
			VelocityY = simulator.VelocityY;
			SteeringWheelAngle = simulator.SteeringWheelAngle;
			SteeringWheelAngleMax = simulator.SteeringWheelAngleMax;
			SteeringWheelVelocity = simulator.SteeringWheelVelocity;
			PitchRate = simulator.PitchRate_ST[ Simulator.SamplesPerFrame360Hz - 1 ]; // the frame's newest sample
		}
	}

	/// <summary>
	/// Assembles the per-tick auxiliary input for the FFB graph engine from the frame-constant snapshot plus
	/// the values that vary per 360 Hz sample (raw torque, LFE magnitude, sample-0 protection pulses). No
	/// allocation (the struct is passed by readonly reference into every module).
	/// </summary>
	private static FFBTickContext BuildTickContext( in FrameContext frameContext, float torque360Hz, float lfeMagnitude, bool crashProtectionTriggered, bool curbProtectionTriggered, int sampleIndex )
	{
		return new FFBTickContext(
			deltaMilliseconds: FFBTickContext.TickDeltaMilliseconds,
			sampleIndex: sampleIndex,
			torque60Hz: frameContext.Torque60Hz,
			torque360Hz: torque360Hz,
			torqueFrame: in frameContext.TorqueFrame,
			maxForce: frameContext.MaxForce,
			wheelForce: frameContext.WheelForce,
			lfeMagnitude: lfeMagnitude,
			wheelPosition: frameContext.WheelPosition,
			wheelVelocity: frameContext.WheelVelocity,
			understeerEffect: frameContext.UndersteerEffect,
			oversteerEffect: frameContext.OversteerEffect,
			seatOfPantsEffect: frameContext.SeatOfPantsEffect,
			skidSlip: frameContext.SkidSlip,
			rpm: frameContext.RPM,
			shiftRPM: frameContext.ShiftRPM,
			redlineRPM: frameContext.RedlineRPM,
			engineRunning: frameContext.EngineRunning,
			gear: frameContext.Gear,
			numForwardGears: frameContext.NumForwardGears,
			absActive: frameContext.ABSActive,
			isOnTrack: frameContext.IsOnTrack,
			usingTorqueData: frameContext.UsingTorqueData,
			velocityMS: frameContext.VelocityMS,
			velocityY: frameContext.VelocityY,
			steeringWheelAngle: frameContext.SteeringWheelAngle,
			steeringWheelAngleMax: frameContext.SteeringWheelAngleMax,
			steeringWheelVelocity: frameContext.SteeringWheelVelocity,
			pitchRate: frameContext.PitchRate,
			crashProtectionTriggered: crashProtectionTriggered,
			curbProtectionTriggered: curbProtectionTriggered );
	}

	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter <= 0 )
		{
			_updateCounter = UpdateInterval;

			// shortcut to settings

			var settings = DataContext.DataContext.Instance.Settings;

			// update auto force label

			_racingWheelPage.AutoForce_TextBlock.Text = $"{_autoTorque:F1}{DataContext.DataContext.Instance.Localization[ "TorqueUnits" ]}";

			// update the FFB graph preview: replay the loaded recording through the preview engine (rebuilt from the
			// currently selected graph). Each recorded sample is expanded back into a full tick context, so effects
			// and generators that depend on telemetry (LFE, wheel velocity, steering effects, RPM, ...) work in the
			// preview, and the crash/curb protection pulses are re-derived from the recorded raw telemetry against
			// the protection modules' CURRENT thresholds. The traces tap the module selected in the node editor —
			// red = its input A, green = its input B (dual-input modules only), blue = its output. For the Output
			// module (the default selection) red/green show the two sources and blue shows the final normalized
			// output, like the old whole-graph preview.

			if ( UpdateAlgorithmPreview )
			{
				UpdateAlgorithmPreview = false;

				var recording = app.RecordingManager.Recording;

				// recordings are loaded lazily — kick off the background load and redraw when it lands; the
				// preview renders without the recording in the meantime
				if ( ( recording != null ) && !recording.IsDataLoaded )
				{
					app.RecordingManager.RequestRecordingData( recording );
				}

				// the preview bitmap is one pixel per DRAWN sample — every previewSkip'th recorded sample — so it
				// shrinks as the preview zooms out; resize it when the recording length or the zoom changes
				// (recordings are dynamic-length now); the default width covers the no-recording case
				var previewSkip = Math.Clamp( AlgorithmPreviewSkip, 1, MaxAlgorithmPreviewSkip );

				var recordingSampleCount = recording?.Data?.Count ?? 0;

				var desiredPreviewWidth = ( recordingSampleCount > 0 ) ? ( recordingSampleCount + previewSkip - 1 ) / previewSkip : DefaultAlgorithmPreviewWidth;

				if ( desiredPreviewWidth != _algorithmPreviewGraphBase.BitmapWidth )
				{
					_racingWheelPage.AlgorithmPreview_Image.Width = desiredPreviewWidth;

					_algorithmPreviewGraphBase.Initialize( _racingWheelPage.AlgorithmPreview_Image );

					_racingWheelPage.OnPreviewImageResized();
				}

				_algorithmPreviewGraphBase.Reset();

				if ( settings.RacingWheelFFBGraphs.TryGetValue( settings.RacingWheelSelectedFFBGraphName, out var previewGraph ) )
				{
					_previewEngine.Rebuild( previewGraph );
				}

				// Rebuild recreated the module instances, so re-apply the session-only test toggles from the
				// editor — a module under test shows its effect in the preview trace too
				foreach ( var moduleViewModel in DataContext.DataContext.Instance.RacingWheelGraphViewModel.Modules )
				{
					if ( moduleViewModel.IsTestActive )
					{
						_previewEngine.SetTestActive( moduleViewModel.ModuleId, true );
					}
				}

				_previewEngine.ResetState();

				var maxForce = settings.RacingWheelMaxForce;

				// resolve the previewed module's taps once, before the replay loop — the preview module normally
				// follows the selection, but a right-click can lock it to a different node

				var previewModule = DataContext.DataContext.Instance.RacingWheelGraphViewModel.PreviewModule;

				var previewIndex = previewModule != null ? _previewEngine.IndexOf( previewModule.ModuleId ) : -1;
				var previewIsOutput = ( previewModule == null ) || previewModule.IsOutput || ( previewIndex < 0 );

				int input1Index = -1;
				int input2Index = -1;
				int outputIndex;

				if ( previewIsOutput )
				{
					input1Index = _previewEngine.IndexOf( FFBGraph.Source360ModuleId );
					input2Index = -1;
					outputIndex = -1; // final output is already normalized — read the engine's output buses directly
				}
				else if ( previewModule!.SignalInputCount == 0 )
				{
					// a source or vibration generator module has no inputs — show just its own waveform
					outputIndex = previewIndex;
				}
				else
				{
					(input1Index, input2Index) = _previewEngine.GetResolvedInputs( previewIndex );

					if ( previewModule.SignalInputCount < 2 )
					{
						// single-input module — no second input trace
						input2Index = -1;
					}

					outputIndex = previewIndex;
				}

				// the main bus is in Nm until the Output module normalizes it, so tapped signals are scaled by max
				// force for display — except vibration generators, whose output is already normalized
				var previewSignalScale = ( ( previewModule != null ) && previewModule.IsGenerator ) ? 1f : maxForce;

				// protection trigger thresholds as published by the preview graph's protection modules on Rebuild —
				// the replay applies them to the recorded raw telemetry the same way Simulator does live
				var crashLongGForceThreshold = _previewEngine.CrashLongGForceThreshold;
				var crashLatGForceThreshold = _previewEngine.CrashLatGForceThreshold;
				var curbShockVelocityThreshold = _previewEngine.CurbShockVelocityThreshold;

				var previousOutputValue = 0f;
				var isFirstSample = true;

				var previewTorqueFrame = new FFBTorqueFrame();
				var previewFramePitchRate = 0f;

				if ( ( recording?.Data != null ) && ( recording.Data.Count > 0 ) )
				{
					for ( var x = 0; x < recording.Data.Count; x++ )
					{
						var recordingData = recording.Data[ x ];

						var sampleIndex = x % FFBTickContext.SamplesPerFrame;

						// reassemble the frame's six raw 360 Hz samples (what the live burst sees in one go) and
						// the frame's newest pitch-rate sample (what the live FrameContext carries)
						if ( ( sampleIndex == 0 ) || isFirstSample )
						{
							var frameStart = x - sampleIndex;

							for ( var i = 0; i < FFBTickContext.SamplesPerFrame; i++ )
							{
								var index = Math.Min( frameStart + i, recording.Data.Count - 1 );

								previewTorqueFrame[ i ] = recording.Data[ index ].InputTorque360Hz;
							}

							previewFramePitchRate = recording.Data[ Math.Min( frameStart + FFBTickContext.SamplesPerFrame - 1, recording.Data.Count - 1 ) ].PitchRate;
						}

						var crashProtectionTriggered = ( ( crashLongGForceThreshold < 20f ) && ( recordingData.LongitudinalGForce >= crashLongGForceThreshold ) )
							|| ( ( crashLatGForceThreshold < 20f ) && ( recordingData.LateralGForce >= crashLatGForceThreshold ) );

						var curbProtectionTriggered = ( curbShockVelocityThreshold > 0f ) && ( recordingData.MaxShockVelocity >= curbShockVelocityThreshold );

						var previewContext = FFBTickContext.FromRecording( recordingData, in previewTorqueFrame, previewFramePitchRate, maxForce, crashProtectionTriggered, curbProtectionTriggered, sampleIndex );

						_previewEngine.Process( in previewContext );

						// zoomed out, only every previewSkip'th sample lands a bitmap column — the engine still
						// ran for the skipped ones, so module state (filters, prediction, protection timers)
						// stays sample-accurate at every zoom level
						if ( ( x % previewSkip ) != 0 )
						{
							continue;
						}

						// draw the output value first because it fill the space below the line with black
						// (the Output node trace matches what the wheel actually plays: main bus + vibration bus)
						var outputValue = outputIndex >= 0 ? _previewEngine.GetSignal( outputIndex ) / previewSignalScale : ( _previewEngine.MainOutput + _previewEngine.VibrationOutput );

						if ( isFirstSample )
						{
							previousOutputValue = outputValue;
							isFirstSample = false;
						}

						// the output renders as a connected line over the solid-filled inputs
						_algorithmPreviewGraphBase.UpdateLine( previousOutputValue, outputValue, 1f, 1f, 1f );

						previousOutputValue = outputValue;

						if ( input1Index >= 0 )
						{
							var inputValue = _previewEngine.GetSignal( input1Index ) / maxForce;

							_algorithmPreviewGraphBase.UpdateSolidFill( inputValue, 0.5f, 0f, 0f );
						}

						if ( input2Index >= 0 )
						{
							var inputValue = _previewEngine.GetSignal( input2Index ) / maxForce;

							_algorithmPreviewGraphBase.UpdateSolidFill( inputValue, 0f, 0.5f, 0f );
						}

						// background flash color, mirroring the live graph — clipping (red, only meaningful when
						// the Output module is previewed) trumps crash protection (orange) trumps curb protection
						// (yellow); the protection flags come from the preview engine, so they honor the current
						// module thresholds and durations over the recorded telemetry
						var clearColor = 0u;

						if ( _previewEngine.CurbProtectionActive )
						{
							clearColor = 0xFF606000;
						}

						if ( _previewEngine.CrashProtectionActive )
						{
							clearColor = 0xFF40260C;
						}

						if ( previewIsOutput && ( MathF.Abs( outputValue ) >= 0.99f ) )
						{
							clearColor = 0xFF600000;
						}

						_algorithmPreviewGraphBase.SetClearColor( clearColor );

						_algorithmPreviewGraphBase.FinishUpdates();
					}
				}
				else
				{
					// no recording — just paint the empty grid across the default-width bitmap
					for ( var x = 0; x < _algorithmPreviewGraphBase.BitmapWidth; x++ )
					{
						_algorithmPreviewGraphBase.FinishUpdates();
					}
				}

				_algorithmPreviewGraphBase.WritePixels();
			}

			// update record button

			_racingWheelPage.Record_MairaMappableButton.Disabled = !app.Simulator.IsOnTrack;
			_racingWheelPage.Record_MairaMappableButton.Blink = app.RecordingManager.IsRecording;

			// generator (vibration) tests shake the physical wheel, which needs live FFB — gate their test
			// buttons on the same on-track state as the record button

			DataContext.DataContext.Instance.RacingWheelGraphViewModel.NotifyIsOnTrackChanged( app.Simulator.IsOnTrack );

			// suspend racing wheel force feedback if iracing ffb is enabled or we are calibrating

			SuspendForceFeedback = !app.Simulator.IsConnected || ( app.Simulator.SteeringFFBEnabled && !settings.RacingWheelAlwaysEnableFFB ) || app.SteeringEffects.IsCalibrating;

			/*
			app.Debug.Label_1 = $"FadingIsActive: {FadingIsActive}";
			app.Debug.Label_2 = $"_fadeTimerMS: {_fadeTimerMS:F0} ms";
			app.Debug.Label_4 = $"_outputTorque: {_outputTorque * 100f:F0}%";
			*/
		}
	}

}
