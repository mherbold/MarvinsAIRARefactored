using System;
using System.IO.Ports;
using System.Threading;

public class ArduinoConnection
{
	#region EventArgs

	public class ConnectionEventArgs : EventArgs
	{
		public readonly SerialPort? ArduinoPort;
		public readonly DateTime ConnectionTime;

		public ConnectionEventArgs( SerialPort? arduinoPort = null )
		{
			ArduinoPort = arduinoPort;
			ConnectionTime = DateTime.Now;
		}
	}

	#endregion

	#region Events and Delegates

	public delegate void ArduinoConnectedHandler( object connection, ConnectionEventArgs connectionInformation );
	public delegate void ArduinoSearchingHandler( object connection, ConnectionEventArgs connectionInformation );
	public delegate void ArduinoDisconnectedHandler( object connection, ConnectionEventArgs connectionInformation );

	public event ArduinoConnectedHandler? ArduinoConnected = null;
	public event ArduinoSearchingHandler? ArduinoSearching = null;
	public event ArduinoDisconnectedHandler? ArduinoDisconnected = null;

	#endregion

	#region Fields

	private readonly byte[] handshake;
	private SerialPort? arduinoPort;
	private bool run;
	private Thread runThread;

	#endregion

	#region Properties

	/// <summary>
	/// Getter for Arduino SerialPort
	/// </summary>
	public SerialPort? ArduinoPort { get => arduinoPort; }

	#endregion

	#region Constructors

	/// <summary>
	/// Constructor for ArduinoConnection
	/// </summary>
	/// <param name="handshake">Handshake byte[] for synchronization with Arduino Board</param>
	public ArduinoConnection( byte[] handshake )
	{
		this.handshake = handshake;

		runThread = new Thread( RunConnection );
	}

	#endregion

	#region Methods

	/// <summary>
	/// Main execution method to work with ArduinoConnection
	/// </summary>
	private void RunConnection()
	{
		arduinoPort = ConnectPort();

		OnArduinoConnected( this, new ConnectionEventArgs( arduinoPort ) );

		do
		{
			if ( !arduinoPort.IsOpen )
			{
				OnArduinoDisconnected( this, new ConnectionEventArgs() );

				arduinoPort = ConnectPort();

				OnArduinoConnected( this, new ConnectionEventArgs( arduinoPort ) );
			}

			Thread.Sleep( 300 );
		}
		while ( run );

		if ( arduinoPort != null && arduinoPort.IsOpen )
			arduinoPort.Close();
	}

	public void Start()
	{
		runThread.Start();
		run = true;
	}

	public void Stop()
	{
		run = false;
	}

	/// <summary>
	/// Checks for available Serial Ports and returns the port with the desired Arduino Board
	/// </summary>
	/// <returns>Port used by desired Arduino Board</returns>
	private SerialPort ConnectPort()
	{
		SerialPort temporalPort, finalPort = new SerialPort();
		bool arduinoConnected = false;
		byte[] readHandshake = new byte[ 4 ];

		//Launch searching event
		OnArduinoSearch( this, new ConnectionEventArgs() );

		//While there is not any board connected
		while ( !arduinoConnected )
		{
			Thread.Sleep( 1000 );

			if ( !run )
				break;

			//Checks all serial ports available on the PC
			foreach ( string portName in SerialPort.GetPortNames() )
			{
				if ( !run )
					break;

				temporalPort = new SerialPort( portName );

				//Checks if the current port is used
				if ( !temporalPort.IsOpen )
				{
					finalPort.PortName = portName;

					try
					{
						finalPort.BaudRate = 9600;
						finalPort.WriteTimeout = 500;
						finalPort.ReadTimeout = 500;
						finalPort.Open(); //Open port

						finalPort.Write( handshake, 0, handshake.Length ); //Send private handshake to Arduino

						//Wait until all bytes from Arduino handshake are ready
						int count = 0;
						while ( count < readHandshake.Length )
						{
							try
							{
								count += finalPort.Read( readHandshake, count, readHandshake.Length - count );
							}
							catch ( TimeoutException )
							{
								goto Final;
							}
						}

						//If handshakes are the same, desired Arduino Board is found
						if ( CheckHandshake( readHandshake ) )
						{
							arduinoConnected = true;
							break;
						}
						else
						{
							finalPort.Close();
							arduinoConnected = false;
							break;
						}
					}
					catch
					{
						if ( finalPort.IsOpen )
							finalPort.Close();
						arduinoConnected = false;
					}
				}

			Final:
				finalPort.Close();
				readHandshake = new Byte[ 4 ];
			}
		}

		return finalPort;
	}

	#region Event methods

	/// <summary>
	/// Method to launch the Connection event
	/// </summary>
	/// <param name="connection">Who sends the event</param>
	/// <param name="connectionInformation">Information about the connection</param>
	protected void OnArduinoConnected( object connection, ConnectionEventArgs connectionInformation )
	{
		ArduinoConnected?.Invoke( connection, connectionInformation );
	}

	/// <summary>
	/// Method to launch the Searching event
	/// </summary>
	/// <param name="connection">Who sends the event</param>
	/// <param name="connectionInformation">Information about the connection</param>
	protected void OnArduinoSearch( object connection, ConnectionEventArgs connectionInformation )
	{
		ArduinoSearching?.Invoke( connection, connectionInformation );
	}

	/// <summary>
	/// Method to launch the Disconnection event
	/// </summary>
	/// <param name="connection">Who sends the event</param>
	/// <param name="connectionInformation">Information about the disconnection</param>
	protected void OnArduinoDisconnected( object connection, ConnectionEventArgs connectionInformation )
	{
		ArduinoDisconnected?.Invoke( connection, connectionInformation );
	}

	#endregion

	/// <summary>
	/// Checks public Arduino handshake with private handshake
	/// </summary>
	/// <param name="response">Readed handshake from Arduino</param>
	/// <returns>True if they are the same, false otherwise</returns>
	private bool CheckHandshake( byte[] response )
	{
		if ( response.Length != handshake.Length )
			return false;

		for ( int i = 0; i < response.Length; i++ )
			if ( response[ i ] != handshake[ i ] )
				return false;

		return true;
	}

	#endregion
}
