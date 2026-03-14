
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

using CsvHelper;

using MarvinsAIRARefactored.Classes;
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
	private const float CrashProtectionRecoveryTime = 1000f;

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
	public bool CrashProtectionIsActive { get => _crashProtectionTimerMS > 0f; }
	public bool CurbProtectionIsActive { get => _curbProtectionTimerMS > 0f; }
	public bool FadingIsActive { get => _fadeTimerMS > 0f; }

	private float _unsuspendTimerMS = 0f;
	private float _fadeTimerMS = 0f;
	private float _testSignalTimerMS = 0f;
	private float _crashProtectionTimerMS = 0f;
	private float _curbProtectionTimerMS = 0f;
	private float _understeerEffectTimerMS = 0f;
	private float _oversteerEffectTimerMS = 0f;
	private float _seatOfPantsEffectTimerMS = 0f;
	private float _vibrateOnGearChangeTimerMS = 0f;
	private float _vibrateOnABSTimerMS = 0f;

	private readonly float[] _steeringWheelTorque360Hz = new float[ Simulator.SamplesPerFrame360Hz + 2 ];

	private readonly float[,] _algorithmProperties = new float[ 2, 12 ];
	private readonly Algorithm[] _lastAlgorithm = new Algorithm[ 2 ];
	private bool _preventSwitchToNonCannedMultiAdjustAlgorithmSource = false;

	private float _outputTorque = 0f;
	private float _peakTorque = 0f;
	private float _autoTorque = 0f;

	private float _lastUnfadedOutputTorque = 0f;

	private float _elapsedMilliseconds = 0f;

	private readonly GraphBase _algorithmPreviewGraphBase = new();

	private bool _logiPlayLedsNotWorking = false;

	private int _updateCounter = UpdateInterval + 4;
	private int _lastGear = 0;

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

		app.Graph.SetLayerColors( Graph.LayerIndex.InputTorque60Hz, 1f, 0f, 0f, 1f, 0f, 0f );
		app.Graph.SetLayerColors( Graph.LayerIndex.InputTorque, 1f, 0f, 1f, 1f, 0f, 1f );
		app.Graph.SetLayerColors( Graph.LayerIndex.InputLFE, 0.1f, 0.5f, 1f, 1f, 1f, 1f );
		app.Graph.SetLayerColors( Graph.LayerIndex.OutputTorque, 0f, 1f, 1f, 0f, 1f, 1f );

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

	public static void SendChatMessage( string key, string? value = null )
	{
		var app = App.Instance!;

		if ( DataContext.DataContext.Instance.Settings.RacingWheelSendChatMessages && ( app.Simulator.UserName != string.Empty ) )
		{
			var playerName = app.Simulator.UserName;

			playerName = playerName.Replace( " ", "." );

			app.ChatQueue.SendMessage( $"/{playerName} (MAIRA) {DataContext.DataContext.Instance.Localization[ key ]}", value );
		}
	}

	public void SetCannedMultiAdjustAlgorithmValues()
	{
		_preventSwitchToNonCannedMultiAdjustAlgorithmSource = true;

		var settings = DataContext.DataContext.Instance.Settings;

		switch ( settings.RacingWheelMultiFFBSourceSelection )
		{
			case MultiFFBSourceOptions.Native60Hz:
				settings.RacingWheelMulti360HzDetail = 0f;
				break;

			case MultiFFBSourceOptions.Native360Hz:
				settings.RacingWheelMulti360HzDetail = 1f;
				break;

			case MultiFFBSourceOptions.Hybrid10:
				if ( settings.RacingWheelMulti360HzDetail == 0f )
				{
					settings.RacingWheelMulti360HzDetail = 1f;
				}
				break;

			case MultiFFBSourceOptions.HybridVariable30:
                if (settings.RacingWheelMulti360HzDetail == 0f)
                {
                    settings.RacingWheelMulti360HzDetail = 1f;
                }
                break;

			case MultiFFBSourceOptions.DefaultsNative60Hz:
				settings.RacingWheelMulti360HzDetail = 1f;
				settings.RacingWheelMultiTorqueCompression = 0f;
				settings.RacingWheelMultiEnableSlewPeakMode = true;
				settings.RacingWheelMultiSlewRateReduction = 0.1f;
				settings.RacingWheelMultiDetailGain = 0f;
				settings.RacingWheelMultiOutputSmoothing = 0.25f;
				break;

			case MultiFFBSourceOptions.DefaultsNative360Hz:
				settings.RacingWheelMulti360HzDetail = 1f;
				settings.RacingWheelMultiTorqueCompression = 0f;
				settings.RacingWheelMultiEnableSlewPeakMode = true;
				settings.RacingWheelMultiSlewRateReduction = 0f;
				settings.RacingWheelMultiDetailGain = 0f;
				settings.RacingWheelMultiOutputSmoothing = 0f;
				break;

			case MultiFFBSourceOptions.DefaultsHybrid10:
				settings.RacingWheelMulti360HzDetail = 1f;
				settings.RacingWheelMultiTorqueCompression = 0f;
				settings.RacingWheelMultiEnableSlewPeakMode = true;
				settings.RacingWheelMultiSlewRateReduction = 0f;
				settings.RacingWheelMultiDetailGain = 0f;
				settings.RacingWheelMultiOutputSmoothing = 0f;
				break;

			case MultiFFBSourceOptions.DefaultsHybridVariable30:
				settings.RacingWheelMulti360HzDetail = 1f;
				settings.RacingWheelMultiTorqueCompression = 0f;
				settings.RacingWheelMultiEnableSlewPeakMode = true;
				settings.RacingWheelMultiSlewRateReduction = 0.1f * ( 1f - MathZ.Saturate( ( 8f - settings.RacingWheelWheelForce ) / 6f ) );
				settings.RacingWheelMultiDetailGain = 0f;
				settings.RacingWheelMultiOutputSmoothing = 0.1f;
				break;

			case MultiFFBSourceOptions.PresetBoostDetail:
				settings.RacingWheelMultiDetailGain = 1f;
				settings.RacingWheelMultiOutputSmoothing = 0.1f;
				break;

			case MultiFFBSourceOptions.PresetReduceDetail:
				settings.RacingWheelMultiDetailGain = -0.3f;
				settings.RacingWheelMultiOutputSmoothing = 0f;
				break;

			case MultiFFBSourceOptions.PresetReduceBigBumps:
				settings.RacingWheelMultiTorqueCompression = 0.5f;
				break;

			case MultiFFBSourceOptions.PresetBasicFFB:
				settings.RacingWheelMulti360HzDetail = 0.3f;
				settings.RacingWheelMultiEnableSlewPeakMode = true;
				settings.RacingWheelMultiSlewRateReduction = 0.1f * ( 1f - MathZ.Saturate( ( 8f - settings.RacingWheelWheelForce ) / 6f ) );
				settings.RacingWheelMultiDetailGain = 0f;
				settings.RacingWheelMultiOutputSmoothing = 0f;
				break;

			case MultiFFBSourceOptions.PresetBalancedFFB:
				settings.RacingWheelMulti360HzDetail = 0.55f;
				settings.RacingWheelMultiEnableSlewPeakMode = true;
				settings.RacingWheelMultiSlewRateReduction = 0.1f * ( 1f - MathZ.Saturate( ( 8f - settings.RacingWheelWheelForce ) / 6f ) );
				settings.RacingWheelMultiDetailGain = 0f;
				settings.RacingWheelMultiOutputSmoothing = 0.08f;
				break;
		}

		_preventSwitchToNonCannedMultiAdjustAlgorithmSource = false;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private float ProcessAlgorithm( int algorithmPropertyIndex, float steeringWheelTorque60Hz, float steeringWheelTorque500Hz, float curbProtectionLerpFactor )
	{
		// shortcut to settings

		var settings = DataContext.DataContext.Instance.Settings;

		// apply algorithm

		var outputTorque = 0f;

		switch ( settings.RacingWheelAlgorithm )
		{
			case Algorithm.Native60Hz:
			{
				outputTorque = steeringWheelTorque60Hz / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.Native360Hz:
			{
				outputTorque = steeringWheelTorque500Hz / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.DetailBooster:
			{
				var detailBoost = MathZ.Lerp( 1f + settings.RacingWheelDetailBoost, 1f, curbProtectionLerpFactor );

				_algorithmProperties[ algorithmPropertyIndex, 1 ] = MathZ.Lerp( _algorithmProperties[ algorithmPropertyIndex, 1 ] + ( steeringWheelTorque500Hz - _algorithmProperties[ algorithmPropertyIndex, 0 ] ) * detailBoost, steeringWheelTorque500Hz, settings.RacingWheelDetailBoostBias );
				_algorithmProperties[ algorithmPropertyIndex, 0 ] = steeringWheelTorque500Hz;

				outputTorque = _algorithmProperties[ algorithmPropertyIndex, 1 ] / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.DeltaLimiter:
			{
				var deltaLimit = MathZ.Lerp( settings.RacingWheelDeltaLimit / 500f, 0f, curbProtectionLerpFactor );

				var limitedDeltaSteeringWheelTorque500Hz = Math.Clamp( steeringWheelTorque500Hz - _algorithmProperties[ algorithmPropertyIndex, 0 ], -deltaLimit, deltaLimit );

				_algorithmProperties[ algorithmPropertyIndex, 1 ] = MathZ.Lerp( _algorithmProperties[ algorithmPropertyIndex, 1 ] + limitedDeltaSteeringWheelTorque500Hz, steeringWheelTorque500Hz, settings.RacingWheelDeltaLimiterBias );
				_algorithmProperties[ algorithmPropertyIndex, 0 ] = steeringWheelTorque500Hz;

				outputTorque = _algorithmProperties[ algorithmPropertyIndex, 1 ] / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.DetailBoosterOn60Hz:
			{
				var detailBoost = MathZ.Lerp( 1f + settings.RacingWheelDetailBoost, 1f, curbProtectionLerpFactor );

				_algorithmProperties[ algorithmPropertyIndex, 1 ] = MathZ.Lerp( _algorithmProperties[ algorithmPropertyIndex, 1 ] + ( steeringWheelTorque500Hz - _algorithmProperties[ algorithmPropertyIndex, 0 ] ) * detailBoost, steeringWheelTorque60Hz, settings.RacingWheelDetailBoostBias );
				_algorithmProperties[ algorithmPropertyIndex, 0 ] = steeringWheelTorque500Hz;

				outputTorque = _algorithmProperties[ algorithmPropertyIndex, 1 ] / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.DeltaLimiterOn60Hz:
			{
				var deltaLimit = MathZ.Lerp( settings.RacingWheelDeltaLimit / 500f, 0f, curbProtectionLerpFactor );

				var limitedDeltaSteeringWheelTorque500Hz = Math.Clamp( steeringWheelTorque500Hz - _algorithmProperties[ algorithmPropertyIndex, 0 ], -deltaLimit, deltaLimit );

				_algorithmProperties[ algorithmPropertyIndex, 1 ] = MathZ.Lerp( _algorithmProperties[ algorithmPropertyIndex, 1 ] + limitedDeltaSteeringWheelTorque500Hz, steeringWheelTorque60Hz, settings.RacingWheelDeltaLimiterBias );
				_algorithmProperties[ algorithmPropertyIndex, 0 ] = steeringWheelTorque500Hz;

				outputTorque = _algorithmProperties[ algorithmPropertyIndex, 1 ] / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.SlewAndTotalCompression:
			{
				var normalizedRunningTorque = _algorithmProperties[ algorithmPropertyIndex, 1 ] / settings.RacingWheelMaxForce;

				var normalizedDelta = ( steeringWheelTorque500Hz - _algorithmProperties[ algorithmPropertyIndex, 1 ] ) / settings.RacingWheelMaxForce;
				var normalizedDeltaAbs = MathF.Abs( normalizedDelta );

				var deltaLimit = settings.RacingWheelSlewCompressionThreshold / 500f;

				float oneMinusSlewCompressionRate;

				if ( MathF.Sign( normalizedDelta ) == MathF.Sign( steeringWheelTorque500Hz ) )
				{
					oneMinusSlewCompressionRate = 1f - settings.RacingWheelSlewCompressionRate;
				}
				else
				{
					oneMinusSlewCompressionRate = MathF.Max( 0.75f, 1f - settings.RacingWheelSlewCompressionRate * 0.75f );
				}

				if ( normalizedDeltaAbs > deltaLimit )
				{
					normalizedRunningTorque += MathF.CopySign( deltaLimit + ( ( normalizedDeltaAbs - deltaLimit ) * oneMinusSlewCompressionRate ), normalizedDelta );
				}
				else
				{
					normalizedRunningTorque += normalizedDelta;
				}

				if ( settings.RacingWheelTotalCompressionRate != 0f )
				{
					normalizedRunningTorque = MathZ.Compression( normalizedRunningTorque, settings.RacingWheelTotalCompressionRate, settings.RacingWheelTotalCompressionThreshold, settings.RacingWheelTotalCompressionThreshold );
				}

				_algorithmProperties[ algorithmPropertyIndex, 1 ] = normalizedRunningTorque * settings.RacingWheelMaxForce;
				_algorithmProperties[ algorithmPropertyIndex, 0 ] = steeringWheelTorque500Hz;

				outputTorque = normalizedRunningTorque;

				break;
			}

			case Algorithm.MultiAdjustmentToolkit:
			{
				const int index60HzLastTorque = 0;
				const int index360HzLastTorque = 1;
				const int indexHybridLastTorque = 2;
				const int indexHybridLastVelocitySign = 3;
				const int indexNormalizedLastCompressedTorque = 4;
				const int indexNormalizedLastSlewReducedTorque = 5;
				const int indexNormalizedLastDetailGainLpfTorque = 6;
				const int indexNormalizedLastDetailGainTorque = 7;
				const int indexNormalizedLastSmoothingLpfTorque = 8;
				const int indexNormalizedLastSmoothedTorque = 9;
				const int indexTicksSinceLast60Hz = 10;
				const int indexPeakCountdown = 11;

				var lastTorque60Hz = 0f;
				var ticksSinceLast60Hz = 0f;
				var peakCountdown = MathF.Max( 0f, _algorithmProperties[ algorithmPropertyIndex, indexPeakCountdown ] - 1f );
				var hybridLastTorque = 0f;
				if ( settings.RacingWheelAlgorithm == _lastAlgorithm[ algorithmPropertyIndex ] )
				{
					lastTorque60Hz = _algorithmProperties[ algorithmPropertyIndex, index60HzLastTorque ];
					ticksSinceLast60Hz = ( lastTorque60Hz == steeringWheelTorque60Hz ) ? ( _algorithmProperties[ algorithmPropertyIndex, indexTicksSinceLast60Hz ] + 1f ) : 0f;
					hybridLastTorque = _algorithmProperties[ algorithmPropertyIndex, indexHybridLastTorque ];
				}

				var hfScaledDelta = ( steeringWheelTorque500Hz - _algorithmProperties[ algorithmPropertyIndex, index360HzLastTorque ] ) * settings.RacingWheelMulti360HzDetail;
				var hybridTorque = 0f;

				switch ( settings.RacingWheelMultiFFBSourceSelection )
				{
					case MultiFFBSourceOptions.Native60Hz:
						hybridTorque = steeringWheelTorque60Hz;
						break;

					case MultiFFBSourceOptions.HybridVariable30:
						var preliminaryHybridTorque = MathZ.Lerp( hybridLastTorque + hfScaledDelta, steeringWheelTorque60Hz, 0.3f - 0.2f * peakCountdown / 10f );

						if ( MathF.Sign( preliminaryHybridTorque - hybridLastTorque ) != _algorithmProperties[ algorithmPropertyIndex, indexHybridLastVelocitySign ] )
						{
							peakCountdown = 10f;
							hybridTorque = MathZ.Lerp( hybridLastTorque + hfScaledDelta, steeringWheelTorque60Hz, 0.3f - 0.2f * peakCountdown / 10f );
						}
						else
						{
							hybridTorque = preliminaryHybridTorque;
						}

						break;

					case MultiFFBSourceOptions.Hybrid10:
						hybridTorque = MathZ.Lerp( hybridLastTorque + hfScaledDelta, steeringWheelTorque60Hz, 0.1f );
						break;

					case MultiFFBSourceOptions.Native360Hz:
						hybridTorque = steeringWheelTorque500Hz;
						break;
				}

				var normalizedHybridTorque = hybridTorque / settings.RacingWheelMaxForce;

				var normalizedCompressedTorque = normalizedHybridTorque;
				var compressionAmount = settings.RacingWheelMultiTorqueCompression;
				if ( compressionAmount > 0f )
				{
					var compressionRate = MathF.Min( 2f * compressionAmount, 0.75f );
					var compressionThreshold = 1f - 0.75f * compressionAmount;
					var compressionWidth = MathF.Min( compressionAmount, 0.5f );

					normalizedCompressedTorque = MathZ.Compression( normalizedHybridTorque, compressionRate, compressionThreshold, compressionWidth );
				}

				var normalizedLastCompressedTorque = _algorithmProperties[ algorithmPropertyIndex, indexNormalizedLastCompressedTorque ];
				var normalizedLastSlewReducedTorque = _algorithmProperties[ algorithmPropertyIndex, indexNormalizedLastSlewReducedTorque ];
				var slewAmount = settings.RacingWheelMultiSlewRateReduction;
				var normalizedSlewReducedTorque = normalizedCompressedTorque;
				if ( slewAmount > 0f )
				{
					var absNormalizedCompressedTorque = MathF.Abs( normalizedCompressedTorque );
					var absNormalizedLastCompressedTorque = MathF.Abs( normalizedLastCompressedTorque );
					var absNormalizedLastSlewReducedTorque = MathF.Abs( normalizedLastSlewReducedTorque );

					if ( settings.RacingWheelMultiEnableSlewPeakMode && ( absNormalizedCompressedTorque < absNormalizedLastCompressedTorque ) && ( MathF.Abs( normalizedLastCompressedTorque ) != 0f ) )
					{
						var targetScaledTorque = normalizedLastSlewReducedTorque * normalizedCompressedTorque / normalizedLastCompressedTorque;
						var targetBlendedTorque = targetScaledTorque * 0.5f + normalizedCompressedTorque * 0.5f;

						if ( MathF.Abs( targetBlendedTorque ) < absNormalizedLastSlewReducedTorque )
						{
							normalizedSlewReducedTorque = normalizedLastSlewReducedTorque + ( targetBlendedTorque - normalizedLastSlewReducedTorque );
						}
						else
						{
							normalizedSlewReducedTorque = normalizedLastSlewReducedTorque + ( targetScaledTorque - normalizedLastSlewReducedTorque );
						}
					}
					else
					{
						var normalizedSlewDelta = normalizedCompressedTorque - normalizedLastSlewReducedTorque;
						var slewThreshold = 0.01f - 0.0095f * slewAmount;
						var slewWidth = MathF.Min( MathF.Pow( slewAmount, 0.005f ), 0.0025f );

						if ( MathF.Abs( normalizedSlewDelta ) > slewThreshold )
						{
							var slewRate = MathF.Min( MathF.Pow( slewAmount, 0.55f ), 0.9f );
							var slewRateMultiplier = ( absNormalizedCompressedTorque < absNormalizedLastSlewReducedTorque ) ? 0.8f : 1f;

							normalizedSlewReducedTorque = normalizedLastSlewReducedTorque + MathZ.Compression( normalizedSlewDelta, slewRate * slewRateMultiplier, slewThreshold, slewWidth );
						}
						else
						{
							normalizedSlewReducedTorque = normalizedCompressedTorque;
						}
					}
				}

				var normalizedDetailGainTorque = normalizedSlewReducedTorque;
				var normalizedDetailGainLpfTorque = 0f;
				var detailGain = MathZ.Lerp( 1f + settings.RacingWheelMultiDetailGain, 1f, curbProtectionLerpFactor );
				if ( detailGain != 1f )
				{
					const float epsilonGuard = 1e-6f;

					var normalizedLastDetailGainLpfTorque = _algorithmProperties[ algorithmPropertyIndex, indexNormalizedLastDetailGainLpfTorque ];
					normalizedDetailGainLpfTorque = MathZ.Lerp( normalizedLastDetailGainLpfTorque, normalizedSlewReducedTorque, 0.11809f );
					var currentDeviation = normalizedSlewReducedTorque - normalizedDetailGainLpfTorque;
					var lastDeviation = normalizedLastSlewReducedTorque - normalizedDetailGainLpfTorque;
					var priorDeviation = normalizedLastSlewReducedTorque - normalizedLastDetailGainLpfTorque;

					if ( MathF.Abs( currentDeviation ) > MathF.Abs( lastDeviation ) || MathF.Sign( currentDeviation ) != MathF.Sign( priorDeviation ) || MathF.Abs( lastDeviation ) < epsilonGuard )
					{
						if ( currentDeviation > 0f )
						{
							normalizedDetailGainTorque = MathF.Max( normalizedDetailGainLpfTorque + currentDeviation * detailGain, normalizedDetailGainLpfTorque );
						}
						else
						{
							normalizedDetailGainTorque = MathF.Min( normalizedDetailGainLpfTorque + currentDeviation * detailGain, normalizedDetailGainLpfTorque );
						}
					}
					else
					{
						var ratio = currentDeviation / lastDeviation;
						var carried = ratio * ( _algorithmProperties[ algorithmPropertyIndex, indexNormalizedLastDetailGainTorque ] - normalizedDetailGainLpfTorque );
						var candidate = normalizedDetailGainLpfTorque + carried;

						normalizedDetailGainTorque = ( currentDeviation > 0f ) ? MathF.Max( candidate, normalizedDetailGainLpfTorque ) : MathF.Min( candidate, normalizedDetailGainLpfTorque );
					}
				}

				var normalizedSmoothedTorque = normalizedDetailGainTorque;
				var normalizedSmoothingLpfTorque = 0f;
				if ( settings.RacingWheelMultiOutputSmoothing > 0f )
				{
					var normalizedLastSmoothingLpfTorque = _algorithmProperties[ algorithmPropertyIndex, indexNormalizedLastSmoothingLpfTorque ];
					normalizedSmoothingLpfTorque = MathZ.Lerp( normalizedLastSmoothingLpfTorque, normalizedDetailGainTorque, 0.22223f );
					var smoothingRate = 0.9f * MathF.Pow( settings.RacingWheelMultiOutputSmoothing, 0.5f );
					var lpfDeltaAdjustedTorque = _algorithmProperties[ algorithmPropertyIndex, indexNormalizedLastSmoothedTorque ] + normalizedSmoothingLpfTorque - normalizedLastSmoothingLpfTorque;
					normalizedSmoothedTorque = MathZ.Lerp( normalizedDetailGainTorque, lpfDeltaAdjustedTorque, smoothingRate );
				}

				outputTorque = normalizedSmoothedTorque;

				_algorithmProperties[ algorithmPropertyIndex, indexHybridLastTorque ] = hybridTorque;
				_algorithmProperties[ algorithmPropertyIndex, indexHybridLastVelocitySign ] = MathF.Sign( hybridTorque - hybridLastTorque );
				_algorithmProperties[ algorithmPropertyIndex, index60HzLastTorque ] = steeringWheelTorque60Hz;
				_algorithmProperties[ algorithmPropertyIndex, index360HzLastTorque ] = steeringWheelTorque500Hz;
				_algorithmProperties[ algorithmPropertyIndex, indexTicksSinceLast60Hz ] = ticksSinceLast60Hz;
				_algorithmProperties[ algorithmPropertyIndex, indexNormalizedLastCompressedTorque ] = normalizedCompressedTorque;
				_algorithmProperties[ algorithmPropertyIndex, indexNormalizedLastSlewReducedTorque ] = normalizedSlewReducedTorque;
				_algorithmProperties[ algorithmPropertyIndex, indexNormalizedLastDetailGainLpfTorque ] = normalizedDetailGainLpfTorque;
				_algorithmProperties[ algorithmPropertyIndex, indexNormalizedLastDetailGainTorque ] = normalizedDetailGainTorque;
				_algorithmProperties[ algorithmPropertyIndex, indexNormalizedLastSmoothingLpfTorque ] = normalizedSmoothingLpfTorque;
				_algorithmProperties[ algorithmPropertyIndex, indexNormalizedLastSmoothedTorque ] = normalizedSmoothedTorque;
				_algorithmProperties[ algorithmPropertyIndex, indexPeakCountdown ] = peakCountdown;

				break;
			}
		}

		// remember the last algorithm used

		_lastAlgorithm[ algorithmPropertyIndex ] = settings.RacingWheelAlgorithm;

		// apply output curve

		if ( settings.RacingWheelOutputCurve != 0f )
		{
			var power = MathZ.CurveToPower( settings.RacingWheelOutputCurve );

			outputTorque = MathF.Sign( outputTorque ) * MathF.Pow( MathF.Abs( outputTorque ), power );
		}

		// apply soft limiter after output curve

		if ( settings.RacingWheelEnableSoftLimiter )
		{
			outputTorque = MathZ.SoftLimiter( outputTorque );
		}

		// apply output maximum

		if ( settings.RacingWheelOutputMaximum < 1f )
		{
			outputTorque = Math.Clamp( outputTorque, -settings.RacingWheelOutputMaximum, settings.RacingWheelOutputMaximum );
		}

		// apply output minimum

		if ( settings.RacingWheelOutputMinimum > 0f )
		{
			if ( outputTorque >= 0f )
			{
				if ( outputTorque < settings.RacingWheelOutputMinimum )
				{
					outputTorque = settings.RacingWheelOutputMinimum;
				}
			}
			else
			{
				if ( outputTorque > -settings.RacingWheelOutputMinimum )
				{
					outputTorque = -settings.RacingWheelOutputMinimum;
				}
			}
		}

		// return calculated output torque

		return outputTorque;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Update( float deltaMilliseconds )
	{
		var app = App.Instance!;

		try
		{
			// easy reference to settings

			var settings = DataContext.DataContext.Instance.Settings;

			// initialize generated vibration torque

			var vibrationTorque = 0f;

			// test signal generator

			if ( PlayTestSignal )
			{
				_testSignalTimerMS = TestSignalTimeMS;

				app.Logger.WriteLine( "[RacingWheel] Sending test signal" );

				PlayTestSignal = false;
			}

			if ( _testSignalTimerMS > 0f )
			{
				_testSignalTimerMS -= deltaMilliseconds;

				vibrationTorque += MathF.Cos( _testSignalTimerMS * MathF.Tau / 20f ) * MathF.Sin( _testSignalTimerMS * MathF.Tau / TestSignalTimeMS * 2f ) * 0.2f;
			}

			// understeer vibration effect

			if ( settings.SteeringEffectsUndersteerEnabled && ( app.SteeringEffects.UndersteerEffect > 0f ) )
			{
				var isUndersteering = ( app.SteeringEffects.UndersteerEffect == 1f );

				var frequency = isUndersteering ? settings.SteeringEffectsUndersteerWheelVibrationMaximumFrequency : settings.SteeringEffectsUndersteerWheelVibrationMinimumFrequency;

				frequency = MathF.Max( 0.01f, frequency );

				var timeInSeconds = _understeerEffectTimerMS * 0.001f;

				var understeerEffectTorque = 0f;

				switch ( settings.SteeringEffectsUndersteerWheelVibrationPattern )
				{
					case VibrationPattern.SineWave:
					{
						var sine = MathF.Sin( timeInSeconds * MathF.Tau * frequency );
						understeerEffectTorque = sine;
						break;
					}

					case VibrationPattern.SquareWave:
					{
						var sine = MathF.Sin( timeInSeconds * MathF.Tau * frequency );
						understeerEffectTorque = ( sine >= 0f ) ? 1f : -1f;
						break;
					}

					case VibrationPattern.TriangleWave:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						understeerEffectTorque = 4f * MathF.Abs( phase - 0.5f ) - 1f;
						break;
					}

					case VibrationPattern.SawtoothWaveIn:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						understeerEffectTorque = ( 1f - phase ) * MathF.Sign( app.Simulator.SteeringWheelAngle );
						break;
					}

					case VibrationPattern.SawtoothWaveOut:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						understeerEffectTorque = ( 1f - phase ) * -MathF.Sign( app.Simulator.SteeringWheelAngle );
						break;
					}
				}

				_understeerEffectTimerMS += deltaMilliseconds;

				var periodMS = 1000f / frequency;

				if ( _understeerEffectTimerMS >= periodMS )
				{
					_understeerEffectTimerMS -= periodMS * MathF.Floor( _understeerEffectTimerMS / periodMS );
				}

				vibrationTorque += understeerEffectTorque * settings.SteeringEffectsUndersteerWheelVibrationStrength * MathF.Pow( app.SteeringEffects.UndersteerEffect, MathZ.CurveToPower( settings.SteeringEffectsUndersteerWheelVibrationCurve ) );
			}

			// oversteer vibration effect

			if ( settings.SteeringEffectsOversteerEnabled && ( app.SteeringEffects.OversteerEffect > 0f ) )
			{
				var isOversteering = ( app.SteeringEffects.OversteerEffect == 1f );

				var frequency = isOversteering ? settings.SteeringEffectsOversteerWheelVibrationMaximumFrequency : settings.SteeringEffectsOversteerWheelVibrationMinimumFrequency;

				frequency = MathF.Max( 0.01f, frequency );

				var timeInSeconds = _oversteerEffectTimerMS * 0.001f;

				var oversteerEffectTorque = 0f;

				switch ( settings.SteeringEffectsOversteerWheelVibrationPattern )
				{
					case VibrationPattern.SineWave:
					{
						var sine = MathF.Sin( timeInSeconds * MathF.Tau * frequency );
						oversteerEffectTorque = sine;
						break;
					}

					case VibrationPattern.SquareWave:
					{
						var sine = MathF.Sin( timeInSeconds * MathF.Tau * frequency );
						oversteerEffectTorque = ( sine >= 0f ) ? 1f : -1f;
						break;
					}

					case VibrationPattern.TriangleWave:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						oversteerEffectTorque = 4f * MathF.Abs( phase - 0.5f ) - 1f;
						break;
					}

					case VibrationPattern.SawtoothWaveIn:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						oversteerEffectTorque = ( phase - 1f ) * -MathF.Sign( app.Simulator.SteeringWheelAngle );
						break;
					}

					case VibrationPattern.SawtoothWaveOut:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						oversteerEffectTorque = ( 1f - phase ) * -MathF.Sign( app.Simulator.SteeringWheelAngle );
						break;
					}
				}

				_oversteerEffectTimerMS += deltaMilliseconds;

				var periodMS = 1000f / frequency;

				if ( _oversteerEffectTimerMS >= periodMS )
				{
					_oversteerEffectTimerMS -= periodMS * MathF.Floor( _oversteerEffectTimerMS / periodMS );
				}

				vibrationTorque += oversteerEffectTorque * settings.SteeringEffectsOversteerWheelVibrationStrength * MathF.Pow( app.SteeringEffects.OversteerEffect, MathZ.CurveToPower( settings.SteeringEffectsOversteerWheelVibrationCurve ) );
			}

			// seat-of-pants vibration effect

			if ( settings.SteeringEffectsSeatOfPantsEnabled && ( app.SteeringEffects.SeatOfPantsEffect != 0f ) )
			{
				var absSeatOfPantsEffect = MathF.Abs( app.SteeringEffects.SeatOfPantsEffect );

				var isAtMaxSeatOfPants = ( absSeatOfPantsEffect == 1f );

				var frequency = isAtMaxSeatOfPants ? settings.SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequency : settings.SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequency;

				frequency = MathF.Max( 0.01f, frequency );

				var timeInSeconds = _seatOfPantsEffectTimerMS * 0.001f;

				var seatOfPantsEffectTorque = 0f;

				switch ( settings.SteeringEffectsSeatOfPantsWheelVibrationPattern )
				{
					case VibrationPattern.SineWave:
					{
						var sine = MathF.Sin( timeInSeconds * MathF.Tau * frequency );
						seatOfPantsEffectTorque = sine;
						break;
					}

					case VibrationPattern.SquareWave:
					{
						var sine = MathF.Sin( timeInSeconds * MathF.Tau * frequency );
						seatOfPantsEffectTorque = ( sine >= 0f ) ? 1f : -1f;
						break;
					}

					case VibrationPattern.TriangleWave:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						seatOfPantsEffectTorque = 4f * MathF.Abs( phase - 0.5f ) - 1f;
						break;
					}

					case VibrationPattern.SawtoothWaveIn:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						seatOfPantsEffectTorque = ( phase - 1f ) * -MathF.Sign( app.Simulator.SteeringWheelAngle );
						break;
					}

					case VibrationPattern.SawtoothWaveOut:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						seatOfPantsEffectTorque = ( 1f - phase ) * -MathF.Sign( app.Simulator.SteeringWheelAngle );
						break;
					}
				}

				_seatOfPantsEffectTimerMS += deltaMilliseconds;

				var periodMS = 1000f / frequency;

				if ( _seatOfPantsEffectTimerMS >= periodMS )
				{
					_seatOfPantsEffectTimerMS -= periodMS * MathF.Floor( _seatOfPantsEffectTimerMS / periodMS );
				}

				vibrationTorque += seatOfPantsEffectTorque * settings.SteeringEffectsSeatOfPantsWheelVibrationStrength * MathF.Pow( absSeatOfPantsEffect, MathZ.CurveToPower( settings.SteeringEffectsSeatOfPantsWheelVibrationCurve ) );
			}

			// gear change vibration effect

			if ( settings.RacingWheelGearChangeVibrateStrength > 0f )
			{
				if ( app.Simulator.Gear != _lastGear )
				{
					if ( app.Simulator.Gear != 0 )
					{
						_vibrateOnGearChangeTimerMS = 100f;
					}

					_lastGear = app.Simulator.Gear;
				}

				if ( _vibrateOnGearChangeTimerMS > 0f )
				{
					var frequency = 40f;
					var timeInSeconds = _vibrateOnGearChangeTimerMS * 0.001f;
					var sine = MathF.Sin( timeInSeconds * MathF.Tau * frequency );

					vibrationTorque += ( sine >= 0f ) ? settings.RacingWheelGearChangeVibrateStrength : -settings.RacingWheelGearChangeVibrateStrength;

					_vibrateOnGearChangeTimerMS -= deltaMilliseconds;
				}
			}

			// abs vibration effect

			if ( settings.RacingWheelABSVibrateStrength > 0f )
			{
				if ( app.Simulator.BrakeABSactive )
				{
					var frequency = 50f;
					var timeInSeconds = _vibrateOnABSTimerMS * 0.001f;
					var phase = ( timeInSeconds * frequency ) % 1f;

					vibrationTorque += settings.RacingWheelABSVibrateStrength * ( 4f * MathF.Abs( phase - 0.5f ) - 1f );

					var periodMS = 1000f / frequency;

					if ( _vibrateOnABSTimerMS >= periodMS )
					{
						_vibrateOnABSTimerMS -= periodMS * MathF.Floor( _vibrateOnABSTimerMS / periodMS );
					}

					_vibrateOnABSTimerMS += deltaMilliseconds;
				}
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

					// Run 60 Hz predictor

					var predictedValue = settings.RacingWheelPredictionMode switch
					{
						PredictionMode.PredictK1 => _ffbPredictorK1.Step( _steeringWheelTorque360Hz[ 6 ], app.DirectInput.ForceFeedbackWheelVelocity ),
						PredictionMode.PredictK2 => _ffbPredictorK2.Step( _steeringWheelTorque360Hz[ 6 ], app.DirectInput.ForceFeedbackWheelVelocity ),
						_ => _steeringWheelTorque360Hz[ 6 ],
					};

					var unclampedDelta = predictedValue - _steeringWheelTorque360Hz[ 6 ];
					var clampedDelta = Math.Clamp( unclampedDelta, -0.5f, 0.5f );

					_predictedSteeringWheelTorque60Hz = MathZ.Lerp( _steeringWheelTorque360Hz[ 6 ], _steeringWheelTorque360Hz[ 6 ] + clampedDelta, settings.RacingWheelPredictionBlend );

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
						predictorSample.PredictionMode = settings.RacingWheelPredictionMode;
						predictorSample.PredictionBlend = settings.RacingWheelPredictionBlend;
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

			_autoTorque = _peakTorque * ( 1f + settings.RacingWheelAutoMargin );

			// update crash protection

			if ( ActivateCrashProtection )
			{
				if ( _crashProtectionTimerMS <= 0f )
				{
					if ( settings.RacingWheelCrashProtectionMessagesEnabled )
					{
						SendChatMessage( "CrashProtectionActivated" );
					}
				}

				_crashProtectionTimerMS = settings.RacingWheelCrashProtectionDuration * 1000f + CrashProtectionRecoveryTime;

				ActivateCrashProtection = false;
			}

			var crashProtectionScale = 1f;

			if ( _crashProtectionTimerMS > 0f )
			{
				crashProtectionScale = 1f - settings.RacingWheelCrashProtectionForceReduction * ( ( _crashProtectionTimerMS <= CrashProtectionRecoveryTime ) ? ( _crashProtectionTimerMS / CrashProtectionRecoveryTime ) : 1f );

				_crashProtectionTimerMS -= deltaMilliseconds;
			}

			// update curb protection

			if ( ActivateCurbProtection )
			{
				if ( _curbProtectionTimerMS <= 0f )
				{
					if ( settings.RacingWheelCurbProtectionMessagesEnabled )
					{
						SendChatMessage( "CurbProtectionActivated" );
					}
				}

				_curbProtectionTimerMS = settings.RacingWheelCurbProtectionDuration * 1000f;

				ActivateCurbProtection = false;
			}

			var curbProtectionLerpFactor = 0f;

			if ( _curbProtectionTimerMS > 0f )
			{
				curbProtectionLerpFactor = settings.RacingWheelCurbProtectionForceReduction;

				_curbProtectionTimerMS -= deltaMilliseconds;
			}

			// grab the next LFE magnitude

			var inputLFEMagnitude = app.LFE.CurrentMagnitude;

			// process the algorithm

			var outputTorque = ProcessAlgorithm( 0, _predictedSteeringWheelTorque60Hz, steeringWheelTorque500Hz, curbProtectionLerpFactor );

			// understeer constant force effect

			if ( settings.SteeringEffectsUndersteerEnabled && ( app.SteeringEffects.UndersteerEffect > 0f ) )
			{
				var constantForceTorque = settings.SteeringEffectsUndersteerWheelConstantForceStrength * MathF.Pow( app.SteeringEffects.UndersteerEffect, MathZ.CurveToPower( settings.SteeringEffectsUndersteerWheelConstantForceCurve ) );

				switch ( settings.SteeringEffectsUndersteerWheelConstantForceDirection )
				{
					case ConstantForceDirection.DecreaseForce:
					{
						outputTorque = MathZ.Lerp( outputTorque, 0f, constantForceTorque );
						break;
					}

					case ConstantForceDirection.IncreaseForce:
					{
						outputTorque += MathF.CopySign( constantForceTorque, app.Simulator.VelocityY );
						break;
					}
				}
			}

			// oversteer constant force effect

			if ( settings.SteeringEffectsOversteerEnabled && ( app.SteeringEffects.OversteerEffect > 0f ) )
			{
				var constantForceTorque = settings.SteeringEffectsOversteerWheelConstantForceStrength * MathF.Pow( app.SteeringEffects.OversteerEffect, MathZ.CurveToPower( settings.SteeringEffectsOversteerWheelConstantForceCurve ) );

				switch ( settings.SteeringEffectsOversteerWheelConstantForceDirection )
				{
					case ConstantForceDirection.DecreaseForce:
					{
						outputTorque = MathZ.Lerp( outputTorque, 0f, constantForceTorque );
						break;
					}

					case ConstantForceDirection.IncreaseForce:
					{
						outputTorque += MathF.CopySign( constantForceTorque, app.Simulator.VelocityY );
						break;
					}
				}
			}

			// seat-of-pants constant force effect

			if ( settings.SteeringEffectsSeatOfPantsEnabled && ( app.SteeringEffects.SeatOfPantsEffect != 0f ) )
			{
				var constantForceTorque = settings.SteeringEffectsSeatOfPantsWheelConstantForceStrength * MathF.CopySign( MathF.Pow( MathF.Abs( app.SteeringEffects.SeatOfPantsEffect ), MathZ.CurveToPower( settings.SteeringEffectsSeatOfPantsWheelConstantForceCurve ) ), app.SteeringEffects.SeatOfPantsEffect );

				switch ( settings.SteeringEffectsSeatOfPantsWheelConstantForceDirection )
				{
					case ConstantForceDirection.DecreaseForce:
					{
						outputTorque = MathZ.Lerp( outputTorque, 0f, MathF.Abs( constantForceTorque ) );

						break;
					}

					case ConstantForceDirection.IncreaseForce:
					{
						outputTorque -= constantForceTorque;

						break;
					}
				}
			}

			// apply crash protection

			outputTorque *= crashProtectionScale;

			// calculate parked factor (0-5 MPH)

			var parkedFactor = MathZ.Saturate( 1f - ( app.Simulator.Velocity / 2.2352f ) );

			// reduce forces when parked

			if ( settings.RacingWheelParkedStrength < 1f )
			{
				outputTorque *= MathZ.Lerp( 1f, settings.RacingWheelParkedStrength, parkedFactor );
			}

			// add wheel LFE

			if ( ( settings.RacingWheelLFEStrength > 0f ) && app.Simulator.IsOnTrack )
			{
				outputTorque += inputLFEMagnitude * settings.RacingWheelLFEStrength;
			}

			// add soft lock

			if ( settings.RacingWheelSoftLockStrength > 0f )
			{
				var deltaToMax = ( app.Simulator.SteeringWheelAngleMax * 0.5f ) - MathF.Abs( app.Simulator.SteeringWheelAngle );

				if ( deltaToMax < 0f )
				{
					var sign = MathF.Sign( app.Simulator.SteeringWheelAngle );

					outputTorque += sign * deltaToMax * 2f * settings.RacingWheelSoftLockStrength;

					if ( MathF.Sign( app.DirectInput.ForceFeedbackWheelVelocity ) != sign )
					{
						outputTorque += app.DirectInput.ForceFeedbackWheelVelocity * settings.RacingWheelSoftLockStrength;
					}
				}
			}

			// apply normal friction torque

			if ( settings.RacingWheelFriction > 0f )
			{
				outputTorque += MathZ.Lerp( app.DirectInput.ForceFeedbackWheelVelocity * settings.RacingWheelFriction, 0f, parkedFactor );
			}

			// apply parked friction torque

			if ( settings.RacingWheelParkedFriction > 0f )
			{
				outputTorque += MathZ.Lerp( 0f, app.DirectInput.ForceFeedbackWheelVelocity * settings.RacingWheelParkedFriction, parkedFactor );
			}

			// center wheel while racing and parked

			if ( app.Simulator.IsOnTrack )
			{
				var centeringForce = Math.Clamp( ( Math.Clamp( app.DirectInput.ForceFeedbackWheelPosition, -0.25f, 0.25f ) + app.DirectInput.ForceFeedbackWheelVelocity * 0.1f ) * settings.RacingWheelWheelCenteringStrength, -1f, 1f );

				var racingCenteringForce = ( settings.RacingWheelCenterWheelWhileRacing ) ? centeringForce : 0f;
				var parkedCenteringForce = ( settings.RacingWheelCenterWheelWhileParked ) ? centeringForce : 0f;

				outputTorque += MathZ.Lerp( racingCenteringForce, parkedCenteringForce, parkedFactor );
			}

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

			app.Graph.UpdateLayer( Graph.LayerIndex.InputTorque60Hz, steeringWheelTorque60Hz, steeringWheelTorque60Hz / settings.RacingWheelMaxForce );
			app.Graph.UpdateLayer( Graph.LayerIndex.InputTorque, steeringWheelTorque500Hz, steeringWheelTorque500Hz / settings.RacingWheelMaxForce );
			app.Graph.UpdateLayer( Graph.LayerIndex.InputLFE, inputLFEMagnitude, inputLFEMagnitude );
			app.Graph.UpdateLayer( Graph.LayerIndex.OutputTorque, outputTorque, outputTorque );

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

			if ( outputTorque < -1f )
			{
				app.Graph.SetGutterColors( protectionForegroundColor, protectionBackgroundColor, 0xFFFF0000, 0xFFFF0000 );
			}
			else if ( outputTorque > 1f )
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

			// update logitech rpm lights

			if ( settings.RacingWheelEnableLogitechRPMLights )
			{
				if ( !_logiPlayLedsNotWorking && app.Simulator.IsConnected && ( app.DirectInput.ForceFeedbackJoystick != null ) )
				{
					try
					{
						if ( !LogitechGSDK.LogiPlayLedsDInput( app.DirectInput.ForceFeedbackJoystick.NativePointer, app.Simulator.RPM, app.Simulator.ShiftLightsFirstRPM, app.Simulator.ShiftLightsShiftRPM ) )
						{
							_logiPlayLedsNotWorking = true;
						}
					}
					catch ( Exception )
					{
						_logiPlayLedsNotWorking = true;
					}

					if ( _logiPlayLedsNotWorking )
					{
						app.Logger.WriteLine( "[RacingWheel] The Logitech G SDK doesn't seem to be working, so we are temporarily disabling Logitech RPM lights support." );
					}
				}
			}

			// update algorithm preview

			if ( UpdateAlgorithmPreview )
			{
				UpdateAlgorithmPreview = false;

				_algorithmPreviewGraphBase.Reset();

				var recording = app.RecordingManager.Recording;

				for ( var i = 0; i < _algorithmProperties.GetLength( 1 ); i++ )
				{
					_algorithmProperties[ 1, i ] = 0f;
				}

				for ( var x = 0; x < _algorithmPreviewGraphBase.BitmapWidth; x++ )
				{
					if ( recording != null )
					{
						var inputTorque60Hz = recording.Data![ x ].InputTorque60Hz;
						var inputTorque500Hz = recording.Data![ x ].InputTorque500Hz;

						var outputTorque = ProcessAlgorithm( 1, inputTorque60Hz, inputTorque500Hz, 0f );

						_algorithmPreviewGraphBase.Update( inputTorque500Hz / settings.RacingWheelMaxForce, 0.5f, 0f, 0f, 1f, 0.25f, 0.25f );
						_algorithmPreviewGraphBase.Update( outputTorque, 0f, 0.5f, 0.5f, 0.25f, 1f, 1f );

						if ( outputTorque < -1f )
						{
							_algorithmPreviewGraphBase.SetGutterColors( 0, 0, 0xFFFF0000, 0xFFFF0000 );
						}
						else if ( outputTorque > 1f )
						{
							_algorithmPreviewGraphBase.SetGutterColors( 0xFFFF0000, 0xFFFF0000, 0, 0 );
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
