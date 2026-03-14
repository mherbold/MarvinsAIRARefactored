
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace MarvinsAIRARefactored.Components;

public class Telemetry
{
	private const int Version = 7;

	private const string MemoryMappedFileName = "Local\\MAIRARefactoredTelemetry";
	private const int MaxStringLengthInBytes = 256;
	private const int MaxRacingWheelAlgorithmSettings = 7;

	[StructLayout( LayoutKind.Sequential, Pack = 4 )]
	public unsafe struct DataBufferStruct
	{
		// header telemetry

		public int tickCount;

		// output telemetry

		public float racingWheelAutoTorque;
		public float racingWheelOutputTorque;
		public bool racingWheelOutputTorqueIsClipping;
		public bool racingWheelCrashProtectionIsActive;
		public bool racingWheelCurbProtectionIsActive;
		public bool racingWheelFadingIsActive;

		public float steeringEffectsUndersteerEffect;
		public float steeringEffectsOversteerEffect;
		public float steeringEffectsSeatOfPantsEffect;
		public float steeringEffectsSkidSlip;

		public float pedalsClutchFrequency;
		public float pedalsClutchAmplitude;

		public float pedalsBrakeFrequency;
		public float pedalsBrakeAmplitude;

		public float pedalsThrottleFrequency;
		public float pedalsThrottleAmplitude;

		// racing wheel settings telemetry

		public float racingWheelStrength;
		public float racingWheelMaxForce;

		public int racingWheelAlgorithm;
		public fixed byte racingWheelAlgorithmName[ MaxStringLengthInBytes ];

		public bool racingWheelAlgorithmSoftLimiterIsEnabled;
		public fixed byte racingWheelAlgorithmSoftLimiterName[ MaxStringLengthInBytes ];
		public fixed byte racingWheelAlgorithmSoftLimiterValue[ MaxStringLengthInBytes ];

		public fixed float racingWheelAlgorithmSettings[ MaxRacingWheelAlgorithmSettings ];
		public fixed byte racingWheelAlgorithmSettingNames[ MaxRacingWheelAlgorithmSettings * MaxStringLengthInBytes ];
		public fixed byte racingWheelAlgorithmSettingValues[ MaxRacingWheelAlgorithmSettings * MaxStringLengthInBytes ];

		// steering effects settings telemetry

		public fixed byte steeringEffectsCalibrationFileName[ MaxStringLengthInBytes ];

		public float steeringEffectsUndersteerMinThreshold;
		public float steeringEffectsUndersteerMaxThreshold;
		public fixed byte steeringEffectsUndersteerVibrationPattern[ MaxStringLengthInBytes ];
		public float steeringEffectsUndersteerVibrationStrength;
		public float steeringEffectsUndersteerVibrationMinFrequency;
		public float steeringEffectsUndersteerVibrationMaxFrequency;
		public float steeringEffectsUndersteerVibrationCurve;
		public fixed byte steeringEffectsUndersteerForceDirection[ MaxStringLengthInBytes ];
		public float steeringEffectsUndersteerForceStrength;
		public float steeringEffectsUndersteerForceCurve;
		public float steeringEffectsUndersteerPedalVibrationMinFrequency;
		public float steeringEffectsUndersteerPedalVibrationMaxFrequency;
		public float steeringEffectsUndersteerPedalVibrationCurve;

		public float steeringEffectsOversteerMinThreshold;
		public float steeringEffectsOversteerMaxThreshold;
		public fixed byte steeringEffectsOversteerVibrationPattern[ MaxStringLengthInBytes ];
		public float steeringEffectsOversteerVibrationStrength;
		public float steeringEffectsOversteerVibrationMinFrequency;
		public float steeringEffectsOversteerVibrationMaxFrequency;
		public float steeringEffectsOversteerVibrationCurve;
		public fixed byte steeringEffectsOversteerForceDirection[ MaxStringLengthInBytes ];
		public float steeringEffectsOversteerForceStrength;
		public float steeringEffectsOversteerForceCurve;
		public float steeringEffectsOversteerPedalVibrationMinFrequency;
		public float steeringEffectsOversteerPedalVibrationMaxFrequency;
		public float steeringEffectsOversteerPedalVibrationCurve;

