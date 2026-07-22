
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public class LFE
{
	public const string DisabledDeviceName = "";

	private const int _bytesPerSample = 2;
	private const int _500HzTo8KhzScale = 16;
	private const int _batchCount = 10;

	private const int _captureBufferFrequency = 8000;
	private const int _captureBufferNumSamples = _captureBufferFrequency;
	private const int _captureBufferSizeInBytes = _captureBufferNumSamples * _bytesPerSample;

	private const int _frameSizeInSamples = _500HzTo8KhzScale * _batchCount;
	private const int _frameSizeInBytes = _frameSizeInSamples * _bytesPerSample;

	public string? NextCaptureDeviceName { private get; set; } = null;

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

	public List<string> CaptureDeviceNames { get; private set; } = [];

	private FMOD.System? _captureSystem = null;
	private FMOD.Sound _captureSound;
	private int _captureDriverId = -1;
	private bool _captureDeviceCreated = false;
	private readonly AutoResetEvent _autoResetEvent = new( false );

	private readonly Thread _workerThread = new( WorkerThread ) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "MAIRA LFE Worker Thread" };

	private volatile bool _running = true;

	private int _lfeBusy = 0;
	private int _pingPongIndex = 0;
	private int _batchIndex = 0;
	private int _lastProcessedWritePosSamples = -1;

	private readonly byte[] _scratchRead = new byte[ _frameSizeInBytes ];
	private readonly float[,] _magnitude = new float[ 2, _batchCount ];

	private string? _configuredCaptureDeviceName = null;

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

		if ( _workerThread.IsAlive )
		{
			_autoResetEvent.Set();

			_workerThread.Join( 5000 );
		}

		ReleaseCaptureDevice();

		app.Logger.WriteLine( "[LFE] <<< Shutdown" );
	}

	private void EnumerateCaptureDevices()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LFE] EnumerateCaptureDevices >>>" );

		CaptureDeviceNames.Clear();

		var createResult = FMOD.Factory.System_Create( out var tempSystem );

		if ( createResult == FMOD.RESULT.OK )
		{
			try
			{
				var initResult = tempSystem.init( 8, FMOD.INITFLAGS.NORMAL, IntPtr.Zero );

				if ( initResult == FMOD.RESULT.OK )
				{
					tempSystem.getRecordNumDrivers( out var numDrivers, out _ );

					app.Logger.WriteLine( $"[LFE] Found {numDrivers} recording drivers" );

					for ( var i = 0; i < numDrivers; i++ )
					{
						tempSystem.getRecordDriverInfo( i, out var name, 256, out _, out _, out _, out _, out _ );

						app.Logger.WriteLine( $"[LFE] Driver {i}: '{name}'" );

						if ( !string.IsNullOrWhiteSpace( name ) )
						{
							CaptureDeviceNames.Add( name );
						}
					}
				}
				else
				{
					app.Logger.WriteLine( $"[LFE] EnumerateCaptureDevices: FMOD init failed: {initResult}" );
				}
			}
			finally
			{
				tempSystem.close();
				tempSystem.release();
			}
		}
		else
		{
			app.Logger.WriteLine( $"[LFE] EnumerateCaptureDevices: FMOD System_Create failed: {createResult}" );
		}

		MainWindow._racingWheelPage.UpdateLFERecordingDeviceOptions();

		app.Logger.WriteLine( "[LFE] <<< EnumerateCaptureDevices" );
	}

	private void CreateCaptureDevice()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LFE] CreateCaptureDevice >>>" );

		if ( !string.IsNullOrEmpty( _configuredCaptureDeviceName ) )
		{
			try
			{
				var createResult = FMOD.Factory.System_Create( out var system );

				if ( createResult != FMOD.RESULT.OK )
				{
					app.Logger.WriteLine( $"[LFE] FMOD System_Create failed: {createResult}" );

					app.Logger.WriteLine( "[LFE] <<< CreateCaptureDevice" );

					return;
				}

				var initResult = system.init( 32, FMOD.INITFLAGS.NORMAL, IntPtr.Zero );

				if ( initResult != FMOD.RESULT.OK )
				{
					app.Logger.WriteLine( $"[LFE] FMOD system init failed: {initResult}" );

					system.release();

					app.Logger.WriteLine( "[LFE] <<< CreateCaptureDevice" );

					return;
				}

				system.getRecordNumDrivers( out var numDrivers, out _ );

				var driverId = -1;

				for ( var i = 0; i < numDrivers; i++ )
				{
					system.getRecordDriverInfo( i, out var name, 256, out _, out _, out _, out _, out _ );

					if ( string.Equals( name, _configuredCaptureDeviceName, StringComparison.OrdinalIgnoreCase ) )
					{
						driverId = i;
						break;
					}
				}

				if ( driverId < 0 )
				{
					app.Logger.WriteLine( $"[LFE] Device '{_configuredCaptureDeviceName}' not found; falling back to driver 0" );

					driverId = 0;
				}

				app.Logger.WriteLine( $"[LFE] Creating capture for driver {driverId} ('{_configuredCaptureDeviceName}')" );

				var exInfo = new FMOD.CREATESOUNDEXINFO
				{
					cbsize = Marshal.SizeOf<FMOD.CREATESOUNDEXINFO>(),
					numchannels = 1,
					defaultfrequency = _captureBufferFrequency,
					format = FMOD.SOUND_FORMAT.PCM16,
					length = (uint) _captureBufferSizeInBytes
				};

				var soundResult = system.createSound( string.Empty, FMOD.MODE.OPENUSER | FMOD.MODE.LOOP_NORMAL, ref exInfo, out var sound );

				if ( soundResult != FMOD.RESULT.OK )
				{
					app.Logger.WriteLine( $"[LFE] FMOD createSound failed: {soundResult}" );

					system.close();
					system.release();

					app.Logger.WriteLine( "[LFE] <<< CreateCaptureDevice" );

					return;
				}

				var recordResult = system.recordStart( driverId, sound, true );

				if ( recordResult != FMOD.RESULT.OK )
				{
					app.Logger.WriteLine( $"[LFE] FMOD recordStart failed: {recordResult}" );

					sound.release();
					system.close();
					system.release();

					app.Logger.WriteLine( "[LFE] <<< CreateCaptureDevice" );

					return;
				}

				_captureSystem = system;
				_captureSound = sound;
				_captureDriverId = driverId;

				_batchIndex = 0;
				_pingPongIndex = 0;
				_lastProcessedWritePosSamples = -1;

				_captureDeviceCreated = true;

				app.Logger.WriteLine( "[LFE] Capture device created successfully" );
			}
			catch ( Exception exception )
			{
				app.Logger.WriteLine( "[LFE] Failed to create FMOD capture device: " + exception.Message.Trim() );
			}
		}

		app.Logger.WriteLine( "[LFE] <<< CreateCaptureDevice" );
	}

	private void ReleaseCaptureDevice()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LFE] ReleaseCaptureDevice >>>" );

		if ( _captureDeviceCreated && _captureSystem.HasValue )
		{
			if ( _captureDriverId >= 0 )
			{
				_captureSystem.Value.recordStop( _captureDriverId );
			}

			_captureSound.release();

			_captureSystem.Value.close();
			_captureSystem.Value.release();
			_captureSystem = null;

			_captureDriverId = -1;
		}

		Array.Clear( _magnitude );

		_captureDeviceCreated = false;
		_lastProcessedWritePosSamples = -1;

		app.Logger.WriteLine( "[LFE] <<< ReleaseCaptureDevice" );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private void Update( App app )
	{
		if ( NextCaptureDeviceName != null )
		{
			app.Logger.WriteLine( $"[LFE] Switching to the next capture device: '{NextCaptureDeviceName}'" );

			_configuredCaptureDeviceName = NextCaptureDeviceName;

			NextCaptureDeviceName = null;

			if ( app.Simulator.IsOnTrack )
			{
				ReleaseCaptureDevice();
				CreateCaptureDevice();
			}
		}
		else if ( app.Simulator.IsOnTrack && !_captureDeviceCreated && !string.IsNullOrEmpty( _configuredCaptureDeviceName ) )
		{
			app.Logger.WriteLine( "[LFE] Went on track - creating capture device" );

			CreateCaptureDevice();
		}
		else if ( !app.Simulator.IsOnTrack && _captureDeviceCreated )
		{
			app.Logger.WriteLine( "[LFE] Went off track - releasing capture device" );

			ReleaseCaptureDevice();
		}

		if ( _captureDeviceCreated && _captureSystem.HasValue )
		{
			_captureSystem.Value.update();

			if ( Interlocked.Exchange( ref _lfeBusy, 1 ) == 0 )
			{
				_captureSystem.Value.getRecordPosition( _captureDriverId, out var writePosSamples );

				var alignedWritePosSamples = (int) ( writePosSamples / _frameSizeInSamples ) * _frameSizeInSamples;

				if ( alignedWritePosSamples == _lastProcessedWritePosSamples )
				{
					_lfeBusy = 0;
					return;
				}

				_lastProcessedWritePosSamples = alignedWritePosSamples;

				var readPosSamples = ( alignedWritePosSamples + _captureBufferNumSamples - _frameSizeInSamples ) % _captureBufferNumSamples;
				var readOffsetBytes = readPosSamples * _bytesPerSample;

				var lockResult = _captureSound.@lock( (uint) readOffsetBytes, (uint) _frameSizeInBytes, out var ptr1, out var ptr2, out var len1, out var len2 );

				if ( lockResult == FMOD.RESULT.OK )
				{
					var bytesCopied = 0;

					if ( ptr1 != IntPtr.Zero && len1 > 0 )
					{
						Marshal.Copy( ptr1, _scratchRead, bytesCopied, (int) len1 );
						bytesCopied += (int) len1;
					}

					if ( ptr2 != IntPtr.Zero && len2 > 0 )
					{
						Marshal.Copy( ptr2, _scratchRead, bytesCopied, (int) len2 );
					}

					_captureSound.unlock( ptr1, ptr2, len1, len2 );

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
				}

				_lfeBusy = 0;
			}
		}
	}

	private static void WorkerThread()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LFE] Worker thread started" );

		var lfe = app.LFE;

		try
		{
			while ( lfe._running )
			{
				lfe._autoResetEvent.WaitOne( 10 );

				lfe.Update( app );
			}
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[LFE] Exception caught: {exception.Message}" );

			app.ShowFatalError( "An exception was thrown inside the LFE worker thread.", exception );
		}

		app.Logger.WriteLine( "[LFE] Worker thread stopped" );
	}
}
