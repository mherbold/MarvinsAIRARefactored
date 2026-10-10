
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using MarvinsAIRARefactored.FFB;

namespace MarvinsAIRARefactored.Components;

public class Telemetry
{
	private const int Version = 9;

	private const string MemoryMappedFileName = "Local\\MAIRARefactoredTelemetry";
	private const int MaxStringLengthInBytes = 256;
	private const int MaxGraphParameters = 50;
	private const int MaxGraphParameterStringLengthInBytes = 64;

	private const int MemoryMappedFileRetryIntervalInMilliseconds = 5000;

	private const string GraphParameterTrueValue = "true";
	private const string GraphParameterFalseValue = "false";

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

		// steering effects settings telemetry

		public fixed byte steeringEffectsCalibrationFileName[ MaxStringLengthInBytes ];

		public float steeringEffectsUndersteerMinThreshold;
		public float steeringEffectsUndersteerMaxThreshold;
		public float steeringEffectsUndersteerPedalVibrationMinFrequency;
		public float steeringEffectsUndersteerPedalVibrationMaxFrequency;
		public float steeringEffectsUndersteerPedalVibrationCurve;

		public float steeringEffectsOversteerMinThreshold;
		public float steeringEffectsOversteerMaxThreshold;
		public float steeringEffectsOversteerPedalVibrationMinFrequency;
		public float steeringEffectsOversteerPedalVibrationMaxFrequency;
		public float steeringEffectsOversteerPedalVibrationCurve;

		public float steeringEffectsSeatOfPantsMinThreshold;
		public float steeringEffectsSeatOfPantsMaxThreshold;
		public fixed byte steeringEffectsSeatOfPantsAlgorithm[ MaxStringLengthInBytes ];
		public float steeringEffectsSeatOfPantsPedalVibrationMinFrequency;
		public float steeringEffectsSeatOfPantsPedalVibrationMaxFrequency;
		public float steeringEffectsSeatOfPantsPedalVibrationCurve;

		public bool soundsWheelLockIsPlaying;
		public float soundsWheelLockVolume;
		public bool soundsWheelSpinIsPlaying;
		public float soundsWheelSpinVolume;

		// ffb graph settings telemetry (pinned settings of the active graph as a flat list, in the display order of
		// the FFB graph settings section) - parameter i of every array below is at index i

		public fixed byte graphName[ MaxStringLengthInBytes ];

		public int graphParameterCount;

		// the per-parameter strings are short (names and values), so they use a smaller slot than the other strings
		// to keep the buffer small - longer text is cut at a character boundary (see WriteString)

		public fixed byte graphParameterNodeNames[ MaxGraphParameters * MaxGraphParameterStringLengthInBytes ];
		public fixed byte graphParameterNodeModuleTypes[ MaxGraphParameters * MaxGraphParameterStringLengthInBytes ];
		public fixed byte graphParameterNames[ MaxGraphParameters * MaxGraphParameterStringLengthInBytes ];
		public fixed byte graphParameterRawValues[ MaxGraphParameters * MaxGraphParameterStringLengthInBytes ];
		public fixed byte graphParameterFormattedValues[ MaxGraphParameters * MaxGraphParameterStringLengthInBytes ];

		// string setters