		public float steeringEffectsSeatOfPantsMinThreshold;
		public float steeringEffectsSeatOfPantsMaxThreshold;
		public fixed byte steeringEffectsSeatOfPantsAlgorithm[ MaxStringLengthInBytes ];
		public fixed byte steeringEffectsSeatOfPantsVibrationPattern[ MaxStringLengthInBytes ];
		public float steeringEffectsSeatOfPantsVibrationStrength;
		public float steeringEffectsSeatOfPantsVibrationMinFrequency;
		public float steeringEffectsSeatOfPantsVibrationMaxFrequency;
		public float steeringEffectsSeatOfPantsVibrationCurve;
		public fixed byte steeringEffectsSeatOfPantsForceDirection[ MaxStringLengthInBytes ];
		public float steeringEffectsSeatOfPantsForceStrength;
		public float steeringEffectsSeatOfPantsForceCurve;
		public float steeringEffectsSeatOfPantsPedalVibrationMinFrequency;
		public float steeringEffectsSeatOfPantsPedalVibrationMaxFrequency;
		public float steeringEffectsSeatOfPantsPedalVibrationCurve;

		// string setters

		public void SetRacingWheelAlgorithmName( string? value )
		{
			fixed ( byte* bytePtr = racingWheelAlgorithmName )
			{
				WriteString( bytePtr, 0, MaxStringLengthInBytes, value );
			}
		}

		public void SetRacingWheelAlgorithmSoftLimiterName( string? value )
		{
			fixed ( byte* bytePtr = racingWheelAlgorithmSoftLimiterName )
			{
				WriteString( bytePtr, 0, MaxStringLengthInBytes, value );
			}
		}

		public void SetRacingWheelAlgorithmSoftLimiterValue( string? value )
		{
			fixed ( byte* bytePtr = racingWheelAlgorithmSoftLimiterValue )
			{
				WriteString( bytePtr, 0, MaxStringLengthInBytes, value );
			}
		}

		public void SetRacingWheelAlgorithmSettingName( int index, string? value )
		{
			if ( index < 0 || index >= MaxRacingWheelAlgorithmSettings ) return;

			fixed ( byte* bytePtr = racingWheelAlgorithmSettingNames )
			{
				WriteString( bytePtr, index, MaxStringLengthInBytes, value );
			}
		}

		public void SetRacingWheelAlgorithmSettingValue( int index, string? value )
		{
			if ( index < 0 || index >= MaxRacingWheelAlgorithmSettings ) return;

			fixed ( byte* bytePtr = racingWheelAlgorithmSettingValues )
			{
				WriteString( bytePtr, index, MaxStringLengthInBytes, value );
			}
		}

		public void SetSteeringEffectsCalibrationFileName( string? value )
		{
			fixed ( byte* bytePtr = steeringEffectsCalibrationFileName )
			{
				WriteString( bytePtr, 0, MaxStringLengthInBytes, value );
			}
		}

		public void SetSteeringEffectsUndersteerVibrationPattern( string? value )
		{
			fixed ( byte* bytePtr = steeringEffectsUndersteerVibrationPattern )
			{
				WriteString( bytePtr, 0, MaxStringLengthInBytes, value );
			}
		}

		public void SetSteeringEffectsUndersteerForceDirection( string? value )
		{
			fixed ( byte* bytePtr = steeringEffectsUndersteerForceDirection )
			{
				WriteString( bytePtr, 0, MaxStringLengthInBytes, value );
			}
		}

		public void SetSteeringEffectsOversteerVibrationPattern( string? value )
		{
			fixed ( byte* bytePtr = steeringEffectsOversteerVibrationPattern )
			{
				WriteString( bytePtr, 0, MaxStringLengthInBytes, value );
			}
		}

		public void SetSteeringEffectsOversteerForceDirection( string? value )
		{
			fixed ( byte* bytePtr = steeringEffectsOversteerForceDirection )
			{
				WriteString( bytePtr, 0, MaxStringLengthInBytes, value );
			}
		}

		public void SetSteeringEffectsSeatOfPantsAlgorithm( string? value )
		{
			fixed ( byte* bytePtr = steeringEffectsSeatOfPantsAlgorithm )
			{
				WriteString( bytePtr, 0, MaxStringLengthInBytes, value );
			}
		}

