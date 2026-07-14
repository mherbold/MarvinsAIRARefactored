
using System.IO.Ports;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace MarvinsAIRARefactored.Classes;

// Set to true to enable verbose logging of the noisy read/write functions. Leave false for daily use to keep the log file small.
#pragma warning disable CS0162 // Unreachable code detected (intentional: _verboseLogging is a compile-time toggle)

public sealed class UsbSerialPortHelper( string handshake = "", string deviceIdToAvoid = "", string vid = "", string pid = "", int baudRate = 115200, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One, byte[]? probeData = null, Regex? probeResponseRegex = null ) : IDisposable
{
	private const bool _verboseLogging = false;
	private const int _probeTimeoutMilliseconds = 1500;

	// USB-serial bridge chips found on Arduino Nano boards and clones - handshake probes are only sent to ports whose
	// device name or PNPDeviceID matches one of these, so we never write probe text at unrelated serial devices.
	// CH340/CH341/CH9102 (WCH, VID 1A86), FT232R (FTDI, VID 0403), CP210x (Silicon Labs, VID 10C4),
	// PL2303 (Prolific, VID 067B), and genuine Arduino USB interfaces (VID 2341 / 2A03).
	private static readonly string[] _knownUsbSerialBridgeTokens = [ "CH340", "CH341", "CH9102", "VID_1A86", "FTDI", "VID_0403", "CP210", "VID_10C4", "Prolific", "VID_067B", "Arduino", "VID_2341", "VID_2A03" ];

	public bool DeviceFound { get => _portName != string.Empty; }
	public string LastErrorMessage { get; private set; } = string.Empty;

	public event EventHandler<string>? DataReceived = null;
	public event EventHandler? PortClosed = null;

	private readonly string _handshake = handshake;
	private readonly string _deviceIdToAvoid = deviceIdToAvoid;

	private readonly byte[]? _probeData = probeData;
	private readonly Regex? _probeResponseRegex = probeResponseRegex;

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
		app.Logger.WriteLine( $"[UsbSerialPortHelper] Search criteria: handshake='{_handshake}', deviceIdToAvoid='{_deviceIdToAvoid}', vid='{_vid}', pid='{_pid}', baudRate={_baudRate}, parity={_parity}, dataBits={_dataBits}, stopBits={_stopBits}" );

		_portName = string.Empty;
		LastErrorMessage = string.Empty;

