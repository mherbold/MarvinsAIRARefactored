
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.Windows;
using SharpDX.DirectSound;
using SharpDX.Multimedia;
using System.Runtime.CompilerServices;

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

	public Guid? NextCaptureDeviceGuid { private get; set; } = null;

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

	public Dictionary<Guid, string> CaptureDeviceList { get; private set; } = [];

	private DirectSoundCapture? _directSoundCapture = null;
	private CaptureBuffer? _captureBuffer = null;
	private readonly AutoResetEvent _autoResetEvent = new( false );

	private readonly Thread _workerThread = new( WorkerThread ) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "MAIRA LFE Worker Thread" };

	private bool _running = true;

	private int _lfeBusy = 0;
	private int _pingPongIndex = 0;
	private int _batchIndex = 0;

	private readonly byte[] _scratchRead = new byte[ _frameSizeInBytes ];
	private readonly float[,] _magnitude = new float[ 2, _batchCount ];

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LFE] Initialize >>>" );

		EnumerateCaptureDevices();

		_workerThread.Start();

		app.Logger.WriteLine( "[LFE] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LFE] Shutdown >>>" );

		_running = false;

		_autoResetEvent.Set();

		_workerThread.Join( 5000 );

		ReleaseCaptureDevice();

		app.Logger.WriteLine( "[LFE] <<< Shutdown" );
	}

	private void EnumerateCaptureDevices()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LFE] EnumerateCaptureDevices >>>" );

		CaptureDeviceList.Clear();

		CaptureDeviceList.Add( Guid.Empty, DataContext.DataContext.Instance.Localization[ "Disabled" ] );

		var deviceInformationList = DirectSoundCapture.GetDevices();

		foreach ( var deviceInformation in deviceInformationList )
		{
			if ( deviceInformation.DriverGuid != Guid.Empty )
			{
				app.Logger.WriteLine( $"[LFE] Description: {deviceInformation.Description}" );
				app.Logger.WriteLine( $"[LFE] Module name: {deviceInformation.ModuleName}" );
				app.Logger.WriteLine( $"[LFE] Driver GUID: {deviceInformation.DriverGuid}" );

				CaptureDeviceList.Add( deviceInformation.DriverGuid, deviceInformation.Description );

				app.Logger.WriteLine( $"[LFE] ---" );
			}
		}

		MainWindow._racingWheelPage.UpdateLFERecordingDeviceOptions();

		app.Logger.WriteLine( "[LFE] <<< EnumerateCaptureDevices" );
	}

	private void CreateCaptureDevice()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LFE] CreateCaptureDevice >>>" );

		if ( ( NextCaptureDeviceGuid != null ) && ( NextCaptureDeviceGuid != Guid.Empty ) )
		{
			try
			{
				app.Logger.WriteLine( "[LFE] Creating the new direct sound capture device" );

				_directSoundCapture = new DirectSoundCapture( (Guid) NextCaptureDeviceGuid );

				var captureBufferDescription = new CaptureBufferDescription
				{
					Format = new WaveFormat( _captureBufferFrequency, _captureBufferBitsPerSample, 1 ),
					BufferBytes = _captureBufferSizeInBytes
				};

				_captureBuffer = new CaptureBuffer( _directSoundCapture, captureBufferDescription );

				app.Logger.WriteLine( "[SpeechToText] Setting up the notification positions" );

				var notifyCount = _captureBufferNumSamples / _frameSizeInSamples;

				var notificationPositionArray = new NotificationPosition[ notifyCount ];

				for ( var i = 0; i < notificationPositionArray.Length; i++ )
				{
					var endOfBlock = ( i + 1 ) * _captureBufferSizeInBytes / notifyCount;

					notificationPositionArray[ i ] = new()
					{
						Offset = endOfBlock - 1,
						WaitHandle = _autoResetEvent
					};
				}

				_captureBuffer.SetNotificationPositions( notificationPositionArray );

				app.Logger.WriteLine( "[LFE] Starting the capture" );

				_batchIndex = 0;
				_pingPongIndex = 0;

				_captureBuffer.Start( true );
			}
			catch ( Exception exception )
			{
				app.Logger.WriteLine( "[LFE] Failed to create direct sound capture device - could microphone access be restricted? " + exception.Message.Trim() );
			}
		}

		app.Logger.WriteLine( "[LFE] <<< CreateCaptureDevice" );
	}

	private void ReleaseCaptureDevice()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LFE] ReleaseCaptureDevice >>>" );

		if ( _captureBuffer != null )
		{
			_captureBuffer.Stop();
			_captureBuffer.Dispose();

			_captureBuffer = null;
		}

		Array.Clear( _magnitude );

		app.Logger.WriteLine( "[LFE] <<< ReleaseCaptureDevice" );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private void Update( App app, bool signalReceived )
	{
		if ( NextCaptureDeviceGuid != null )
		{
			app.Logger.WriteLine( $"[LFE] Switching to the next capture device: {NextCaptureDeviceGuid}" );

			ReleaseCaptureDevice();
			CreateCaptureDevice();

			NextCaptureDeviceGuid = null;

			signalReceived = false;
		}

		if ( signalReceived && ( _captureBuffer != null ) )
		{
			if ( Interlocked.Exchange( ref _lfeBusy, 1 ) == 0 )
			{
				// copy audio from the capture buffer into our scratch read buffer

				var currentCapturePosition = _captureBuffer.CurrentCapturePosition;

				currentCapturePosition = ( currentCapturePosition / _frameSizeInBytes ) * _frameSizeInBytes;

				var currentReadPosition = ( currentCapturePosition + _captureBufferSizeInBytes - _frameSizeInBytes ) % _captureBufferSizeInBytes;

				_captureBuffer.Read( _scratchRead, 0, _frameSizeInBytes, currentReadPosition, LockFlags.None );

				// convert from PCM16 to float32 [-1,1]

				var floatSamples = new float[ _frameSizeInSamples ];

				for ( var i = 0; i < _frameSizeInSamples; i++ )
				{
					var b0 = _scratchRead[ 2 * i + 0 ];
					var b1 = _scratchRead[ 2 * i + 1 ];

					var s = (short) ( b0 | ( b1 << 8 ) );

					floatSamples[ i ] = s / 32768f;
				}

				var pingPongIndex = ( _pingPongIndex + 1 ) & 1;
				var sampleOffset = 0;

				for ( var batchIndex = 0; batchIndex < _batchCount; batchIndex++ )
				{
					var amplitudeSum = 0f;

					for ( var sampleIndex = 0; sampleIndex < _500HzTo8KhzScale; sampleIndex++ )
					{
						amplitudeSum += floatSamples[ sampleOffset ];

						sampleOffset++;
					}

					_magnitude[ pingPongIndex, batchIndex ] = amplitudeSum / _500HzTo8KhzScale;
				}

				_batchIndex = 0;
				_pingPongIndex = pingPongIndex;
				_lfeBusy = 0;
			}
		}
	}

	private static void WorkerThread()
	{
		var app = App.Instance!;

		var lfe = app.LFE;

		while ( lfe._running )
		{
			var signalReceived = lfe._autoResetEvent.WaitOne( 250 );

			lfe.Update( app, signalReceived );
		}
	}
}