		public void SetSteeringEffectsCalibrationFileName( string? value )
		{
			fixed ( byte* bytePtr = steeringEffectsCalibrationFileName )
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

		public void SetGraphName( string? value )
		{
			fixed ( byte* bytePtr = graphName )
			{
				WriteString( bytePtr, 0, MaxStringLengthInBytes, value );
			}
		}

		public void SetGraphParameterNodeName( int parameterIndex, string? value )
		{
			if ( parameterIndex < 0 || parameterIndex >= MaxGraphParameters ) return;

			fixed ( byte* bytePtr = graphParameterNodeNames )
			{
				WriteString( bytePtr, parameterIndex, MaxGraphParameterStringLengthInBytes, value );
			}
		}

		public void SetGraphParameterNodeModuleType( int parameterIndex, string? value )
		{
			if ( parameterIndex < 0 || parameterIndex >= MaxGraphParameters ) return;

			fixed ( byte* bytePtr = graphParameterNodeModuleTypes )
			{
				WriteString( bytePtr, parameterIndex, MaxGraphParameterStringLengthInBytes, value );
			}
		}

		public void SetGraphParameterName( int parameterIndex, string? value )
		{
			if ( parameterIndex < 0 || parameterIndex >= MaxGraphParameters ) return;

			fixed ( byte* bytePtr = graphParameterNames )
			{
				WriteString( bytePtr, parameterIndex, MaxGraphParameterStringLengthInBytes, value );
			}
		}

		public void SetGraphParameterRawValue( int parameterIndex, string? value )
		{
			if ( parameterIndex < 0 || parameterIndex >= MaxGraphParameters ) return;

			fixed ( byte* bytePtr = graphParameterRawValues )
			{
				WriteString( bytePtr, parameterIndex, MaxGraphParameterStringLengthInBytes, value );
			}
		}

		public void SetGraphParameterFormattedValue( int parameterIndex, string? value )
		{
			if ( parameterIndex < 0 || parameterIndex >= MaxGraphParameters ) return;

			fixed ( byte* bytePtr = graphParameterFormattedValues )
			{
				WriteString( bytePtr, parameterIndex, MaxGraphParameterStringLengthInBytes, value );
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

			// when the text is cut, back off to the start of the cut character so a multi-byte UTF-8 sequence is never
			// left half-written (continuation bytes are 10xxxxxx)
			if ( length < bytes.Length )
			{
				while ( ( length > 0 ) && ( ( bytes[ length ] & 0xC0 ) == 0x80 ) )
				{
					length--;
				}
			}

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

	private long _nextMemoryMappedFileAttemptTickCount = 0;
	private bool _memoryMappedFileTooSmallLogged = false;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Telemetry] Initialize >>>" );

		TryOpenMemoryMappedFile( app );

		app.Logger.WriteLine( "[Telemetry] <<< Initialize" );
	}

	private void TryOpenMemoryMappedFile( App app )
	{
		_nextMemoryMappedFileAttemptTickCount = Environment.TickCount64 + MemoryMappedFileRetryIntervalInMilliseconds;

		// Unsafe.SizeOf (the managed layout) is what MemoryMappedViewAccessor.Write actually copies - Marshal.SizeOf
		// can't size this struct (the marshaler rejects the nested large buffers) and would over-count bools as 4 bytes
		var sizeOfTelemetryData = Unsafe.SizeOf<DataStruct>();

		var memoryMappedFile = MemoryMappedFile.CreateOrOpen( MemoryMappedFileName, sizeOfTelemetryData );
		var memoryMappedFileViewAccessor = memoryMappedFile.CreateViewAccessor();

		// CreateOrOpen returns the EXISTING mapping when another process (e.g. SimHub) still holds one open from an
		// older, smaller layout - it can't be resized, so writing our struct would run past its end. Let go of it and
		// try again every few seconds (from Tick) - the SimHub plugin releases a mapping that is too small for it, so
		// once nothing holds the old one open, the next attempt creates a fresh mapping of the right size.
		if ( memoryMappedFileViewAccessor.Capacity < sizeOfTelemetryData )
		{
			if ( !_memoryMappedFileTooSmallLogged )
			{
				app.Logger.WriteLine( $"[Telemetry] Existing memory mapped file is too small ({memoryMappedFileViewAccessor.Capacity} < {sizeOfTelemetryData} bytes) - another process has an older layout open; retrying every {MemoryMappedFileRetryIntervalInMilliseconds / 1000} seconds" );

				_memoryMappedFileTooSmallLogged = true;
			}

			memoryMappedFileViewAccessor.Dispose();
			memoryMappedFile.Dispose();

			return;
		}

		if ( _memoryMappedFileTooSmallLogged )
		{
			app.Logger.WriteLine( "[Telemetry] Memory mapped file opened after retrying" );

			_memoryMappedFileTooSmallLogged = false;
		}

		_memoryMappedFile = memoryMappedFile;
		_memoryMappedFileViewAccessor = memoryMappedFileViewAccessor;
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

		//Wheel lock and wheel spin sounds status

		dataBuffer.soundsWheelLockIsPlaying = app.Sounds.SoundEffects[Sounds.SoundEffectType.WheelLock].IsPlaying;
		dataBuffer.soundsWheelLockVolume = app.Sounds.SoundEffects[Sounds.SoundEffectType.WheelLock].Volume;
		dataBuffer.soundsWheelSpinIsPlaying = app.Sounds.SoundEffects[Sounds.SoundEffectType.WheelSpin].IsPlaying;
		dataBuffer.soundsWheelSpinVolume = app.Sounds.SoundEffects[Sounds.SoundEffectType.WheelSpin].Volume;

		if ( _settingsUpdatesRemaining > 0 )
		{
			// update settings 3 times because we have 3 buffers to fill then we no longer need to update settings

			_settingsUpdatesRemaining--;

			// racing wheel settings telemetry

			dataBuffer.racingWheelStrength = settings.RacingWheelStrength;
			dataBuffer.racingWheelMaxForce = settings.RacingWheelMaxForce;

			// steering effects settings telemetry

			dataBuffer.SetSteeringEffectsCalibrationFileName( app.SteeringEffects.CalibrationFileName );

			dataBuffer.steeringEffectsUndersteerMinThreshold = settings.SteeringEffectsUndersteerMinimumThreshold;
			dataBuffer.steeringEffectsUndersteerMaxThreshold = settings.SteeringEffectsUndersteerMaximumThreshold;
			dataBuffer.steeringEffectsUndersteerPedalVibrationMinFrequency = settings.SteeringEffectsUndersteerPedalVibrationMinimumFrequency;
			dataBuffer.steeringEffectsUndersteerPedalVibrationMaxFrequency = settings.SteeringEffectsUndersteerPedalVibrationMaximumFrequency;
			dataBuffer.steeringEffectsUndersteerPedalVibrationCurve = settings.SteeringEffectsUndersteerPedalVibrationCurve;

			dataBuffer.steeringEffectsOversteerMinThreshold = settings.SteeringEffectsOversteerMinimumThreshold;
			dataBuffer.steeringEffectsOversteerMaxThreshold = settings.SteeringEffectsOversteerMaximumThreshold;
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

			dataBuffer.steeringEffectsSeatOfPantsPedalVibrationMinFrequency = settings.SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequency;
			dataBuffer.steeringEffectsSeatOfPantsPedalVibrationMaxFrequency = settings.SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequency;
			dataBuffer.steeringEffectsSeatOfPantsPedalVibrationCurve = settings.SteeringEffectsSeatOfPantsPedalVibrationCurve;

			// ffb graph settings telemetry

			UpdateGraphParameterTelemetry( ref dataBuffer );
		}

		// let SimHub know this buffer is ready for reading

		_data.version = Version;
		_data.bufferIndex = _currentBufferIndex;

		if ( ( _memoryMappedFileViewAccessor == null ) && ( Environment.TickCount64 >= _nextMemoryMappedFileAttemptTickCount ) )
		{
			TryOpenMemoryMappedFile( app );
		}

		_memoryMappedFileViewAccessor?.Write( 0, ref _data );
	}

	private static void UpdateGraphParameterTelemetry( ref DataBufferStruct dataBuffer )
	{
		var graphViewModel = DataContext.DataContext.Instance.RacingWheelGraphViewModel;

		// the active graph's user-facing name (built-ins localized), as shown in the graph selector

		dataBuffer.SetGraphName( FFBGraphViewModel.GraphDisplayName( graphViewModel.GraphName, graphViewModel.IsFFBGraphBuiltIn ) );

		// blank every slot first so parameters that no longer exist (or were unpinned) don't linger

		for ( var parameterIndex = 0; parameterIndex < MaxGraphParameters; parameterIndex++ )
		{
			dataBuffer.SetGraphParameterNodeName( parameterIndex, null );
			dataBuffer.SetGraphParameterNodeModuleType( parameterIndex, null );
			dataBuffer.SetGraphParameterName( parameterIndex, null );
			dataBuffer.SetGraphParameterRawValue( parameterIndex, null );
			dataBuffer.SetGraphParameterFormattedValue( parameterIndex, null );
		}

		// PinnedGroups is exactly what the FFB graph settings section shows (whether or not it is currently visible):
		// only nodes with pins, in display order, each with its pinned entries in display order (a pinned Enabled
		// toggle first) - flattened here into one list, stopping once MaxGraphParameters is reached. Nodes whose
		// output doesn't reach the Output module are skipped, even when they have pinned settings.

		var contributingModuleIds = graphViewModel.ModuleIdsContributingToOutput();

		var parameterCount = 0;

		foreach ( var pinnedGroup in graphViewModel.PinnedGroups )
		{
			if ( !contributingModuleIds.Contains( pinnedGroup.Module.ModuleId ) )
			{
				continue;
			}

			foreach ( var pinnedEntry in pinnedGroup.Settings )
			{
				if ( parameterCount >= MaxGraphParameters )
				{
					break;
				}

				var (parameterName, parameterRawValue, parameterFormattedValue) = DescribePinnedEntry( pinnedEntry );

				dataBuffer.SetGraphParameterNodeName( parameterCount, pinnedGroup.Module.NodeName );
				dataBuffer.SetGraphParameterNodeModuleType( parameterCount, pinnedGroup.Module.ModuleTypeName );
				dataBuffer.SetGraphParameterName( parameterCount, parameterName );
				dataBuffer.SetGraphParameterRawValue( parameterCount, parameterRawValue );
				dataBuffer.SetGraphParameterFormattedValue( parameterCount, parameterFormattedValue );

				parameterCount++;
			}
		}

		dataBuffer.graphParameterCount = parameterCount;
	}

	private static (string Name, string RawValue, string FormattedValue) DescribePinnedEntry( object pinnedEntry )
	{
		switch ( pinnedEntry )
		{
			case FFBPinnedEnabledViewModel enabledEntry:

				return (enabledEntry.Label, FormatBooleanValue( enabledEntry.Module.Enabled ), FormatBooleanText( enabledEntry.Module.Enabled ));

			case FFBModuleSettingViewModel setting:

				switch ( setting.SettingType )
				{
					case FFBSettingType.Switch:

						return (setting.Label, FormatBooleanValue( setting.IsOn ), FormatBooleanText( setting.IsOn ));

					case FFBSettingType.Choice:

						var choiceText = setting.ChoiceOptions.FirstOrDefault( option => option.Key == setting.ChoiceIndex ).Value ?? setting.ValueString;

						return (setting.Label, setting.ChoiceIndex.ToString( CultureInfo.InvariantCulture ), choiceText);

					default:

						return (setting.Label, setting.Value.ToString( CultureInfo.InvariantCulture ), setting.ValueString);
				}

			default:

				return (string.Empty, string.Empty, string.Empty);
		}
	}

	private static string FormatBooleanValue( bool value ) => value ? GraphParameterTrueValue : GraphParameterFalseValue;

	private static string FormatBooleanText( bool value ) => value ? FFBDisplayNames.Localize( "ON", "ON" ) : FFBDisplayNames.Localize( "OFF", "OFF" );
}
