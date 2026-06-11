
using System.IO.Ports;
using System.Management;
using System.Text;

namespace MarvinsAIRARefactored.Classes;

public sealed class UsbSerialPortHelper( string handshake = "", string deviceIdMustNotContain = "", string vid = "", string pid = "", int baudRate = 115200, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One ) : IDisposable
{
	public bool DeviceFound { get => _portName != string.Empty; }
	public string LastErrorMessage { get; private set; } = string.Empty;

	public event EventHandler<string>? DataReceived = null;
	public event EventHandler? PortClosed = null;

	private readonly string _handshake = handshake;
	private readonly string _deviceIdMustNotContain = deviceIdMustNotContain;

	private readonly string _vid = vid.ToUpper();
	private readonly string _pid = pid.ToUpper();

	private readonly int _baudRate = baudRate;
	private readonly Parity _parity = parity;
	private readonly int _dataBits = dataBits;
	private readonly StopBits _stopBits = stopBits;

	private string _portName = string.Empty;

	private SerialPort? _serialPort = null;
	private CancellationTokenSource? _cancellationTokenSource = null;

	private readonly StringBuilder _dataBuffer = new();

	private readonly Lock _lock = new();

	public void Initialize()
	{
		var app = App.Instance!;
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		app.Logger.WriteLine( "[UsbSerialPortHelper] Initialize >>>" );
		app.Logger.WriteLine( $"[UsbSerialPortHelper] Search criteria: handshake='{_handshake}', deviceIdMustNotContain='{_deviceIdMustNotContain}', vid='{_vid}', pid='{_pid}', baudRate={_baudRate}, parity={_parity}, dataBits={_dataBits}, stopBits={_stopBits}" );

		_portName = string.Empty;
		LastErrorMessage = string.Empty;

		try
		{
			using var searcher = new ManagementObjectSearcher( "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'" );
			using var devices = searcher.Get();

			var inspectedDeviceCount = 0;

			foreach ( var device in devices )
			{
				inspectedDeviceCount++;

				var name = device[ "Name" ]?.ToString();
				var deviceId = device[ "PNPDeviceID" ]?.ToString();

				app.Logger.WriteLine( $"[UsbSerialPortHelper] Device #{inspectedDeviceCount}: Name='{name}', PNPDeviceID='{deviceId}'" );

				if ( string.IsNullOrEmpty( name ) || string.IsNullOrEmpty( deviceId ) )
				{
					app.Logger.WriteLine( "[UsbSerialPortHelper] Skipping device because Name or PNPDeviceID is empty" );
					continue;
				}

				var start = name.IndexOf( "(COM" );

				if ( start < 0 )
				{
					app.Logger.WriteLine( $"[UsbSerialPortHelper] Skipping device '{name}' because COM token was not found" );
					continue;
				}

				var end = name.IndexOf( ')', start );

				if ( end < 0 )
				{
					app.Logger.WriteLine( $"[UsbSerialPortHelper] Skipping device '{name}' because COM token did not have a closing parenthesis" );
					continue;
				}

				var portName = name.Substring( start + 1, end - start - 1 );

				app.Logger.WriteLine( $"[UsbSerialPortHelper] Parsed serial port '{portName}' from '{name}'" );

				if ( _handshake != string.Empty )
				{
					if ( !name.Contains( "CH340", StringComparison.OrdinalIgnoreCase ) )
					{
						app.Logger.WriteLine( $"[UsbSerialPortHelper] Skipping handshake probe on '{portName}' because device name '{name}' does not contain 'CH340'" );
						continue;
					}

					app.Logger.WriteLine( $"[UsbSerialPortHelper] Testing handshake mode on '{portName}'" );

					try
					{
						using var testPort = new SerialPort( portName, _baudRate, _parity, _dataBits, _stopBits )
						{
							Handshake = Handshake.None,
							Encoding = Encoding.ASCII,
							ReadTimeout = 500,
							WriteTimeout = 500,
							NewLine = "\n"
						};

						testPort.Open();
						testPort.DiscardInBuffer();
						testPort.DiscardOutBuffer();
						testPort.WriteLine( "WHAT ARE YOU?" );

						Thread.Sleep( 200 );

						var response = testPort.ReadExisting()?.Trim();

						app.Logger.WriteLine( $"[UsbSerialPortHelper] Handshake response on '{portName}': '{response}'" );

						if ( !string.IsNullOrEmpty( response ) && response.Contains( _handshake, StringComparison.OrdinalIgnoreCase ) )
						{
							app.Logger.WriteLine( $"[UsbSerialPortHelper] Handshake successful on '{portName}'" );

							_portName = portName;

							break;
						}

						app.Logger.WriteLine( $"[UsbSerialPortHelper] Handshake token '{_handshake}' not found in response from '{portName}'" );
					}
					catch ( Exception exception )
					{
						app.Logger.WriteLine( $"[UsbSerialPortHelper] Handshake failed on '{portName}': {exception.Message}" );
					}
				}
				else
				{
					var deviceIdIsGood = ( _deviceIdMustNotContain == string.Empty ) || !deviceId.Contains( _deviceIdMustNotContain, StringComparison.OrdinalIgnoreCase );

					if ( !deviceIdIsGood )
					{
						app.Logger.WriteLine( $"[UsbSerialPortHelper] Skipping '{portName}' because PNPDeviceID contains excluded token '{_deviceIdMustNotContain}'" );
						continue;
					}

					if ( ( _vid == string.Empty ) || ( _pid == string.Empty ) )
					{
						app.Logger.WriteLine( "[UsbSerialPortHelper] VID/PID mode selected but VID or PID is empty; skipping device matching" );
						continue;
					}

					var matchesVid = deviceId.Contains( $"VID_{_vid}", StringComparison.OrdinalIgnoreCase );
					var matchesPid = deviceId.Contains( $"PID_{_pid}", StringComparison.OrdinalIgnoreCase );

					app.Logger.WriteLine( $"[UsbSerialPortHelper] VID/PID check on '{portName}': matchesVid={matchesVid}, matchesPid={matchesPid}" );

					if ( matchesVid && matchesPid )
					{
						_portName = portName;

						app.Logger.WriteLine( $"[UsbSerialPortHelper] Selected port '{_portName}' based on VID/PID match" );

						break;
					}
				}
			}

			app.Logger.WriteLine( $"[UsbSerialPortHelper] Device scan complete. inspectedDeviceCount={inspectedDeviceCount}, selectedPort='{_portName}'" );

			if ( _portName == string.Empty )
			{
				app.Logger.WriteLine( "[UsbSerialPortHelper] Device not found" );

				LastErrorMessage = DataContext.DataContext.Instance.Localization[ "DeviceNotFound" ];
			}
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[UsbSerialPortHelper] Unexpected error during device search: {exception.Message}" );

			LastErrorMessage = exception.Message;
		}

		app.Logger.WriteLine( $"[UsbSerialPortHelper] <<< Initialize (DeviceFound={DeviceFound}, SelectedPort='{_portName}', elapsed={stopwatch.ElapsedMilliseconds}ms)" );
	}