		try
		{
			using var searcher = new ManagementObjectSearcher( "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'" );
			using var devices = searcher.Get();

			var inspectedDeviceCount = 0;
			var fallbackPortName = string.Empty;
			var candidatePorts = new List<(string portName, bool isPreferred)>();

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
					var matchedBridgeToken = _knownUsbSerialBridgeTokens.FirstOrDefault( token => name.Contains( token, StringComparison.OrdinalIgnoreCase ) || deviceId.Contains( token, StringComparison.OrdinalIgnoreCase ) );

					if ( matchedBridgeToken == null )
					{
						app.Logger.WriteLine( $"[UsbSerialPortHelper] Skipping handshake probe on '{portName}' because neither device name '{name}' nor PNPDeviceID '{deviceId}' matches a known USB-serial bridge chip" );
						continue;
					}

					app.Logger.WriteLine( $"[UsbSerialPortHelper] Port '{portName}' matched USB-serial bridge token '{matchedBridgeToken}'" );

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
						var isPreferredPort = ( _deviceIdToAvoid == string.Empty ) || !deviceId.Contains( _deviceIdToAvoid, StringComparison.OrdinalIgnoreCase );

						if ( ( _probeData != null ) && ( _probeResponseRegex != null ) )
						{
							candidatePorts.Add( (portName, isPreferredPort) );

							app.Logger.WriteLine( $"[UsbSerialPortHelper] Port '{portName}' matches VID/PID; added as a probe candidate (isPreferred={isPreferredPort})" );

							continue;
						}

						if ( isPreferredPort )
						{
							_portName = portName;

							app.Logger.WriteLine( $"[UsbSerialPortHelper] Selected port '{_portName}' based on VID/PID match" );

							break;
						}

						if ( fallbackPortName == string.Empty )
						{
							fallbackPortName = portName;

							app.Logger.WriteLine( $"[UsbSerialPortHelper] Port '{portName}' matches VID/PID but PNPDeviceID contains '{_deviceIdToAvoid}'; keeping it as a fallback" );
						}
					}
				}
			}

			if ( ( _portName == string.Empty ) && ( fallbackPortName != string.Empty ) )
			{
				_portName = fallbackPortName;

				app.Logger.WriteLine( $"[UsbSerialPortHelper] Selected fallback port '{_portName}' because no VID/PID match without '{_deviceIdToAvoid}' was found" );
			}

			// Probe candidates preferred-first so the data port answers before the console port (old firmware) is ever written to
			foreach ( var (candidatePortName, _) in candidatePorts.OrderByDescending( candidate => candidate.isPreferred ) )
			{
				if ( ProbePort( candidatePortName ) )
				{
					_portName = candidatePortName;

					app.Logger.WriteLine( $"[UsbSerialPortHelper] Selected port '{_portName}' based on successful probe response" );

					break;
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

	// Writes the probe message to the port and waits for a response line matching the expected pattern.
	// Only called on ports that already match the VID/PID filter - probe bytes must never be written to unrelated devices.
	private bool ProbePort( string portName )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[UsbSerialPortHelper] Probing '{portName}'" );

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
			testPort.Write( _probeData!, 0, _probeData!.Length );

			var responseBuffer = new StringBuilder();
			var stopwatch = System.Diagnostics.Stopwatch.StartNew();

			while ( stopwatch.ElapsedMilliseconds < _probeTimeoutMilliseconds )
			{
				Thread.Sleep( 50 );

				responseBuffer.Append( testPort.ReadExisting() );

				foreach ( var line in responseBuffer.ToString().Split( '\n' ) )
				{
					if ( _probeResponseRegex!.IsMatch( line.TrimEnd( '\r' ) ) )
					{
						app.Logger.WriteLine( $"[UsbSerialPortHelper] Probe successful on '{portName}': '{line.TrimEnd( '\r' )}'" );

						return true;
					}
				}
			}

			app.Logger.WriteLine( $"[UsbSerialPortHelper] Probe timed out on '{portName}'; response so far: '{responseBuffer}'" );
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[UsbSerialPortHelper] Probe failed on '{portName}': {exception.Message}" );
		}

		return false;
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
		var app = App.Instance;

		using ( _lock.EnterScope() )
		{
			if ( _serialPort != null && _serialPort.IsOpen )
			{
				try
				{
					if ( _verboseLogging )
					{
						app?.Logger.WriteLine( $"[UsbSerialPortHelper] Write: {DescribeBytes( data )}" );
					}

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
		var app = App.Instance;

		using ( _lock.EnterScope() )
		{
			if ( _serialPort != null && _serialPort.IsOpen )
			{
				try
				{
					if ( _verboseLogging )
					{
						app?.Logger.WriteLine( $"[UsbSerialPortHelper] Write: {DescribeBytes( data )}" );
					}

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
		var app = App.Instance;

		using ( _lock.EnterScope() )
		{
			if ( _serialPort != null && _serialPort.IsOpen )
			{
				try
				{
					if ( _verboseLogging )
					{
						app?.Logger.WriteLine( $"[UsbSerialPortHelper] WriteLine: {DescribeBytes( _serialPort.Encoding.GetBytes( data + _serialPort.NewLine ) )}" );
					}

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
		var app = App.Instance;

		using ( _lock.EnterScope() )
		{
			if ( _serialPort != null && _serialPort.IsOpen )
			{
				try
				{
					if ( _verboseLogging )
					{
						app?.Logger.WriteLine( $"[UsbSerialPortHelper] WriteLine payload: {DescribeBytes( data )}" );
					}

					_serialPort.BaseStream.Write( data );

					if ( data.Length == 0 || data[ ^1 ] != (byte) '\n' )
					{
						if ( _verboseLogging )
						{
							app?.Logger.WriteLine( $"[UsbSerialPortHelper] WriteLine terminator: {DescribeBytes( stackalloc byte[] { (byte) '\n' } )}" );
						}

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

	private static string DescribeBytes( ReadOnlySpan<byte> data )
	{
		return data.Length == 0 ? "len=0, hex=<empty>" : $"len={data.Length}, hex={Convert.ToHexString( data )}";
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
				var bytesToRead = _serialPort.BytesToRead;

				if ( _verboseLogging )
				{
					app?.Logger.WriteLine( $"[UsbSerialPortHelper] OnDataReceived: EventType={e.EventType}, BytesToRead={bytesToRead}" );
				}

				if ( bytesToRead <= 0 )
				{
					return;
				}

				var incomingBytes = new byte[ bytesToRead ];
				var bytesRead = _serialPort.Read( incomingBytes, 0, incomingBytes.Length );

				if ( _verboseLogging )
				{
					app?.Logger.WriteLine( $"[UsbSerialPortHelper] OnDataReceived: Received {DescribeBytes( incomingBytes.AsSpan( 0, bytesRead ) )}" );
				}

				var incoming = _serialPort.Encoding.GetString( incomingBytes, 0, bytesRead );

				_dataBuffer.Append( incoming );

				var newlineIndex = 0;

				while ( ( newlineIndex = _dataBuffer.ToString().IndexOf( '\n' ) ) >= 0 )
				{
					var data = _dataBuffer.ToString( 0, newlineIndex ).TrimEnd( '\r' );

					_dataBuffer.Remove( 0, newlineIndex + 1 );

					if ( _verboseLogging )
					{
						app?.Logger.WriteLine( $"[UsbSerialPortHelper] OnDataReceived: Dispatching bytes {DescribeBytes( _serialPort.Encoding.GetBytes( data ) )}" );
					}

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

#pragma warning restore CS0162 // Unreachable code detected
