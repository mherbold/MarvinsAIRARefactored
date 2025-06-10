
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Components;

public class AdminBoxx
{
	public bool IsConnected { get; private set; } = false;

	private readonly UsbSerialPortHelper _usbSerialPortHelper = new( "239A", "80F2" );

	private int counter = 60 * 2;

	public AdminBoxx()
	{
		_usbSerialPortHelper.DataReceived += OnDataReceived;
	}

	public bool Connect()
	{
		IsConnected = _usbSerialPortHelper.Open();

		return IsConnected;
	}

	public void Disconnect()
	{
		IsConnected = false;

		_usbSerialPortHelper.Close();
	}

	public void Tick( App app )
	{
		counter--;

		if ( counter == 0 )
		{
			counter = 2 * 60;

			_usbSerialPortHelper.WriteLine( "PING" );
		}
	}

	private void OnDataReceived( object? sender, string e )
	{
	}
}