	public bool Open()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[UsbSerialPortHelper] Open >>>" );

		var serialPortOpened = false;

		LastErrorMessage = string.Empty;

		if ( DeviceFound )
		{
			using ( _lock.EnterScope() )
			{
				try
				{
					_serialPort = new SerialPort( _portName, _baudRate, _parity, _dataBits, _stopBits )
					{
						Handshake = Handshake.None,
						Encoding = Encoding.ASCII,
						ReadTimeout = 3000,
						WriteTimeout = 3000,
						NewLine = "\n"
					};

					_serialPort.DataReceived += OnDataReceived;

					_serialPort.Open();
					_serialPort.DiscardInBuffer();
					_serialPort.DiscardOutBuffer();

					_cancellationTokenSource = new();

					_ = Task.Run( () => MonitorPort( _cancellationTokenSource.Token ) );

					serialPortOpened = true;
				}
				catch ( Exception exception )
				{
					app.Logger.WriteLine( $"[UsbSerialPortHelper] Failed to open serial port {_portName}: {exception.Message}" );

					LastErrorMessage = exception.Message;

					if ( _serialPort != null )
					{
						_serialPort.DataReceived -= OnDataReceived;
						_serialPort.Dispose();
						_serialPort = null;
					}
				}
			}
		}
		else
		{
			LastErrorMessage = DataContext.DataContext.Instance.Localization[ "DeviceNotFound" ];
		}

		app.Logger.WriteLine( "[UsbSerialPortHelper] <<< Open" );

