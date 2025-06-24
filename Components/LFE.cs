
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SharpDX.DirectSound;
using SharpDX.Multimedia;

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

	//	private readonly Lock _lock = new();

	public Guid? NextRecordingDeviceGuid { private get; set; } = null;

	public float CurrentMagnitude
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		get
		{
			// using ( _lock.EnterScope() )
			{
				var magnitude = _magnitude[ _pingPongIndex, _batchIndex ];

				_batchIndex = Math.Min( _batchIndex + 1, _batchCount - 1 );

				return magnitude;
			}
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

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LFE] Initialize >>>" );

		EnumerateDevices();

		_workerThread.Start();

		app.Logger.WriteLine( "[LFE] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LFE] Shutdown >>>" );

		_running = false;

		_autoResetEvent.Set();

		app.Logger.WriteLine( "[LFE] <<< Shutdown" );
	}

	public void SetMairaComboBoxItemsSource( MairaComboBox mairaComboBox )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LFE] SetMairaComboBoxItemsSource >>>" );

		var dictionary = new Dictionary<Guid, string>();

		_captureDeviceList.ToList().ForEach( keyValuePair => dictionary[ keyValuePair.Key ] = keyValuePair.Value );

		mairaComboBox.ItemsSource = dictionary.OrderBy( keyValuePair => keyValuePair.Value );
		mairaComboBox.SelectedValue = DataContext.DataContext.Instance.Settings.RacingWheelLFERecordingDeviceGuid;

		app.Logger.WriteLine( "[LFE] <<< SetMairaComboBoxItemsSource" );
	}

	private void EnumerateDevices()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LFE] EnumerateDevices >>>" );

		_captureDeviceList.Clear();

		_captureDeviceList.Add( Guid.Empty, DataContext.DataContext.Instance.Localization[ "Disabled" ] );

		var deviceInformationList = DirectSoundCapture.GetDevices();

		foreach ( var deviceInformation in deviceInformationList )
		{
			if ( deviceInformation.DriverGuid != Guid.Empty )
			{
				app.Logger.WriteLine( $"[LFE] Description: {deviceInformation.Description}" );
				app.Logger.WriteLine( $"[LFE] Module name: {deviceInformation.ModuleName}" );
				app.Logger.WriteLine( $"[LFE] Driver GUID: {deviceInformation.DriverGuid}" );

				_captureDeviceList.Add( deviceInformation.DriverGuid, deviceInformation.Description );

				app.Logger.WriteLine( $"[LFE] ---" );
			}
		}

		app.Logger.WriteLine( "[LFE] <<< EnumerateDevices" );
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

				Array.Clear( _magnitude );
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

				_batchIndex = 0;
				_pingPongIndex = 0;

				_captureBuffer.SetNotificationPositions( notificationPositionArray );
				_captureBuffer.Start( true );

				signalReceived = false;
			}

			NextRecordingDeviceGuid = null;
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
			}

			// using ( _lock.EnterScope() )
			{
				_batchIndex = 0;
				_pingPongIndex = pingPongIndex;
			}

			_captureBuffer.Unlock( dataStream, secondPart );
		}
	}

	private static void WorkerThread()
	{
		var _byteSpan = new Span<byte>( new byte[ _frameSizeInBytes ] );

		var app = App.Instance!;

		var directSound = app.LFE;

		while ( directSound._running )
		{
			var signalReceived = directSound._autoResetEvent.WaitOne( 250 );

			directSound.Update( app, signalReceived, _byteSpan );
		}
	}
}
