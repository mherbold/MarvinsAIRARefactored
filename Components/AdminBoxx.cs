
namespace MarvinsAIRARefactored.Components;

public class AdminBoxx
{
	private readonly UsbSerialPortHelper _usbSerialPortHelper = new( "239A", "80F2" );

	private int counter = 60 * 2;

	public bool Connect()
	{
		return _usbSerialPortHelper.Open( 115200 );
	}

	public void Disconnect()
	{
		_usbSerialPortHelper.Close();
	}

	public void Tick( App app )
	{
		counter--;

		if ( counter == 0 )
		{
			counter = 60 * 10;

			_usbSerialPortHelper.SendData( "u" );
		}
	}
}