		return serialPortOpened;
	}

	public void Close()
	{
		if ( _serialPort != null )
		{
			var app = App.Instance!;

			app.Logger.WriteLine( "[UsbSerialPortHelper] Closing serial port" );

			using ( _lock.EnterScope() )
			{
				_serialPort.DataReceived -= OnDataReceived;

				if ( _serialPort.IsOpen )
				{
					try
					{
						_serialPort.BaseStream.Flush();
					}
					catch
					{
					}

					_serialPort.Close();
				}

				_serialPort.Dispose();

				_serialPort = null;
			}
		}
	}

	public void Dispose()
	{
		GC.SuppressFinalize( this );

		Close();
	}

	public void Write( byte[] data )
	{
		using ( _lock.EnterScope() )
		{
			if ( _serialPort != null && _serialPort.IsOpen )
			{
				try
				{
					_serialPort.Write( data, 0, data.Length );
				}
				catch ( Exception exception )
				{
					HandleWriteFailure( "Write", exception );
				}
			}
		}
	}

	public void Write( ReadOnlySpan<byte> data )
	{
		using ( _lock.EnterScope() )
		{
			if ( _serialPort != null && _serialPort.IsOpen )
			{
				try
				{
					_serialPort.BaseStream.Write( data );
				}
				catch ( Exception exception )
				{
					HandleWriteFailure( "Write", exception );
				}
			}
		}
	}

	public void WriteLine( string data )
	{
		using ( _lock.EnterScope() )
		{
			if ( _serialPort != null && _serialPort.IsOpen )
			{
				try
				{
					_serialPort.WriteLine( data );
				}
				catch ( Exception exception )
				{
					HandleWriteFailure( "WriteLine", exception );
				}
			}
		}
	}

	public void WriteLine( ReadOnlySpan<byte> data )
	{
		using ( _lock.EnterScope() )
		{
			if ( _serialPort != null && _serialPort.IsOpen )
			{
				try
				{
					_serialPort.BaseStream.Write( data );

					if ( data.Length == 0 || data[ ^1 ] != (byte) '\n' )
					{
						_serialPort.BaseStream.WriteByte( (byte) '\n' );
					}
				}
				catch ( Exception exception )
				{
					HandleWriteFailure( "WriteLine", exception );
				}
			}
		}
	}

	private void HandleWriteFailure( string operation, Exception exception )
	{
		var app = App.Instance;

		app?.Logger.WriteLine( $"[UsbSerialPortHelper] {operation} failed: {exception.Message} : '{_portName}'." );

		if ( _serialPort == null || !_serialPort.IsOpen )
		{
			return;
		}

		try
		{
			_serialPort.Close();
		}
		catch ( Exception closeException )
		{
			app?.Logger.WriteLine( $"[UsbSerialPortHelper] Failed to close serial port {_portName} after {operation} failure: {closeException.Message}" );
		}
	}

	private void HandleReadFailure( string operation, Exception exception )
	{
		var app = App.Instance;

		app?.Logger.WriteLine( $"[UsbSerialPortHelper] {operation} failed: {exception.Message} : '{_portName}'." );

		if ( _serialPort == null || !_serialPort.IsOpen )
		{
			return;
		}

		try
		{
			_serialPort.Close();
		}
		catch ( Exception closeException )
		{
			app?.Logger.WriteLine( $"[UsbSerialPortHelper] Failed to close serial port {_portName} after {operation} failure: {closeException.Message}" );
		}
	}

	private void OnDataReceived( object sender, SerialDataReceivedEventArgs e )
	{
		var app = App.Instance;

		try
		{
			if ( _serialPort != null )
			{
				// app?.Logger.WriteLine( $"[UsbSerialPortHelper] OnDataReceived: EventType={e.EventType}, BytesToRead={_serialPort.BytesToRead}" );

				var incoming = _serialPort.ReadExisting();

				_dataBuffer.Append( incoming );

				var newlineIndex = 0;

				while ( ( newlineIndex = _dataBuffer.ToString().IndexOf( '\n' ) ) >= 0 )
				{
					var data = _dataBuffer.ToString( 0, newlineIndex ).TrimEnd( '\r' );

					_dataBuffer.Remove( 0, newlineIndex + 1 );

					// app?.Logger.WriteLine( $"[UsbSerialPortHelper] OnDataReceived: Dispatching line '{data}'" );

					DataReceived?.Invoke( this, data );
				}
			}
		}
		catch ( Exception exception )
		{
			HandleReadFailure( "OnDataReceived", exception );
		}
	}

	private async Task MonitorPort( CancellationToken token )
	{
		var app = App.Instance;

		app?.Logger.WriteLine( $"[UsbSerialPortHelper] MonitorPort started for '{_portName}'." );

		while ( !token.IsCancellationRequested )
		{
			try
			{
				await Task.Delay( 1000, token );
			}
			catch ( OperationCanceledException )
			{
				app?.Logger.WriteLine( $"[UsbSerialPortHelper] MonitorPort cancelled for '{_portName}'." );
				break;
			}

			using ( _lock.EnterScope() )
			{
				var portIsNull = _serialPort == null;
				var portIsOpen = _serialPort?.IsOpen ?? false;

				if ( portIsNull || !portIsOpen )
				{
					app?.Logger.WriteLine( $"[UsbSerialPortHelper] MonitorPort detected '{_portName}' is no longer open; closing and raising PortClosed." );

					Close();
					PortClosed?.Invoke( this, EventArgs.Empty );
					break;
				}
			}
		}

		app?.Logger.WriteLine( $"[UsbSerialPortHelper] MonitorPort exited for '{_portName}'." );
	}
}
