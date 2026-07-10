
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

	public enum PredictionMode
	{
		Disabled,
		PredictK1,
		PredictK2
	}

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
	private const int MaxSteeringWheelTorque360HzIndex = Simulator.SamplesPerFrame360Hz + 1;

	private const float UnsuspendTimeMS = 1000f;
	private const float FadeInTimeMS = 2000f;
	private const float FadeOutTimeMS = 750f;
	private const float TestSignalTimeMS = 2000f;

	// Commentary voice slot used for MAIRA system announcements (matches VoiceSlotSettings.CreateDefaults order).
	private const int CommentarySlotMaira = 5;

	private Guid? _currentRacingWheelGuid = null;

	private bool _isSuspended = true;
	private bool _usingSteeringWheelTorqueData = false;

	public Guid? NextRacingWheelGuid { private get; set; } = null;
	public bool SuspendForceFeedback { get; private set; } = true; // true if we want to suspend FFB (for various reasons)
	public bool ResetForceFeedback { private get; set; } = false; // set to true manually (via reset button)
	public bool UseSteeringWheelTorqueData { private get; set; } = false; // false if simulator is disconnected or if driver is not on track
	public bool UpdateSteeringWheelTorqueBuffer { private get; set; } = false; // true when simulator has new torque data to be copied
	public bool ActivateCrashProtection { private get; set; } = false; // set to true to activate crash protection
	public bool ActivateCurbProtection { private get; set; } = false; // set to true to activate curb protection
	public bool PlayTestSignal { private get; set; } = false; // set to true manually (via test button)
	public bool ClearPeakTorque { private get; set; } = false; // set to clear peak torque
	public bool AutoSetMaxForce { private get; set; } = false; // set to auto-set the max force setting
	public bool UpdateAlgorithmPreview { private get; set; } = true; // set to update the algorithm preview

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

	private readonly float[] _steeringWheelTorque360Hz = new float[ Simulator.SamplesPerFrame360Hz + 2 ];

	private float _outputTorque = 0f;
	private float _peakTorque = 0f;
	private float _autoTorque = 0f;

	private float _lastUnfadedOutputTorque = 0f;

	private float _elapsedMilliseconds = 0f;

	// Rising-edge trackers for the protection chat/voice announcements (side effects stay out of the modules).
	private bool _lastCrashProtectionActive = false;
	private bool _lastCurbProtectionActive = false;

	private readonly GraphBase _algorithmPreviewGraphBase = new();

	// Milestone 3: the modular FFB graph now drives all wheel processing. _liveEngine (volatile) is evaluated by
	// this 360 Hz worker-thread Update and rebuilt/swapped on the UI thread on graph/context changes (the reader
	// picks up the new reference next tick, same one-tick tolerance as the old _lastAlgorithm reset).
	// _previewEngine is driven only by Tick (dispatcher) over a recording for the editor preview.
	private volatile FFBGraphEngine _liveEngine = new();
	private readonly FFBGraphEngine _previewEngine = new();

	private int _updateCounter = UpdateInterval + 4;

#if DEBUG

	private struct PredictorSample
	{
		public int TickCount { get; set; }

		public float InputFFBSample { get; set; }
		public float WheelVelocity { get; set; }
		public PredictionMode PredictionMode { get; set; }
		public float PredictionBlend { get; set; }
		public float PredictedValue { get; set; }
		public bool DeltaClamped { get; set; }
		public float OutputFFBSample { get; set; }
	}

	private float _lastLapDistPct = 0f;
	private int _lapNumber = 0;
	private readonly PredictorSample[] _predictorSampleArray = new PredictorSample[ 65536 ]; // enough for 18+ minutes a lap at 60 Hz
	private int _predictorSampleCount = 0;

#endif

	private readonly RlsWheelVelocityPredictor _ffbPredictorK1 = new( horizon: 1 );
	private readonly RlsWheelVelocityPredictor _ffbPredictorK2 = new( horizon: 2 );

	private float _predictedSteeringWheelTorque60Hz = 0f;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[RacingWheel] Initialize >>>" );

		app.Graph.SetLayerColors( Graph.LayerIndex.InputTorque60Hz, 1f, 0f, 0f );
		app.Graph.SetLayerColors( Graph.LayerIndex.InputTorque, 1f, 0f, 0f );
		app.Graph.SetLayerColors( Graph.LayerIndex.InputLFE, 0f, 0f, 1f );
		app.Graph.SetLayerColors( Graph.LayerIndex.OutputTorque, 0f, 1f, 1f );

		_algorithmPreviewGraphBase.Initialize( MainWindow._racingWheelPage.AlgorithmPreview_Image );

