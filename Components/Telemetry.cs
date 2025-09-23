
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace MarvinsAIRARefactored.Components;

public class Telemetry
{
	private const string MemoryMappedFileName = "Local\\MAIRARefactoredTelemetry";
	private const int MaxStringLength = 128;

	[StructLayout( LayoutKind.Sequential, Pack = 4 )]
	public unsafe struct DataBufferStruct
	{
		public int tickCount;

		public float racingWheelStrength;
		public float racingWheelMaxForce;

		public float racingWheelAutoTorque;

		public int racingWheelAlgorithm;
		public fixed byte racingWheelAlgorithmName[ MaxStringLength ];
		public fixed float racingWheelAlgorithmSettings[ 5 ];

		public float racingWheelOutputTorque;
		public bool racingWheelOutputTorqueIsClipping;

		public bool racingWheelCrashProtectionIsActive;
		public bool racingWheelCurbProtectionIsActive;

		public bool racingWheelFadingIsActive;

		public float steeringEffectsUndersteerEffect;
		public float steeringEffectsOversteerEffect;
		public float steeringEffectsSkidSlip;

		public float pedalsClutchFrequency;
		public float pedalsClutchAmplitude;

		public float pedalsBrakeFrequency;
		public float pedalsBrakeAmplitude;

		public float pedalsThrottleFrequency;
		public float pedalsThrottleAmplitude;

		public void SetAlgorithmName( string? value )
		{
			fixed ( byte* p = racingWheelAlgorithmName )
			{
				var byteSpan = new Span<byte>( p, MaxStringLength );

				byteSpan.Clear();

				if ( string.IsNullOrEmpty( value ) ) return;

				var bytesWritten = Encoding.UTF8.GetBytes( value.AsSpan(), byteSpan );

				if ( bytesWritten >= byteSpan.Length ) byteSpan[ ^1 ] = 0; else byteSpan[ bytesWritten ] = 0;
			}
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

	public void Tick( App app )
	{
		var localization = DataContext.DataContext.Instance.Localization;
		var settings = DataContext.DataContext.Instance.Settings;

		// get the buffer to write to

		_currentBufferIndex = ( _currentBufferIndex + 1 ) % 3;

		ref var dataBuffer = ref DataStruct.GetDataBuffer( ref _data, _currentBufferIndex );

		// update the buffer

		dataBuffer.tickCount++;

		// racing wheel

		dataBuffer.racingWheelStrength = settings.RacingWheelStrength;
		dataBuffer.racingWheelMaxForce = settings.RacingWheelMaxForce;

		dataBuffer.racingWheelAutoTorque = app.RacingWheel.AutoTorque;

		dataBuffer.racingWheelAlgorithm = (int) settings.RacingWheelAlgorithm;
		dataBuffer.SetAlgorithmName( localization[ settings.RacingWheelAlgorithm.ToString() ] );

		unsafe
		{
			dataBuffer.racingWheelAlgorithmSettings[ 0 ] = 0f;
			dataBuffer.racingWheelAlgorithmSettings[ 1 ] = 0f;
			dataBuffer.racingWheelAlgorithmSettings[ 2 ] = 0f;
			dataBuffer.racingWheelAlgorithmSettings[ 3 ] = 0f;
			dataBuffer.racingWheelAlgorithmSettings[ 4 ] = 0f;

			switch ( settings.RacingWheelAlgorithm )
			{
				case RacingWheel.Algorithm.DetailBooster:
				case RacingWheel.Algorithm.DetailBoosterOn60Hz:

					dataBuffer.racingWheelAlgorithmSettings[ 0 ] = settings.RacingWheelDetailBoost;
					dataBuffer.racingWheelAlgorithmSettings[ 1 ] = settings.RacingWheelDetailBoostBias;

					break;

				case RacingWheel.Algorithm.DeltaLimiter:
				case RacingWheel.Algorithm.DeltaLimiterOn60Hz:

					dataBuffer.racingWheelAlgorithmSettings[ 0 ] = settings.RacingWheelDeltaLimit;
					dataBuffer.racingWheelAlgorithmSettings[ 1 ] = settings.RacingWheelDeltaLimiterBias;

					break;

				case RacingWheel.Algorithm.SlewAndTotalCompression:

					dataBuffer.racingWheelAlgorithmSettings[ 0 ] = settings.RacingWheelEnableSoftLimiter ? 1f : 0f;
					dataBuffer.racingWheelAlgorithmSettings[ 1 ] = settings.RacingWheelSlewCompressionThreshold;
					dataBuffer.racingWheelAlgorithmSettings[ 2 ] = settings.RacingWheelSlewCompressionRate;
					dataBuffer.racingWheelAlgorithmSettings[ 3 ] = settings.RacingWheelTotalCompressionThreshold;
					dataBuffer.racingWheelAlgorithmSettings[ 4 ] = settings.RacingWheelTotalCompressionRate;

					break;

				case RacingWheel.Algorithm.MultiAdjustmentToolkit:

					dataBuffer.racingWheelAlgorithmSettings[ 0 ] = settings.RacingWheelEnableMultiSoftLimiter ? 1f : 0f;
					dataBuffer.racingWheelAlgorithmSettings[ 1 ] = settings.RacingWheelMultiTorqueCompression;
					dataBuffer.racingWheelAlgorithmSettings[ 2 ] = settings.RacingWheelMultiSlewRateReduction;
					dataBuffer.racingWheelAlgorithmSettings[ 3 ] = settings.RacingWheelMultiDetailGain;
					dataBuffer.racingWheelAlgorithmSettings[ 4 ] = settings.RacingWheelMultiOutputSmoothing;

					break;
			}
		}

		dataBuffer.racingWheelOutputTorque = app.RacingWheel.OutputTorque;
		dataBuffer.racingWheelOutputTorqueIsClipping = ( app.RacingWheel.OutputTorque < -1f ) || ( app.RacingWheel.OutputTorque > 1f );

		dataBuffer.racingWheelCrashProtectionIsActive = app.RacingWheel.CrashProtectionIsActive;
		dataBuffer.racingWheelCurbProtectionIsActive = app.RacingWheel.CurbProtectionIsActive;

		dataBuffer.racingWheelFadingIsActive = app.RacingWheel.FadingIsActive;

		// pedals

		dataBuffer.pedalsClutchFrequency = app.Pedals.ClutchFrequency;
		dataBuffer.pedalsClutchAmplitude = app.Pedals.ClutchAmplitude;

		dataBuffer.pedalsBrakeFrequency = app.Pedals.BrakeFrequency;
		dataBuffer.pedalsBrakeAmplitude = app.Pedals.BrakeAmplitude;

		dataBuffer.pedalsThrottleFrequency = app.Pedals.ThrottleFrequency;
		dataBuffer.pedalsThrottleAmplitude = app.Pedals.ThrottleAmplitude;

		// steering effects

		dataBuffer.steeringEffectsUndersteerEffect = app.SteeringEffects.UndersteerEffect;
		dataBuffer.steeringEffectsOversteerEffect = app.SteeringEffects.OversteerEffect;
		dataBuffer.steeringEffectsSkidSlip = app.SteeringEffects.SkidSlip;

		// let SimHub know this buffer is ready for reading

		_data.version = 2;
		_data.bufferIndex = _currentBufferIndex;

		_memoryMappedFileViewAccessor?.Write( 0, ref _data );
	}
}
