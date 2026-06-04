
using System.IO.Ports;
using System.Management;
using System.Text;

namespace MarvinsAIRARefactored.Classes;

public sealed class UsbSerialPortHelper( string handshake = "", string deviceIdMustContain = "", string vid = "", string pid = "", int baudRate = 115200, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One ) : IDisposable
{
	public bool DeviceFound { get => _portName != string.Empty; }
	public string LastErrorMessage { get; private set; } = string.Empty;

	public event EventHandler<string>? DataReceived = null;
	public event EventHandler? PortClosed = null;

	private readonly string _handshake = handshake;
	private readonly string _deviceIdMustContain = deviceIdMustContain;

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

		app.Logger.WriteLine( "[UsbSerialPortHelper] Initialize >>>" );

		try
		{
			using var searcher = new ManagementObjectSearcher( "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'" );

			foreach ( var device in searcher.Get() )
			{
				var name = device[ "Name" ]?.ToString();
				var deviceId = device[ "PNPDeviceID" ]?.ToString();

				if ( !string.IsNullOrEmpty( name ) && !string.IsNullOrEmpty( deviceId ) )
				{
					var start = name.IndexOf( "(COM" );

					if ( start >= 0 )
					{
						var end = name.IndexOf( ')', start );

						if ( end >= 0 )
						{
							var portName = name.Substring( start + 1, end - start - 1 );

							if ( _handshake != string.Empty )
							{
								// use handshake method

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

									if ( !string.IsNullOrEmpty( response ) && response.Contains( _handshake, StringComparison.OrdinalIgnoreCase ) )
									{
										app.Logger.WriteLine( $"[UsbSerialPortHelper] Handshake successful on {portName}" );

										_portName = portName;

										break;
									}
								}
								catch ( Exception exception )
								{
									app.Logger.WriteLine( $"[UsbSerialPortHelper] Handshake failed on {portName}: {exception.Message}" );
								}
							}
							else
							{
								// use VID/PID method

								if ( ( _deviceIdMustContain == string.Empty ) || !deviceId.Contains( _deviceIdMustContain, StringComparison.OrdinalIgnoreCase ) )
								{
									if ( ( _vid != string.Empty ) && ( _pid != string.Empty ) )
									{
										if ( deviceId.Contains( $"VID_{_vid}", StringComparison.OrdinalIgnoreCase ) && deviceId.Contains( $"PID_{_pid}", StringComparison.OrdinalIgnoreCase ) )
										{
											_portName = portName;

											break;
										}
									}
								}
							}
						}
					}
				}
			}

			if ( _portName == string.Empty )
			{
				app.Logger.WriteLine( "[UsbSerialPortHelper] Device not found" );

				LastErrorMessage = DataContext.DataContext.Instance.Localization[ "DeviceNotFound" ];
			}
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[UsbSerialPortHelper] Unexpected error during device search: {exception.Message}" );
		}

		app.Logger.WriteLine( "[UsbSerialPortHelper] <<< Initialize" );
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

	private void OnDataReceived( object sender, SerialDataReceivedEventArgs e )
	{
		try
		{
			if ( _serialPort != null )
			{
				var incoming = _serialPort.ReadExisting();

				_dataBuffer.Append( incoming );

				var newlineIndex = 0;

				while ( ( newlineIndex = _dataBuffer.ToString().IndexOf( '\n' ) ) >= 0 )
				{
					var data = _dataBuffer.ToString( 0, newlineIndex ).TrimEnd( '\r' );

					_dataBuffer.Remove( 0, newlineIndex + 1 );

					DataReceived?.Invoke( this, data );
				}
			}
		}
		catch ( Exception )
		{
		}
	}

	private async Task MonitorPort( CancellationToken token )
	{
		while ( !token.IsCancellationRequested )
		{
			try
			{
				await Task.Delay( 1000, token );
			}
			catch ( OperationCanceledException )
			{
				break;
			}

			using ( _lock.EnterScope() )
			{
				if ( ( _serialPort == null ) || !_serialPort.IsOpen )
				{
					Close();
					PortClosed?.Invoke( this, EventArgs.Empty );
					break;
				}
			}
		}
	}
}
