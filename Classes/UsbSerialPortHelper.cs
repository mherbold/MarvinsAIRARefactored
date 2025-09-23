
using System.IO.Ports;
using System.Management;
using System.Text;

namespace MarvinsAIRARefactored.Classes;

public sealed class UsbSerialPortHelper( string handshake = "", string deviceIdMustContain = "", string fallbackVid = "", string fallbackPid = "", int baudRate = 115200, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One ) : IDisposable
{
	public bool DeviceFound { get => _portName != string.Empty; }

	public event EventHandler<string>? DataReceived = null;
	public event EventHandler? PortClosed = null;

	private readonly string _handshake = handshake;
	private readonly string _deviceIdMustContain = deviceIdMustContain;

	private readonly string _fallbackVid = fallbackVid.ToUpper();
	private readonly string _fallbackPid = fallbackPid.ToUpper();

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

		using var searcher = new ManagementObjectSearcher( "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'" );

		var fallbackPortName = string.Empty;

		foreach ( var device in searcher.Get() )
		{
			var name = device[ "Name" ]?.ToString();
			var deviceId = device[ "PNPDeviceID" ]?.ToString();

			if ( !string.IsNullOrEmpty( name ) && !string.IsNullOrEmpty( deviceId ) )
			{
				if ( ( _deviceIdMustContain == string.Empty ) || !deviceId.Contains( _deviceIdMustContain, StringComparison.OrdinalIgnoreCase ) )
				{
					var start = name.IndexOf( "(COM" );
					var end = name.IndexOf( ')', start );

					if ( ( start >= 0 ) && ( end >= 0 ) )
					{
						var portName = name.Substring( start + 1, end - start - 1 );

						// try handshake first

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

						// try VID/PID second (and use as fallback)

						if ( ( _fallbackVid != string.Empty ) && ( _fallbackPid != string.Empty ) )
						{
							if ( deviceId.Contains( $"VID_{_fallbackVid}", StringComparison.OrdinalIgnoreCase ) && deviceId.Contains( $"PID_{_fallbackPid}", StringComparison.OrdinalIgnoreCase ) )
							{
								fallbackPortName = portName;
							}
						}
					}
				}
			}
		}

		if ( _portName == string.Empty )
		{
			if ( fallbackPortName != string.Empty )
			{
				app.Logger.WriteLine( $"[UsbSerialPortHelper] Device found on port {fallbackPortName} (using fallback method)" );

				_portName = fallbackPortName;
			}
			else
			{
				app.Logger.WriteLine( "[UsbSerialPortHelper] Device not found" );
			}
		}

		app.Logger.WriteLine( "[UsbSerialPortHelper] <<< Initialize" );
	}

	public bool Open()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[UsbSerialPortHelper] Open >>>" );

		var serialPortOpened = false;

		if ( DeviceFound )
		{
			using ( _lock.EnterScope() )
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
				_serialPort.Write( data, 0, data.Length );
			}
		}
	}

	public void WriteLine( string data )
	{
		using ( _lock.EnterScope() )
		{
			if ( _serialPort != null && _serialPort.IsOpen )
			{
				_serialPort.WriteLine( data );
			}
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
			await Task.Delay( 1000, token );

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
