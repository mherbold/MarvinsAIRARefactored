
using System.IO.Ports;
using System.Management;
using System.Text;

namespace MarvinsAIRARefactored.Classes;

public class UsbSerialPortHelper( string vid, string pid ) : IDisposable
{
	public event EventHandler<string>? DataReceived = null;
	public event EventHandler? PortClosed = null;

	private readonly string _vid = vid.ToUpper();
	private readonly string _pid = pid.ToUpper();

	private SerialPort? _serialPort = null;
	private CancellationTokenSource? _cancellationTokenSource = null;

	private StringBuilder _dataBuffer = new();

	private readonly Lock _lock = new();

	public string? FindPortName()
	{
		var app = App.Instance!;

		using var searcher = new ManagementObjectSearcher( "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'" );

		foreach ( var device in searcher.Get() )
		{
			var name = device[ "Name" ]?.ToString();
			var deviceId = device[ "PNPDeviceID" ]?.ToString();

			if ( !string.IsNullOrEmpty( name ) && !string.IsNullOrEmpty( deviceId ) )
			{
				if ( deviceId.Contains( $"VID_{_vid}", StringComparison.OrdinalIgnoreCase ) && deviceId.Contains( $"PID_{_pid}", StringComparison.OrdinalIgnoreCase ) )
				{
					if ( !deviceId.Contains( "MI_00", StringComparison.OrdinalIgnoreCase ) )
					{
						var start = name.IndexOf( "(COM" );
						var end = name.IndexOf( ')', start );

						if ( ( start >= 0 ) && ( end >= 0 ) )
						{
							var portName = name.Substring( start + 1, end - start - 1 );

							app.Logger.WriteLine( $"[UsbSerialPortHelper] Device found on port {portName}" );

							return portName;
						}
					}
				}
			}
		}

		app.Logger.WriteLine( "[UsbSerialPortHelper] Device not found" );

		return null;
	}

	public bool Open( int baudRate = 115200, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One )
	{
		var serialPortOpened = false;

		var app = App.Instance!;

		app.Logger.WriteLine( "[UsbSerialPortHelper] Open >>>" );

		using ( _lock.EnterScope() )
		{
			var portName = FindPortName();

			if ( portName != null )
			{
				_serialPort = new SerialPort( portName, baudRate, parity, dataBits, stopBits )
				{
					Handshake = Handshake.None,
					Encoding = Encoding.ASCII,
					ReadTimeout = 3000,
					WriteTimeout = 3000
				};

				_serialPort.DataReceived += OnDataReceived;

				_serialPort.Open();
				_serialPort.DiscardInBuffer();

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