		public void SetSteeringEffectsSeatOfPantsVibrationPattern( string? value )
		{
			fixed ( byte* bytePtr = steeringEffectsSeatOfPantsVibrationPattern )
			{
				WriteString( bytePtr, 0, MaxStringLengthInBytes, value );
			}
		}

		public void SetSteeringEffectsSeatOfPantsForceDirection( string? value )
		{
			fixed ( byte* bytePtr = steeringEffectsSeatOfPantsForceDirection )
			{
				WriteString( bytePtr, 0, MaxStringLengthInBytes, value );
			}
		}

		public static unsafe void WriteString( byte* bytePtr, int index, int capacity, string? value )
		{
			if ( bytePtr == null || capacity <= 0 ) return;

			var offset = index * capacity;

			if ( string.IsNullOrEmpty( value ) )
			{
				bytePtr[ offset ] = 0;
				return;
			}

			var bytes = Encoding.UTF8.GetBytes( value );

			var length = Math.Min( bytes.Length, capacity - 1 );

			Marshal.Copy( bytes, 0, (IntPtr) bytePtr + offset, length );

			bytePtr[ offset + length ] = 0;
		}
	}

	[StructLayout( LayoutKind.Sequential, Pack = 4 )]
	public unsafe struct DataStruct
	{
		public int version;
		public int bufferIndex;

		public DataBufferStruct buffer0;
		public DataBufferStruct buffer1;
		public DataBufferStruct buffer2;

		public static ref DataBufferStruct GetDataBuffer( ref DataStruct dataStruct, int index )
		{
			switch ( index )
			{
				case 0: return ref dataStruct.buffer0;
				case 1: return ref dataStruct.buffer1;
				default: return ref dataStruct.buffer2;
			}
		}
	}

	private DataStruct _data = new();
	private int _currentBufferIndex = 0;
	private int _settingsUpdatesRemaining = 0;

