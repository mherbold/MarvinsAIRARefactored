
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace MarvinsAIRARefactored.Components;

public class Debug
{
	public string Label_1 { private get; set; } = string.Empty;
	public string Label_2 { private get; set; } = string.Empty;
	public string Label_3 { private get; set; } = string.Empty;
	public string Label_4 { private get; set; } = string.Empty;
	public string Label_5 { private get; set; } = string.Empty;
	public string Label_6 { private get; set; } = string.Empty;
	public string Label_7 { private get; set; } = string.Empty;
	public string Label_8 { private get; set; } = string.Empty;
	public string Label_9 { private get; set; } = string.Empty;
	public string Label_10 { private get; set; } = string.Empty;

	public class FFBSample
	{
		public float deltaMilliseconds;
		public float steeringWheelTorque60Hz;
		public float steeringWheelTorque500Hz;
		public float inputLFEMagnitude;
		public float outputTorque;
		public float simulatorSteeringWheelAngle;
		public float directInputWheelPosition;
		public float directInputWheelVelocity;
		public float velocityX;
		public float velocityY;
		public float gear;
		public float clutch;
		public float brake;
		public float throttle;
		public float lapDistPct;
	}

	private readonly FFBSample[] _ffbSamples = new FFBSample[ 300000 ];

	private int _ffbSampleIndex;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Tick( App app )
	{
		if ( app.MainWindow.DebugTabItemIsVisible )
		{
			app.MainWindow.Debug_Label_1.Content = Label_1;
			app.MainWindow.Debug_Label_2.Content = Label_2;
			app.MainWindow.Debug_Label_3.Content = Label_3;
			app.MainWindow.Debug_Label_4.Content = Label_4;
			app.MainWindow.Debug_Label_5.Content = Label_5;
			app.MainWindow.Debug_Label_6.Content = Label_6;
			app.MainWindow.Debug_Label_7.Content = Label_7;
			app.MainWindow.Debug_Label_8.Content = Label_8;
			app.MainWindow.Debug_Label_9.Content = Label_9;
			app.MainWindow.Debug_Label_10.Content = Label_10;
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void AddFFBSample( float deltaMilliseconds, float steeringWheelTorque60Hz, float steeringWheelTorque500Hz, float inputLFEMagnitude, float outputTorque )
	{
		var app = App.Instance!;

		if ( _ffbSampleIndex < _ffbSamples.Length )
		{
			_ffbSamples[ _ffbSampleIndex++ ] = new FFBSample()
			{
				deltaMilliseconds = deltaMilliseconds,
				steeringWheelTorque60Hz = steeringWheelTorque60Hz,
				steeringWheelTorque500Hz = steeringWheelTorque500Hz,
				inputLFEMagnitude = inputLFEMagnitude,
				outputTorque = outputTorque,
				simulatorSteeringWheelAngle = app.Simulator.SteeringWheelAngle,
				directInputWheelPosition = app.DirectInput.ForceFeedbackWheelPosition,
				directInputWheelVelocity = app.DirectInput.ForceFeedbackWheelVelocity,
				velocityX = app.Simulator.VelocityX,
				velocityY = app.Simulator.VelocityY,
				gear = app.Simulator.Gear,
				clutch = app.Simulator.Clutch,
				brake = app.Simulator.Brake,
				throttle = app.Simulator.Throttle,
				lapDistPct = app.Simulator.LapDistPct
			};
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void ResetFFBSamples()
	{
		_ffbSampleIndex = 0;
	}

	public void DumpFFBSamplesToCSVFile()
	{
		var filePath = Path.Combine( App.DocumentsFolder, "DataDump.csv" );

		using var writer = new StreamWriter( filePath, false, Encoding.UTF8 );

		writer.WriteLine( "DeltaMilliseconds,60HzInput,500HzInput,500HzLFEInput,500HzOutput,SimWheelAngle,DIWheelPosition,DIWheelVelocity,CarVelocityX,CarVelocityY,Gear,Clutch,Brake,Throttle,LapDistPct" );

		for ( var i = 0; i < _ffbSampleIndex; i++ )
		{
			writer.WriteLine( $"{_ffbSamples[ i ].deltaMilliseconds:F6},{_ffbSamples[ i ].steeringWheelTorque60Hz:F4},{_ffbSamples[ i ].steeringWheelTorque500Hz:F4},{_ffbSamples[ i ].inputLFEMagnitude:F4},{_ffbSamples[ i ].outputTorque:F4},{_ffbSamples[ i ].simulatorSteeringWheelAngle:F4},{_ffbSamples[ i ].directInputWheelPosition:F4},{_ffbSamples[ i ].directInputWheelVelocity:F8},{_ffbSamples[ i ].velocityX:F4},{_ffbSamples[ i ].velocityY:F4},{_ffbSamples[ i ].gear},{_ffbSamples[ i ].clutch:F4},{_ffbSamples[ i ].brake:F4},{_ffbSamples[ i ].throttle:F4},{_ffbSamples[ i ].lapDistPct:F6}" );
		}
	}
}
