
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace MarvinsAIRARefactored.Components;

public class Telemetry
{
	[StructLayout( LayoutKind.Sequential, Pack = 4 )]
	public struct DataStruct
	{
		public DataStruct()
		{
			algorithmName = string.Empty;
			for ( var i = 0; i < algorithmParameterName.Length; i++) { algorithmParameterName[i] = string.Empty; }
			for (var i = 0; i < algorithmParameterValue.Length; i++) { algorithmParameterValue[i] = string.Empty; }
		}

		public float racingWheelOutputTorque;
		public float autoRacingWheelMaxForce;
		public bool racingWheelOutputTorqueIsClipping;
		public bool racingWheelCrashProtectionIsActive;
		public bool racingWheelCurbProtectionIsActive;
		public bool racingWheelIsFading;

		public float steeringEffectsUndersteerEffect;
		public float steeringEffectsOversteerEffect;
		public float steeringEffectsSkidSlip;

		public float pedalsClutchFrequency;
		public float pedalsClutchAmplitude;

		public float pedalsBrakeFrequency;
		public float pedalsBrakeAmplitude;

		public float pedalsThrottleFrequency;
		public float pedalsThrottleAmplitude;

		public string algorithmName;
		public string[] algorithmParameterName = new string[5];
		public string[] algorithmParameterValue = new string[5];
	}

	private struct ShareDataStruct
	{
		public void CopyData( DataStruct sourceData )
		{
			autoRacingWheelMaxForce = sourceData.autoRacingWheelMaxForce;
			racingWheelOutputTorque = sourceData.racingWheelOutputTorque;
			racingWheelOutputTorqueIsClipping = sourceData.racingWheelOutputTorqueIsClipping;
			racingWheelCrashProtectionIsActive = sourceData.racingWheelCrashProtectionIsActive;
			racingWheelCurbProtectionIsActive = sourceData.racingWheelCurbProtectionIsActive;
			racingWheelIsFading = sourceData.racingWheelIsFading;
			steeringEffectsUndersteerEffect = sourceData.steeringEffectsUndersteerEffect;
			steeringEffectsOversteerEffect = sourceData.steeringEffectsOversteerEffect;
			steeringEffectsSkidSlip = sourceData.steeringEffectsSkidSlip;
			pedalsClutchFrequency = sourceData.pedalsClutchFrequency;
			pedalsClutchAmplitude = sourceData.pedalsClutchAmplitude;
			pedalsBrakeFrequency = sourceData.pedalsBrakeFrequency;
			pedalsBrakeAmplitude = sourceData.pedalsBrakeAmplitude;
			pedalsThrottleFrequency = sourceData.pedalsThrottleFrequency;
			pedalsThrottleAmplitude = sourceData.pedalsThrottleAmplitude;
			algorithmNameLength = ( sourceData.algorithmName.Length) * sizeof( char );
			algorithmParameterNameLength_0 = ( sourceData.algorithmParameterName[0].Length ) * sizeof( char );
			algorithmParameterValueLength_0 = ( sourceData.algorithmParameterValue[0].Length ) * sizeof( char );
			algorithmParameterNameLength_1 = ( sourceData.algorithmParameterName[1].Length ) * sizeof( char );
			algorithmParameterValueLength_1 = ( sourceData.algorithmParameterValue[1].Length ) * sizeof( char );
			algorithmParameterNameLength_2 = ( sourceData.algorithmParameterName[2].Length ) * sizeof( char );
			algorithmParameterValueLength_2 = ( sourceData.algorithmParameterValue[2].Length ) * sizeof( char );
			algorithmParameterNameLength_3 = ( sourceData.algorithmParameterName[3].Length ) * sizeof( char );
			algorithmParameterValueLength_3 = ( sourceData.algorithmParameterValue[3].Length ) * sizeof( char );
			algorithmParameterNameLength_4 = ( sourceData.algorithmParameterName[4].Length ) * sizeof( char );
			algorithmParameterValueLength_4 = ( sourceData.algorithmParameterValue[4].Length ) * sizeof( char );
		}

		public int version;
		public int tickCount;

		public bool iracingConnected;

		public float racingWheelStrength;
		public float racingWheelMaxForce;

		public float racingWheelOutputTorque;
		public float autoRacingWheelMaxForce;
		public bool racingWheelOutputTorqueIsClipping;
		public bool racingWheelCrashProtectionIsActive;
		public bool racingWheelCurbProtectionIsActive;
		public bool racingWheelIsFading;

		public float steeringEffectsUndersteerEffect;
		public float steeringEffectsOversteerEffect;
		public float steeringEffectsSkidSlip;

		public float pedalsClutchFrequency;
		public float pedalsClutchAmplitude;

		public float pedalsBrakeFrequency;
		public float pedalsBrakeAmplitude;

		public float pedalsThrottleFrequency;
		public float pedalsThrottleAmplitude;

		public int algorithmNameLength;
		public int algorithmParameterNameLength_0;
		public int algorithmParameterValueLength_0;
		public int algorithmParameterNameLength_1;
		public int algorithmParameterValueLength_1;
		public int algorithmParameterNameLength_2;
		public int algorithmParameterValueLength_2;
		public int algorithmParameterNameLength_3;
		public int algorithmParameterValueLength_3;
		public int algorithmParameterNameLength_4;
		public int algorithmParameterValueLength_4;
	}

	public DataStruct Data = new();
	private ShareDataStruct _shareData = new();

	private const string MemoryMappedFileName = "Local\\MAIRARefactoredTelemetry";

	private MemoryMappedFile? _memoryMappedFile = null;
	private MemoryMappedViewAccessor? _memoryMappedFileViewAccessor = null;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Telemetry] Initialize >>>" );

		var sizeOfTelemetryData = Marshal.SizeOf( typeof( DataStruct ) ) + sizeof( char ) * 1000;

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
		var settings = DataContext.DataContext.Instance.Settings;

		_shareData.version = 1;
		_shareData.tickCount++;

		_shareData.iracingConnected = app.Simulator.IsConnected;
		_shareData.racingWheelStrength = settings.RacingWheelStrength;
		_shareData.racingWheelMaxForce = settings.RacingWheelMaxForce;

		_shareData.CopyData( Data );

		_memoryMappedFileViewAccessor?.Write( 0, ref _shareData );

		byte[] stringBytes;
		long filePositionOffset = Marshal.SizeOf( typeof( ShareDataStruct ) );

		stringBytes = Encoding.Unicode.GetBytes( Data.algorithmName );
		_memoryMappedFileViewAccessor?.WriteArray( filePositionOffset, stringBytes, 0, stringBytes.Length );
		filePositionOffset += stringBytes.Length;

		for ( var i = 0; i < 5; i++ )
		{
			stringBytes = Encoding.Unicode.GetBytes( Data.algorithmParameterName[i] );
			_memoryMappedFileViewAccessor?.WriteArray( filePositionOffset, stringBytes, 0, stringBytes.Length );
			filePositionOffset += stringBytes.Length;

			stringBytes = Encoding.Unicode.GetBytes( Data.algorithmParameterValue[i] );
			_memoryMappedFileViewAccessor?.WriteArray( filePositionOffset, stringBytes, 0, stringBytes.Length );
			filePositionOffset += stringBytes.Length;
		}
	}
}