#if DEBUG

		for ( var i = 0; i < _predictorSampleArray.Length; i++ )
		{
			_predictorSampleArray[ i ] = new PredictorSample();
		}

#endif

		app.Logger.WriteLine( "[RacingWheel] <<< Initialize" );
	}

	public float GetCurrentAutoTorque()
	{
		return _autoTorque;
	}

	/// <summary>
	/// Rebuild the live FFB graph engine from the currently selected graph and swap the volatile reference.
	/// UI thread only (structure edits / graph selection / per-context reload). Tolerant of a missing selection
	/// or empty graph dictionary (e.g. during settings load before the built-ins are ensured) — leaves the
	/// current engine in place. Milestone 2: the engine is kept valid but does not yet drive FFB output.
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

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Update( float deltaMilliseconds )
	{
		var app = App.Instance!;

		try
		{
			// easy reference to settings

			var settings = DataContext.DataContext.Instance.Settings;

			// snapshot the volatile live engine once for the whole tick (it may be swapped from the UI thread)

			var engine = _liveEngine;

			// test signal generator (its own vibration contribution; added to the vibration bus after the engine).
			// Its timer advances every tick as before (even while suspended, then discarded by the early return).

			var testSignalTorque = 0f;

			if ( PlayTestSignal )
			{
				_testSignalTimerMS = TestSignalTimeMS;

				app.Logger.WriteLine( "[RacingWheel] Sending test signal" );

				PlayTestSignal = false;
			}

			if ( _testSignalTimerMS > 0f )
			{
				_testSignalTimerMS -= deltaMilliseconds;

				testSignalTorque += MathF.Cos( _testSignalTimerMS * MathF.Tau / 20f ) * MathF.Sin( _testSignalTimerMS * MathF.Tau / TestSignalTimeMS * 2f ) * 0.2f;
			}

			// (understeer/oversteer/seat-of-pants/shift-RPM/gear-change/ABS vibrations are now generator modules
			// evaluated inside the FFB graph engine below)

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

				_ffbPredictorK1.Reset();
				_ffbPredictorK2.Reset();
			}

			// check if we want to auto set max force

			if ( AutoSetMaxForce )
			{
				AutoSetMaxForce = false;
				ClearPeakTorque = true;

				settings.RacingWheelMaxForce = _autoTorque;

				app.Logger.WriteLine( $"[RacingWheel] Max force auto set to {_autoTorque}" );
			}

			// update elapsed milliseconds

			_elapsedMilliseconds += deltaMilliseconds;

			// update steering wheel torque data

			if ( UpdateSteeringWheelTorqueBuffer )
			{
				if ( _usingSteeringWheelTorqueData )
				{
					_steeringWheelTorque360Hz[ 0 ] = _steeringWheelTorque360Hz[ 7 ];
					_steeringWheelTorque360Hz[ 1 ] = app.Simulator.SteeringWheelTorque_ST[ 0 ];
					_steeringWheelTorque360Hz[ 2 ] = app.Simulator.SteeringWheelTorque_ST[ 1 ];
					_steeringWheelTorque360Hz[ 3 ] = app.Simulator.SteeringWheelTorque_ST[ 2 ];
					_steeringWheelTorque360Hz[ 4 ] = app.Simulator.SteeringWheelTorque_ST[ 3 ];
					_steeringWheelTorque360Hz[ 5 ] = app.Simulator.SteeringWheelTorque_ST[ 4 ];
					_steeringWheelTorque360Hz[ 6 ] = app.Simulator.SteeringWheelTorque_ST[ 5 ];
					_steeringWheelTorque360Hz[ 7 ] = app.Simulator.SteeringWheelTorque_ST[ 5 ];

					// Run 60 Hz predictor (mode/blend now come from the live engine's Source60 module)

					var predictedValue = engine.PredictionMode switch
					{
						PredictionMode.PredictK1 => _ffbPredictorK1.Step( _steeringWheelTorque360Hz[ 6 ], app.DirectInput.ForceFeedbackWheelVelocity ),
						PredictionMode.PredictK2 => _ffbPredictorK2.Step( _steeringWheelTorque360Hz[ 6 ], app.DirectInput.ForceFeedbackWheelVelocity ),
						_ => _steeringWheelTorque360Hz[ 6 ],
					};

					var unclampedDelta = predictedValue - _steeringWheelTorque360Hz[ 6 ];
					var clampedDelta = Math.Clamp( unclampedDelta, -0.5f, 0.5f );

					_predictedSteeringWheelTorque60Hz = MathZ.Lerp( _steeringWheelTorque360Hz[ 6 ], _steeringWheelTorque360Hz[ 6 ] + clampedDelta, engine.PredictionBlend );

#if DEBUG

					if ( ( _lastLapDistPct > 0.95f ) && ( app.Simulator.LapDistPct < 0.05f ) )
					{
						_lapNumber++;

						var lapNumber = _lapNumber;

						var snapshot = _predictorSampleArray.AsSpan( 0, _predictorSampleCount ).ToArray();

						_predictorSampleCount = 0;

						_ = Task.Run( async () =>
						{
							var path = Path.Combine( App.DocumentsFolder, "Predictor Data", $"Lap-{lapNumber}.csv" );

							try
							{
								await DumpLapAsync( snapshot, path );
							}
							catch ( Exception exception )
							{
								app.Logger.WriteLine( $"[RacingWheel] Exception while dumping predictor data: {exception}" );
							}
						} );
					}

					if ( _predictorSampleCount < _predictorSampleArray.Length )
					{
						ref var predictorSample = ref _predictorSampleArray[ _predictorSampleCount++ ];

						predictorSample.TickCount = app.Simulator.IRSDK.Data.TickCount;
						predictorSample.InputFFBSample = app.Simulator.SteeringWheelTorque_ST[ 5 ];
						predictorSample.WheelVelocity = app.DirectInput.ForceFeedbackWheelVelocity;
						predictorSample.PredictionMode = engine.PredictionMode;
						predictorSample.PredictionBlend = engine.PredictionBlend;
						predictorSample.PredictedValue = predictedValue;
						predictorSample.DeltaClamped = clampedDelta != unclampedDelta;
						predictorSample.OutputFFBSample = _predictedSteeringWheelTorque60Hz;
					}

					_lastLapDistPct = app.Simulator.LapDistPct;

#endif
				}
				else
				{
					_steeringWheelTorque360Hz[ 0 ] = 0f;
					_steeringWheelTorque360Hz[ 1 ] = 0f;
					_steeringWheelTorque360Hz[ 2 ] = 0f;
					_steeringWheelTorque360Hz[ 3 ] = 0f;
					_steeringWheelTorque360Hz[ 4 ] = 0f;
					_steeringWheelTorque360Hz[ 5 ] = 0f;
					_steeringWheelTorque360Hz[ 6 ] = 0f;
					_steeringWheelTorque360Hz[ 7 ] = 0f;

					_predictedSteeringWheelTorque60Hz = 0f;
				}

				_elapsedMilliseconds = 0f;

				UpdateSteeringWheelTorqueBuffer = false;
			}

			// get next 60Hz and 360Hz steering wheel torque samples

			var steeringWheelTorque360HzIndex = 1f + ( _elapsedMilliseconds * 360f / 1000f );

			var i1 = Math.Min( MaxSteeringWheelTorque360HzIndex, (int) MathF.Truncate( steeringWheelTorque360HzIndex ) );
			var i2 = Math.Min( MaxSteeringWheelTorque360HzIndex, i1 + 1 );
			var i3 = Math.Min( MaxSteeringWheelTorque360HzIndex, i2 + 1 );
			var i0 = Math.Max( 0, i1 - 1 );

			var t = MathF.Min( 1f, steeringWheelTorque360HzIndex - i1 );

			var m0 = _steeringWheelTorque360Hz[ i0 ];
			var m1 = _steeringWheelTorque360Hz[ i1 ];
			var m2 = _steeringWheelTorque360Hz[ i2 ];
			var m3 = _steeringWheelTorque360Hz[ i3 ];

			var steeringWheelTorque60Hz = _steeringWheelTorque360Hz[ 6 ];
			var steeringWheelTorque500Hz = MathZ.InterpolateHermite( m0, m1, m2, m3, t );

			// update peak torque

			if ( ClearPeakTorque )
			{
				_peakTorque = 0f;

				ClearPeakTorque = false;
			}

			if ( app.Simulator.IsOnTrack && ( app.Simulator.PlayerTrackSurface == IRSDKSharper.IRacingSdkEnum.TrkLoc.OnTrack ) && ( app.Simulator.PlayerTrackSurfaceMaterial >= IRSDKSharper.IRacingSdkEnum.TrkSurf.Asphalt1Material ) && ( app.Simulator.PlayerTrackSurfaceMaterial <= IRSDKSharper.IRacingSdkEnum.TrkSurf.RacingDirt2Material ) )
			{
				_peakTorque = MathF.Max( _peakTorque, MathZ.Lerp( _peakTorque, MathF.Abs( steeringWheelTorque500Hz ), 0.01f ) );
			}

			// update auto torque

			_autoTorque = _peakTorque * settings.RacingWheelWheelForce / settings.RacingWheelAutoTarget;

			// convert the one-shot crash/curb protection triggers into per-tick pulses for the engine's protection
			// modules (the modules own the timers now; the announcement side effects stay here in Update)

			var crashProtectionTriggered = ActivateCrashProtection;
			ActivateCrashProtection = false;

			var curbProtectionTriggered = ActivateCurbProtection;
			ActivateCurbProtection = false;

			// grab the next LFE magnitude and the parked factor (0-5 MPH), both fed into the tick context

			var inputLFEMagnitude = app.LFE.CurrentMagnitude;

			var parkedFactor = MathZ.Saturate( 1f - ( app.Simulator.Velocity / 2.2352f ) );

			// build the per-tick context and evaluate the whole FFB graph (algorithm + effects + vibration generators)

			var tickContext = BuildTickContext( app, deltaMilliseconds, _predictedSteeringWheelTorque60Hz, steeringWheelTorque500Hz, settings.RacingWheelMaxForce, inputLFEMagnitude, parkedFactor, crashProtectionTriggered, curbProtectionTriggered );

			engine.Process( in tickContext );

			// fire the protection chat/voice announcements on the rising edge of each protection becoming active
			// (moved out of the modules; the message-enable settings stay global)

			if ( engine.CrashProtectionActive && !_lastCrashProtectionActive )
			{
				if ( settings.RacingWheelCrashProtectionMessagesEnabled )
				{
					SendChatMessage( null, "CrashProtectionActivated" );
				}

				SpeakMairaAnnouncement( "MairaCrashProtectionActive" );
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

			// engine outputs: normalized main bus (post output curve/limiter) + normalized vibration bus, plus the
			// separately-computed test signal contribution

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

				_fadeTimerMS -= deltaMilliseconds;
			}
			else
			{
				_lastUnfadedOutputTorque = outputTorque;

				outputTorque += vibrationTorque;
			}

			// update output torque for telemetry

			_outputTorque = outputTorque;

			// update force feedback torque

			app.DirectInput.UpdateForceFeedbackEffect( outputTorque );

			// update graph

			app.Graph.UpdateLayer( Graph.LayerIndex.InputTorque60Hz, steeringWheelTorque60Hz / settings.RacingWheelMaxForce );
			app.Graph.UpdateLayer( Graph.LayerIndex.InputTorque, steeringWheelTorque500Hz / settings.RacingWheelMaxForce );
			app.Graph.UpdateLayer( Graph.LayerIndex.InputLFE, inputLFEMagnitude );
			app.Graph.UpdateLayer( Graph.LayerIndex.OutputTorque, outputTorque );

			var protectionForegroundColor = 0u;
			var protectionBackgroundColor = 0u;

			if ( CrashProtectionIsActive )
			{
				protectionForegroundColor = 0xFFFF5B2E;
				protectionBackgroundColor = 0xFF000000;
			}
			else if ( CurbProtectionIsActive )
			{
				protectionForegroundColor = 0xFFFFFF00;
				protectionBackgroundColor = 0xFF000000;
			}

			if ( outputTorque <= -0.99f )
			{
				app.Graph.SetGutterColors( protectionForegroundColor, protectionBackgroundColor, 0xFFFF0000, 0xFFFF0000 );
			}
			else if ( outputTorque >= 0.99f )
			{
				app.Graph.SetGutterColors( 0xFFFF0000, 0xFFFF0000, protectionForegroundColor, protectionBackgroundColor );
			}
			else
			{
				app.Graph.SetGutterColors( protectionForegroundColor, protectionBackgroundColor, protectionForegroundColor, protectionBackgroundColor );
			}

			// update recording data

			app.RecordingManager.AddRecordingData( steeringWheelTorque60Hz, steeringWheelTorque500Hz );
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[RacingWheel] Exception caught: {exception.Message.Trim()}" );

			_unsuspendTimerMS = UnsuspendTimeMS;
		}
	}

	/// <summary>
	/// Assembles the per-tick auxiliary input for the FFB graph engine from the current torque samples,
	/// telemetry, wheel hardware state, and the one-shot protection pulses. Built once per 360 Hz tick with no
	/// allocation (the struct is passed by readonly reference into every module).
	/// </summary>
	private FFBTickContext BuildTickContext( App app, float deltaMilliseconds, float torque60Hz, float torque360Hz, float maxForce, float lfeMagnitude, float parkedFactor, bool crashProtectionTriggered, bool curbProtectionTriggered )
	{
		var simulator = app.Simulator;
		var steeringEffects = app.SteeringEffects;
		var directInput = app.DirectInput;

		return new FFBTickContext(
			deltaMilliseconds: deltaMilliseconds,
			torque60Hz: torque60Hz,
			torque360Hz: torque360Hz,
			maxForce: maxForce,
			lfeMagnitude: lfeMagnitude,
			wheelPosition: directInput.ForceFeedbackWheelPosition,
			wheelVelocity: directInput.ForceFeedbackWheelVelocity,
			understeerEffect: steeringEffects.UndersteerEffect,
			oversteerEffect: steeringEffects.OversteerEffect,
			seatOfPantsEffect: steeringEffects.SeatOfPantsEffect,
			skidSlip: steeringEffects.SkidSlip,
			rpm: simulator.RPM,
			shiftRPM: simulator.ShiftLightsShiftRPM,
			gear: simulator.Gear,
			numForwardGears: simulator.NumForwardGears,
			absActive: simulator.BrakeABSactive,
			isOnTrack: simulator.IsOnTrack,
			usingTorqueData: _usingSteeringWheelTorqueData,
			velocityMS: simulator.Velocity,
			velocityY: simulator.VelocityY,
			parkedFactor: parkedFactor,
			steeringWheelAngle: simulator.SteeringWheelAngle,
			steeringWheelAngleMax: simulator.SteeringWheelAngleMax,
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

			_racingWheelPage.AutoForce_TextBlock.Text = $"{_autoTorque:F1} {DataContext.DataContext.Instance.Localization[ "TorqueUnits" ]}";

			// update the FFB graph preview: replay the loaded recording through the preview engine (rebuilt from the
			// currently selected graph) with a neutral context, so only the algorithm chain contributes. The traces
			// tap the module selected in the node editor — red = its input A, green = its input B (dual-input
			// modules only), blue = its output. For the Output module (the default selection) red/green show the
			// two sources and blue shows the final normalized output, like the old whole-graph preview.

			if ( UpdateAlgorithmPreview )
			{
				UpdateAlgorithmPreview = false;

				_algorithmPreviewGraphBase.Reset();

				var recording = app.RecordingManager.Recording;

				if ( settings.RacingWheelFFBGraphs.TryGetValue( settings.RacingWheelSelectedFFBGraphName, out var previewGraph ) )
				{
					_previewEngine.Rebuild( previewGraph );
				}

				_previewEngine.ResetState();

				var maxForce = settings.RacingWheelMaxForce;

				// resolve the selected module's taps once, before the replay loop

				var selectedModule = DataContext.DataContext.Instance.RacingWheelGraphViewModel.SelectedModule;

				var selectedIndex = selectedModule != null ? _previewEngine.IndexOf( selectedModule.ModuleId ) : -1;
				var selectedIsOutput = ( selectedModule == null ) || selectedModule.IsOutput || ( selectedIndex < 0 );

				// the ±100% clipping lines only mean something on the normalized final output
				_algorithmPreviewGraphBase.DrawClippingLines = selectedIsOutput;

				int input1Index = -1;
				int input2Index = -1;
				int outputIndex;

				if ( selectedIsOutput )
				{
					input1Index = _previewEngine.IndexOf( FFBGraph.Source360ModuleId );
					input2Index = -1;
					outputIndex = -1; // final output is already normalized — read MainOutput directly
				}
				else if ( selectedModule!.SignalInputCount == 0 )
				{
					// a source module has no inputs — show just its own waveform
					outputIndex = selectedIndex;
				}
				else
				{
					(input1Index, input2Index) = _previewEngine.GetResolvedInputs( selectedIndex );

					if ( selectedModule.SignalInputCount < 2 )
					{
						// single-input module — no second input trace
						input2Index = -1;
					}

					outputIndex = selectedIndex;
				}

				var previousOutputValue = 0f;
				var isFirstSample = true;

				for ( var x = 0; x < _algorithmPreviewGraphBase.BitmapWidth; x++ )
				{
					if ( ( recording?.Data != null ) && ( x < recording.Data.Count ) )
					{
						var inputTorque60Hz = recording.Data[ x ].InputTorque60Hz;
						var inputTorque500Hz = recording.Data[ x ].InputTorque500Hz;

						var previewContext = FFBTickContext.Neutral( 0f, inputTorque60Hz, inputTorque500Hz, maxForce );

						_previewEngine.Process( in previewContext );

						// the main bus is in Nm until the Output module normalizes it, so tapped signals are
						// scaled by max force for display; the final output is already normalized
						// draw the output value first because it fill the space below the line with black
						var outputValue = outputIndex >= 0 ? _previewEngine.GetSignal( outputIndex ) / maxForce : _previewEngine.MainOutput;

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

						// show clipping only if the output module is selected
						if ( selectedIsOutput )
						{
							if ( outputValue <= -0.99f )
							{
								_algorithmPreviewGraphBase.SetGutterColors( 0, 0, 0xFFFF0000, 0xFFFF0000 );
							}
							else if ( outputValue >= 0.99f )
							{
								_algorithmPreviewGraphBase.SetGutterColors( 0xFFFF0000, 0xFFFF0000, 0, 0 );
							}
							else
							{
								_algorithmPreviewGraphBase.SetGutterColors( 0, 0, 0, 0 );
							}
						}
						else
						{
							_algorithmPreviewGraphBase.SetGutterColors( 0, 0, 0, 0 );
						}
					}

					_algorithmPreviewGraphBase.FinishUpdates();
				}

				_algorithmPreviewGraphBase.WritePixels();
			}

			// update record button

			_racingWheelPage.Record_MairaMappableButton.Disabled = !app.Simulator.IsOnTrack;
			_racingWheelPage.Record_MairaMappableButton.Blink = app.RecordingManager.IsRecording;

			// suspend racing wheel force feedback if iracing ffb is enabled or we are calibrating

			SuspendForceFeedback = !app.Simulator.IsConnected || ( app.Simulator.SteeringFFBEnabled && !settings.RacingWheelAlwaysEnableFFB ) || app.SteeringEffects.IsCalibrating;

			/*
			app.Debug.Label_1 = $"FadingIsActive: {FadingIsActive}";
			app.Debug.Label_2 = $"_fadeTimerMS: {_fadeTimerMS:F0} ms";
			app.Debug.Label_4 = $"_outputTorque: {_outputTorque * 100f:F0}%";
			*/
		}
	}

#if DEBUG

	private static async Task DumpLapAsync( PredictorSample[] samples, string path )
	{
		var directoryPath = Path.GetDirectoryName( path );

		if ( !string.IsNullOrEmpty( directoryPath ) )
		{
			Directory.CreateDirectory( directoryPath );
		}

		await using var stream = new FileStream( path, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024, useAsync: true );
		await using var writer = new StreamWriter( stream );
		await using var csv = new CsvWriter( writer, CultureInfo.InvariantCulture );

		await csv.WriteRecordsAsync( samples );
	}

#endif
}
