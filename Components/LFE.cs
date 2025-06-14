
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Image = System.Windows.Controls.Image;

using SharpDX.DirectSound;
using SharpDX.Multimedia;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;

namespace MarvinsAIRARefactored.Components;

public class LFE
{
	private const int _bytesPerSample = 2;
	private const int _500HzTo8KhzScale = 16;
	private const int _batchCount = 10;

	private const int _captureBufferFrequency = 8000;
	private const int _captureBufferBitsPerSample = _bytesPerSample * 8;
	private const int _captureBufferNumSamples = _captureBufferFrequency;
	private const int _captureBufferSizeInBytes = _captureBufferNumSamples * _bytesPerSample;

	private const int _frameSizeInSamples = _500HzTo8KhzScale * _batchCount;
	private const int _frameSizeInBytes = _frameSizeInSamples * _bytesPerSample;

	public Guid? NextRecordingDeviceGuid { private get; set; } = null;

	public float CurrentMagnitude
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		get
		{
			var magnitude = _magnitude[ _pingPongIndex, _batchIndex ];

			_batchIndex = Math.Min( _batchIndex + 1, _batchCount - 1 );

			return magnitude;
		}
	}

	private readonly Dictionary<Guid, string> _captureDeviceList = [];

	private DirectSoundCapture? _directSoundCapture = null;
	private CaptureBuffer? _captureBuffer = null;
	private readonly AutoResetEvent _autoResetEvent = new( false );

	private readonly Thread _workerThread = new( WorkerThread ) { IsBackground = true, Priority = ThreadPriority.Highest };

	private bool _running = true;
	private int _pingPongIndex = 0;
	private int _batchIndex = 0;
	private readonly float[,] _magnitude = new float[ 2, _batchCount ];

	private readonly Graph _lfeGraph;
	private readonly Statistics _lfeStatistics = new( 500 );

	public LFE( Image lfeGraphImage )
	{
		var app = App.Instance;

		app?.Logger.WriteLine( $"[DirectSound] Constructor >>>" );

		_lfeGraph = new Graph( lfeGraphImage );

		app?.Logger.WriteLine( $"[DirectSound] <<< Constructor" );
	}

	public void Initialize()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[DirectSound] Initialize >>>" );

			EnumerateDevices();

			_workerThread.Start();

			app.Logger.WriteLine( "[DirectSound] <<< Initialize" );
		}
	}

	public void Shutdown()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[DirectSound] Shutdown >>>" );

			_captureDeviceList.Clear();

			_running = false;

			_autoResetEvent.Set();

			app.Logger.WriteLine( "[DirectSound] <<< Shutdown" );
		}
	}

	public void SetMairaComboBoxItemsSource( MairaComboBox mairaComboBox )
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[DirectSound] SetMairaComboBoxItemsSource >>>" );

			var dictionary = new Dictionary<Guid, string>();

			if ( _captureDeviceList.Count == 0 )
			{
				dictionary.Add( Guid.Empty, DataContext.DataContext.Instance.Localization[ "NoLFEDevicesFound" ] );
			}

			_captureDeviceList.ToList().ForEach( keyValuePair => dictionary[ keyValuePair.Key ] = keyValuePair.Value );

			mairaComboBox.ItemsSource = dictionary.OrderBy( keyValuePair => keyValuePair.Value );
			mairaComboBox.SelectedValue = DataContext.DataContext.Instance.Settings.RacingWheelLFERecordingDeviceGuid;

			app.Logger.WriteLine( "[DirectSound] <<< SetMairaComboBoxItemsSource" );
		}
	}

	private void EnumerateDevices()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[DirectSound] EnumerateDevices >>>" );

			var deviceInformationList = DirectSoundCapture.GetDevices();

			foreach ( var deviceInformation in deviceInformationList )
			{
				if ( deviceInformation.DriverGuid != Guid.Empty )
				{
					app.Logger.WriteLine( $"[DirectSound] Description: {deviceInformation.Description}" );
					app.Logger.WriteLine( $"[DirectSound] Module name: {deviceInformation.ModuleName}" );
					app.Logger.WriteLine( $"[DirectSound] Driver GUID: {deviceInformation.DriverGuid}" );

					_captureDeviceList.Add( deviceInformation.DriverGuid, deviceInformation.Description );

					app.Logger.WriteLine( $"[DirectSound] ---" );
				}
			}

			app.Logger.WriteLine( "[DirectSound] <<< EnumerateDevices" );
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private void Update( App app, bool signalReceived, Span<byte> byteSpan )
	{
		if ( NextRecordingDeviceGuid != null )
		{
			if ( _captureBuffer != null )
			{
				_captureBuffer.Stop();
				_captureBuffer.Dispose();

				_captureBuffer = null;

				for ( var j = 0; j < _batchCount; j++ )
				{
					_magnitude[ 0, j ] = 0f;
					_magnitude[ 1, j ] = 0f;
				}
			}

			if ( NextRecordingDeviceGuid != Guid.Empty )
			{
				_directSoundCapture = new DirectSoundCapture( (Guid) NextRecordingDeviceGuid );

				var captureBufferDescription = new CaptureBufferDescription
				{
					Format = new WaveFormat( _captureBufferFrequency, _captureBufferBitsPerSample, 1 ),
					BufferBytes = _captureBufferSizeInBytes
				};

				_captureBuffer = new CaptureBuffer( _directSoundCapture, captureBufferDescription );

				var notificationPositionArray = new NotificationPosition[ _captureBufferNumSamples / _frameSizeInSamples ];

				for ( var i = 0; i < notificationPositionArray.Length; i++ )
				{
					notificationPositionArray[ i ] = new()
					{
						Offset = i * _frameSizeInBytes,
						WaitHandle = _autoResetEvent
					};
				}

				_pingPongIndex = 0;
				_batchIndex = 0;

				_captureBuffer.SetNotificationPositions( notificationPositionArray );
				_captureBuffer.Start( true );

				NextRecordingDeviceGuid = null;

				signalReceived = false;
			}
		}

		if ( signalReceived && ( _captureBuffer != null ) )
		{
			var currentCapturePosition = _captureBuffer.CurrentCapturePosition;

			currentCapturePosition = ( currentCapturePosition / _frameSizeInBytes ) * _frameSizeInBytes;

			var currentReadPosition = ( currentCapturePosition + _captureBufferSizeInBytes - _frameSizeInBytes ) % _captureBufferSizeInBytes;

			var dataStream = _captureBuffer.Lock( currentReadPosition, _frameSizeInBytes, LockFlags.None, out var secondPart );

			dataStream.ReadExactly( byteSpan );

			var shortSpan = MemoryMarshal.Cast<byte, short>( byteSpan );
			var pingPongIndex = ( _pingPongIndex + 1 ) & 1;
			var sampleOffset = 0;

			for ( var batchIndex = 0; batchIndex < _batchCount; batchIndex++ )
			{
				var amplitudeSum = 0f;

				for ( var sampleIndex = 0; sampleIndex < _500HzTo8KhzScale; sampleIndex++ )
				{
					amplitudeSum += shortSpan[ sampleOffset ] / (float) short.MinValue;

					sampleOffset++;
				}

				_magnitude[ pingPongIndex, batchIndex ] = amplitudeSum / _500HzTo8KhzScale;

				if ( app.MainWindow.GraphsTabItemIsVisible )
				{
					_lfeStatistics.Update( _magnitude[ pingPongIndex, batchIndex ] );

					var y = Math.Clamp( _magnitude[ pingPongIndex, batchIndex ] / 2f, -1f, 1f );

					_lfeGraph.DrawGradientLine( y, 240, 96, 255 );
					_lfeGraph.Advance();
				}
			}

			_pingPongIndex = pingPongIndex;

			_captureBuffer.Unlock( dataStream, secondPart );
		}
	}

	private static void WorkerThread()
	{
		var _byteSpan = new Span<byte>( new byte[ _frameSizeInBytes ] );

		var app = App.Instance;

		if ( app != null )
		{
			var directSound = app.LFE;

			while ( directSound._running )
			{
				var signalReceived = directSound._autoResetEvent.WaitOne( 250 );

				directSound.Update( app, signalReceived, _byteSpan );
			}
		}
	}

	public void Tick( App app )
	{
		if ( app.MainWindow.GraphsTabItemIsVisible )
		{
			_lfeGraph.UpdateImage();

			app.MainWindow.Graphs_LFE_MinMaxAvg.Content = $"{_lfeStatistics.MinimumValue,5:F2} {_lfeStatistics.MaximumValue,5:F2} {_lfeStatistics.AverageValue,5:F2}";
			app.MainWindow.Graphs_LFE_VarStdDev.Content = $"{_lfeStatistics.Variance,5:F2} {_lfeStatistics.StandardDeviation,5:F2}";
		}
	}
}