	private MemoryMappedFile? _memoryMappedFile = null;
	private MemoryMappedViewAccessor? _memoryMappedFileViewAccessor = null;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Telemetry] Initialize >>>" );

		var sizeOfTelemetryData = Marshal.SizeOf<DataStruct>();

		_memoryMappedFile = MemoryMappedFile.CreateOrOpen( MemoryMappedFileName, sizeOfTelemetryData );
		_memoryMappedFileViewAccessor = _memoryMappedFile.CreateViewAccessor();

		app.Logger.WriteLine( "[Telemetry] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Telemetry] Shutdown >>>" );

		_memoryMappedFileViewAccessor = null;
		_memoryMappedFile = null;

		app.Logger.WriteLine( "[Telemetry] <<< Shutdown" );
	}

	public void UpdateSettings()
	{
		_settingsUpdatesRemaining = 3;
	}

	public void Tick( App app )
	{
		var localization = DataContext.DataContext.Instance.Localization;
		var settings = DataContext.DataContext.Instance.Settings;

		// get the buffer to write to

		_currentBufferIndex = ( _currentBufferIndex + 1 ) % 3;

		ref var dataBuffer = ref DataStruct.GetDataBuffer( ref _data, _currentBufferIndex );

		// header telemetry

		dataBuffer.tickCount++;

		// output telemetry

		dataBuffer.racingWheelAutoTorque = app.RacingWheel.AutoTorque;
		dataBuffer.racingWheelOutputTorque = app.RacingWheel.OutputTorque;
		dataBuffer.racingWheelOutputTorqueIsClipping = ( app.RacingWheel.OutputTorque < -1f ) || ( app.RacingWheel.OutputTorque > 1f );
		dataBuffer.racingWheelCrashProtectionIsActive = app.RacingWheel.CrashProtectionIsActive;
		dataBuffer.racingWheelCurbProtectionIsActive = app.RacingWheel.CurbProtectionIsActive;
		dataBuffer.racingWheelFadingIsActive = app.RacingWheel.FadingIsActive;

		dataBuffer.steeringEffectsUndersteerEffect = app.SteeringEffects.UndersteerEffect;
		dataBuffer.steeringEffectsOversteerEffect = app.SteeringEffects.OversteerEffect;
		dataBuffer.steeringEffectsSeatOfPantsEffect = app.SteeringEffects.SeatOfPantsEffect;
		dataBuffer.steeringEffectsSkidSlip = app.SteeringEffects.SkidSlip;

		dataBuffer.pedalsClutchFrequency = app.Pedals.ClutchFrequency;
		dataBuffer.pedalsClutchAmplitude = app.Pedals.ClutchAmplitude;

		dataBuffer.pedalsBrakeFrequency = app.Pedals.BrakeFrequency;
		dataBuffer.pedalsBrakeAmplitude = app.Pedals.BrakeAmplitude;

		dataBuffer.pedalsThrottleFrequency = app.Pedals.ThrottleFrequency;
		dataBuffer.pedalsThrottleAmplitude = app.Pedals.ThrottleAmplitude;

		if ( _settingsUpdatesRemaining > 0 )
		{
			// update settings 3 times because we have 3 buffers to fill then we no longer need to update settings

			_settingsUpdatesRemaining--;

			// racing wheel settings telemetry

			dataBuffer.racingWheelStrength = settings.RacingWheelStrength;
			dataBuffer.racingWheelMaxForce = settings.RacingWheelMaxForce;

			dataBuffer.racingWheelAlgorithm = (int) settings.RacingWheelAlgorithm;
			dataBuffer.SetRacingWheelAlgorithmName( localization[ settings.RacingWheelAlgorithm.ToString() ] );

			dataBuffer.racingWheelAlgorithmSoftLimiterIsEnabled = settings.RacingWheelEnableSoftLimiter;
			dataBuffer.SetRacingWheelAlgorithmSoftLimiterName( localization[ "SoftClipping" ] );
			dataBuffer.SetRacingWheelAlgorithmSoftLimiterValue( settings.RacingWheelEnableSoftLimiter ? localization[ "ON" ] : localization[ "OFF" ] );

			unsafe
			{
				for ( var index = 0; index < MaxRacingWheelAlgorithmSettings; index++ )
				{
					dataBuffer.racingWheelAlgorithmSettings[ index ] = 0f;

					dataBuffer.SetRacingWheelAlgorithmSettingName( index, null );
					dataBuffer.SetRacingWheelAlgorithmSettingValue( index, null );
				}

				switch ( settings.RacingWheelAlgorithm )
				{
					case RacingWheel.Algorithm.DetailBooster:
					case RacingWheel.Algorithm.DetailBoosterOn60Hz:

						dataBuffer.racingWheelAlgorithmSettings[ 0 ] = settings.RacingWheelDetailBoost;
						dataBuffer.racingWheelAlgorithmSettings[ 1 ] = settings.RacingWheelDetailBoostBias;

						dataBuffer.SetRacingWheelAlgorithmSettingName( 0, localization[ "DetailBoost" ] );
						dataBuffer.SetRacingWheelAlgorithmSettingName( 1, localization[ "DetailBoostBias" ] );

						dataBuffer.SetRacingWheelAlgorithmSettingValue( 0, settings.RacingWheelDetailBoostString );
						dataBuffer.SetRacingWheelAlgorithmSettingValue( 1, settings.RacingWheelDetailBoostBiasString );

						break;

					case RacingWheel.Algorithm.DeltaLimiter:
					case RacingWheel.Algorithm.DeltaLimiterOn60Hz:

						dataBuffer.racingWheelAlgorithmSettings[ 0 ] = settings.RacingWheelDeltaLimit;
						dataBuffer.racingWheelAlgorithmSettings[ 1 ] = settings.RacingWheelDeltaLimiterBias;

						dataBuffer.SetRacingWheelAlgorithmSettingName( 0, localization[ "DeltaLimit" ] );
						dataBuffer.SetRacingWheelAlgorithmSettingName( 1, localization[ "DeltaLimiterBias" ] );

						dataBuffer.SetRacingWheelAlgorithmSettingValue( 0, settings.RacingWheelDeltaLimitString );
						dataBuffer.SetRacingWheelAlgorithmSettingValue( 1, settings.RacingWheelDeltaLimiterBiasString );

						break;

					case RacingWheel.Algorithm.SlewAndTotalCompression:

						dataBuffer.racingWheelAlgorithmSettings[ 0 ] = settings.RacingWheelSlewCompressionThreshold;
						dataBuffer.racingWheelAlgorithmSettings[ 1 ] = settings.RacingWheelSlewCompressionRate;
						dataBuffer.racingWheelAlgorithmSettings[ 2 ] = settings.RacingWheelTotalCompressionThreshold;
						dataBuffer.racingWheelAlgorithmSettings[ 3 ] = settings.RacingWheelTotalCompressionRate;

						dataBuffer.SetRacingWheelAlgorithmSettingName( 0, localization[ "SlewCompressionThreshold" ] );
						dataBuffer.SetRacingWheelAlgorithmSettingName( 1, localization[ "SlewCompressionRate" ] );
						dataBuffer.SetRacingWheelAlgorithmSettingName( 2, localization[ "TotalCompressionThreshold" ] );
						dataBuffer.SetRacingWheelAlgorithmSettingName( 3, localization[ "TotalCompressionRate" ] );

						dataBuffer.SetRacingWheelAlgorithmSettingValue( 0, settings.RacingWheelSlewCompressionThresholdString );
						dataBuffer.SetRacingWheelAlgorithmSettingValue( 1, settings.RacingWheelSlewCompressionRateString );
						dataBuffer.SetRacingWheelAlgorithmSettingValue( 2, settings.RacingWheelTotalCompressionThresholdString );
						dataBuffer.SetRacingWheelAlgorithmSettingValue( 3, settings.RacingWheelTotalCompressionRateString );

						break;

					case RacingWheel.Algorithm.MultiAdjustmentToolkit:

						switch ( settings.RacingWheelMultiFFBSourceSelection )
						{
							case RacingWheel.MultiFFBSourceOptions.Native60Hz:
								dataBuffer.racingWheelAlgorithmSettings[ 0 ] = 0f;
								break;

							case RacingWheel.MultiFFBSourceOptions.HybridVariable30:
								dataBuffer.racingWheelAlgorithmSettings[ 0 ] = 1f;
								break;

							case RacingWheel.MultiFFBSourceOptions.Hybrid10:
								dataBuffer.racingWheelAlgorithmSettings[ 0 ] = 2f;
								break;

							case RacingWheel.MultiFFBSourceOptions.Native360Hz:
								dataBuffer.racingWheelAlgorithmSettings[ 0 ] = 3f;
								break;
						}

						dataBuffer.racingWheelAlgorithmSettings[ 1 ] = settings.RacingWheelMulti360HzDetail;
						dataBuffer.racingWheelAlgorithmSettings[ 2 ] = settings.RacingWheelMultiTorqueCompression;
						dataBuffer.racingWheelAlgorithmSettings[ 3 ] = ( settings.RacingWheelMultiEnableSlewPeakMode ) ? 1f : 0f;
						dataBuffer.racingWheelAlgorithmSettings[ 4 ] = settings.RacingWheelMultiSlewRateReduction;
						dataBuffer.racingWheelAlgorithmSettings[ 5 ] = settings.RacingWheelMultiDetailGain;
						dataBuffer.racingWheelAlgorithmSettings[ 6 ] = settings.RacingWheelMultiOutputSmoothing;

						dataBuffer.SetRacingWheelAlgorithmSettingName( 0, localization[ "FFBSource" ] );
						dataBuffer.SetRacingWheelAlgorithmSettingName( 1, localization[ "Multi360HzDetail" ] );
						dataBuffer.SetRacingWheelAlgorithmSettingName( 2, localization[ "TorqueCompression" ] );
						dataBuffer.SetRacingWheelAlgorithmSettingName( 3, localization[ "SlewPeakMode" ] );
						dataBuffer.SetRacingWheelAlgorithmSettingName( 4, localization[ "SlewRateReduction" ] );
						dataBuffer.SetRacingWheelAlgorithmSettingName( 5, localization[ "DetailGain" ] );
						dataBuffer.SetRacingWheelAlgorithmSettingName( 6, localization[ "OutputSmoothing" ] );

						switch ( settings.RacingWheelMultiFFBSourceSelection )
						{
							case RacingWheel.MultiFFBSourceOptions.Native60Hz:
								dataBuffer.SetRacingWheelAlgorithmSettingValue( 0, localization[ "Native60Hz" ] );
								break;

							case RacingWheel.MultiFFBSourceOptions.HybridVariable30:
								dataBuffer.SetRacingWheelAlgorithmSettingValue( 0, localization[ "HybridVariable30" ] );
								break;

							case RacingWheel.MultiFFBSourceOptions.Hybrid10:
								dataBuffer.SetRacingWheelAlgorithmSettingValue( 0, localization[ "Hybrid10" ] );
								break;

							case RacingWheel.MultiFFBSourceOptions.Native360Hz:
								dataBuffer.SetRacingWheelAlgorithmSettingValue( 0, localization[ "Native360Hz" ] );
								break;
						}

						dataBuffer.SetRacingWheelAlgorithmSettingValue( 1, settings.RacingWheelMulti360HzDetailString );
						dataBuffer.SetRacingWheelAlgorithmSettingValue( 2, settings.RacingWheelMultiTorqueCompressionString );
						dataBuffer.SetRacingWheelAlgorithmSettingValue( 3, settings.RacingWheelMultiEnableSlewPeakMode ? localization[ "ON" ] : localization[ "OFF" ] );
						dataBuffer.SetRacingWheelAlgorithmSettingValue( 4, settings.RacingWheelMultiSlewRateReductionString );
						dataBuffer.SetRacingWheelAlgorithmSettingValue( 5, settings.RacingWheelMultiDetailGainString );
						dataBuffer.SetRacingWheelAlgorithmSettingValue( 6, settings.RacingWheelMultiOutputSmoothingString );

						break;
				}
			}

			// steering effects settings telemetry

			dataBuffer.SetSteeringEffectsCalibrationFileName( settings.SteeringEffectsCalibrationFileName.ToString() );

			dataBuffer.steeringEffectsUndersteerMinThreshold = settings.SteeringEffectsUndersteerMinimumThreshold;
			dataBuffer.steeringEffectsUndersteerMaxThreshold = settings.SteeringEffectsUndersteerMaximumThreshold;
			dataBuffer.SetSteeringEffectsUndersteerVibrationPattern( localization[ settings.SteeringEffectsUndersteerWheelVibrationPattern.ToString() ] );
			dataBuffer.steeringEffectsUndersteerVibrationStrength = settings.SteeringEffectsUndersteerWheelVibrationStrength;
			dataBuffer.steeringEffectsUndersteerVibrationMinFrequency = settings.SteeringEffectsUndersteerWheelVibrationMinimumFrequency;
			dataBuffer.steeringEffectsUndersteerVibrationMaxFrequency = settings.SteeringEffectsUndersteerWheelVibrationMaximumFrequency;
			dataBuffer.steeringEffectsUndersteerVibrationCurve = settings.SteeringEffectsUndersteerWheelVibrationCurve;
			dataBuffer.SetSteeringEffectsUndersteerForceDirection( localization[ settings.SteeringEffectsUndersteerWheelConstantForceDirection.ToString() ] );
			dataBuffer.steeringEffectsUndersteerForceStrength = settings.SteeringEffectsUndersteerWheelConstantForceStrength;
			dataBuffer.steeringEffectsUndersteerForceCurve = settings.SteeringEffectsUndersteerWheelConstantForceCurve;
			dataBuffer.steeringEffectsUndersteerPedalVibrationMinFrequency = settings.SteeringEffectsUndersteerPedalVibrationMinimumFrequency;
			dataBuffer.steeringEffectsUndersteerPedalVibrationMaxFrequency = settings.SteeringEffectsUndersteerPedalVibrationMaximumFrequency;
			dataBuffer.steeringEffectsUndersteerPedalVibrationCurve = settings.SteeringEffectsUndersteerPedalVibrationCurve;

			dataBuffer.steeringEffectsOversteerMinThreshold = settings.SteeringEffectsOversteerMinimumThreshold;
			dataBuffer.steeringEffectsOversteerMaxThreshold = settings.SteeringEffectsOversteerMaximumThreshold;
			dataBuffer.SetSteeringEffectsOversteerVibrationPattern( localization[ settings.SteeringEffectsOversteerWheelVibrationPattern.ToString() ] );
			dataBuffer.steeringEffectsOversteerVibrationStrength = settings.SteeringEffectsOversteerWheelVibrationStrength;
			dataBuffer.steeringEffectsOversteerVibrationMinFrequency = settings.SteeringEffectsOversteerWheelVibrationMinimumFrequency;
			dataBuffer.steeringEffectsOversteerVibrationMaxFrequency = settings.SteeringEffectsOversteerWheelVibrationMaximumFrequency;
			dataBuffer.steeringEffectsOversteerVibrationCurve = settings.SteeringEffectsOversteerWheelVibrationCurve;
			dataBuffer.SetSteeringEffectsOversteerForceDirection( localization[ settings.SteeringEffectsOversteerWheelConstantForceDirection.ToString() ] );
			dataBuffer.steeringEffectsOversteerForceStrength = settings.SteeringEffectsOversteerWheelConstantForceStrength;
			dataBuffer.steeringEffectsOversteerForceCurve = settings.SteeringEffectsOversteerWheelConstantForceCurve;
			dataBuffer.steeringEffectsOversteerPedalVibrationMinFrequency = settings.SteeringEffectsOversteerPedalVibrationMinimumFrequency;
			dataBuffer.steeringEffectsOversteerPedalVibrationMaxFrequency = settings.SteeringEffectsOversteerPedalVibrationMaximumFrequency;
			dataBuffer.steeringEffectsOversteerPedalVibrationCurve = settings.SteeringEffectsOversteerPedalVibrationCurve;

			dataBuffer.steeringEffectsSeatOfPantsMinThreshold = settings.SteeringEffectsSeatOfPantsMinimumThreshold;
			dataBuffer.steeringEffectsSeatOfPantsMaxThreshold = settings.SteeringEffectsSeatOfPantsMaximumThreshold;

			switch ( settings.SteeringEffectsSeatOfPantsAlgorithm )
			{
				case SteeringEffects.SeatOfPantsAlgorithm.YAcceleration:
					dataBuffer.SetSteeringEffectsSeatOfPantsAlgorithm( localization[ "LateralAcceleration" ] );
					break;

				case SteeringEffects.SeatOfPantsAlgorithm.YVelocity:
					dataBuffer.SetSteeringEffectsSeatOfPantsAlgorithm( localization[ "LateralVelocity" ] );
					break;

				case SteeringEffects.SeatOfPantsAlgorithm.YVelocityOverXVelocity:
					dataBuffer.SetSteeringEffectsSeatOfPantsAlgorithm( localization[ "RatioOfVelocities" ] );
					break;
			}

			dataBuffer.SetSteeringEffectsSeatOfPantsVibrationPattern( localization[ settings.SteeringEffectsSeatOfPantsWheelVibrationPattern.ToString() ] );
			dataBuffer.steeringEffectsSeatOfPantsVibrationStrength = settings.SteeringEffectsSeatOfPantsWheelVibrationStrength;
			dataBuffer.steeringEffectsSeatOfPantsVibrationMinFrequency = settings.SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequency;
			dataBuffer.steeringEffectsSeatOfPantsVibrationMaxFrequency = settings.SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequency;
			dataBuffer.steeringEffectsSeatOfPantsVibrationCurve = settings.SteeringEffectsSeatOfPantsWheelVibrationCurve;
			dataBuffer.SetSteeringEffectsSeatOfPantsForceDirection( localization[ settings.SteeringEffectsSeatOfPantsWheelConstantForceDirection.ToString() ] );
			dataBuffer.steeringEffectsSeatOfPantsForceStrength = settings.SteeringEffectsSeatOfPantsWheelConstantForceStrength;
			dataBuffer.steeringEffectsSeatOfPantsForceCurve = settings.SteeringEffectsSeatOfPantsWheelConstantForceCurve;
			dataBuffer.steeringEffectsSeatOfPantsPedalVibrationMinFrequency = settings.SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequency;
			dataBuffer.steeringEffectsSeatOfPantsPedalVibrationMaxFrequency = settings.SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequency;
			dataBuffer.steeringEffectsSeatOfPantsPedalVibrationCurve = settings.SteeringEffectsSeatOfPantsPedalVibrationCurve;
		}

		// let SimHub know this buffer is ready for reading

		_data.version = Version;
		_data.bufferIndex = _currentBufferIndex;

		_memoryMappedFileViewAccessor?.Write( 0, ref _data );
	}
}
