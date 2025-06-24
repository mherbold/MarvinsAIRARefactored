
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

using Timer = System.Timers.Timer;

using IRSDKSharper;

using Color = MarvinsAIRARefactored.Classes.Color;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Components;

public partial class AdminBoxx
{
	public static Color Yellow { get; } = new( 1f, 1f, 0f );
	public static Color Green { get; } = new( 0f, 1f, 0f );
	public static Color White { get; } = new( 1f, 1f, 1f );
	public static Color Blue { get; } = new( 0f, 0f, 1f );
	public static Color Red { get; } = new( 1f, 0f, 0f );
	public static Color Cyan { get; } = new( 0f, 1f, 1f );
	public static Color Magenta { get; } = new( 1f, 0f, 1f );
	public static Color Disabled { get; } = new( 0f, 0f, 0f );

	public bool IsConnected { get; private set; } = false;

	private const int _numColumns = 8;
	private const int _numRows = 4;

	private readonly UsbSerialPortHelper _usbSerialPortHelper = new( "239A", "80F2" );

	private readonly Color[,] _colors = new Color[ _numRows, _numColumns ];

	private static readonly (int x, int y)[] _blueNoiseLedOrder =
	[
		(3, 2), (6, 0), (0, 3), (4, 1), (7, 2), (2, 0), (1, 3), (5, 2),
		(6, 3), (3, 0), (0, 0), (4, 2), (7, 0), (1, 0), (5, 3), (2, 2),
		(6, 1), (3, 3), (0, 2), (4, 0), (7, 3), (2, 1), (1, 2), (5, 0),
		(6, 2), (3, 1), (0, 1), (4, 3), (7, 1), (1, 1), (5, 1), (2, 3)
	];

	private static readonly (int x, int y)[] _wavingFlagLedOrder =
	[
		(0, 0),
		(0, 1), (1, 0),
		(0, 2), (1, 1), (2, 0),
		(0, 3), (1, 2), (2, 1), (3, 0),
		(1, 3), (2, 2), (3, 1), (4, 0),
		(2, 3), (3, 2), (4, 1), (5, 0),
		(3, 3), (4, 2), (5, 1), (6, 0),
		(4, 3), (5, 2), (6, 1), (7, 0),
		(5, 3), (6, 2), (7, 1),
		(6, 3), (7, 2),
		(7, 3)
	];

	private static readonly Color[,] _playbackDisabledColors = new Color[ _numRows, _numColumns ]
	{
		{ Green, Red, Red, Red, Cyan,  Cyan, Green, Green },
		{ Green, Red, Red, Red, Cyan,  Cyan, Cyan,  Cyan  },
		{ Green, Red, Red, Red, Green, Red,  Red,   Red   },
		{ Green, Red, Red, Red, Red,   Red,  Red,   Red   }
	};

	private static readonly Color[,] _playbackEnabledColors = new Color[ _numRows, _numColumns ]
	{
		{ Green, Red, Red, Red, Cyan,     Cyan,     Green,    Green    },
		{ Green, Red, Red, Red, Cyan,     Cyan,     Cyan,     Cyan     },
		{ Green, Red, Red, Red, Green,    Disabled, Cyan,     Cyan     },
		{ Green, Red, Red, Red, Disabled, Disabled, Disabled, Disabled }
	};

	private static readonly Color[,] _numpadEnabledColors = new Color[ _numRows, _numColumns ]
	{
		{ Red, Cyan,   Cyan, Cyan,  Red, Red, Red, Red },
		{ Red, Cyan,   Cyan, Cyan,  Red, Red, Red, Red },
		{ Red, Cyan,   Cyan, Cyan,  Red, Red, Red, Red },
		{ Red, Yellow, Cyan, Green, Red, Red, Red, Red }
	};

	private static readonly Regex ButtonPressRegex = MyRegex();

	private bool _globalChatEnabled = true;
	private HashSet<string> _driverChatDisabled = [];

	private bool _inNumpadMode = false;
	private bool _replayEnabled = false;
	private bool _blackFlagDriveThrough = false;
	private bool _singleFilePaceMode = false;
	private bool _carNumberIsRequired = false;

	private bool _shownYellowFlag = false;
	private bool _shownOneLapToGreenFlag = false;
	private bool _shownStartReadyFlag = false;
	private bool _shownStartSetFlag = false;
	private bool _shownGreenFlag = false;
	private bool _shownWhiteFlag = false;
	private bool _shownCheckeredFlag = false;
	private bool _shownBlackFlag = false;
	private bool _shownBlueFlag = false;
	private bool _shownRedFlag = false;

	private int _connectCounter = 0;
	private int _connectState = 0;

	private int _wavingFlagCounter = 0;
	private int _wavingFlagNumberOfTimes = 0;
	private int _wavingFlagState = 0;
	private Color _wavingFlagColor = Disabled;
	private bool _wavingFlagCheckered = false;

	private int _testCounter = 0;
	private int _testState = 0;

	private float _brightness = 1f;

	private string _carNumber = string.Empty;

	private delegate void CarNumberCallback();

	private CarNumberCallback? _carNumberCallback = null;

	private int _pingCounter = 0;

	private readonly ConcurrentQueue<(int y, int x)> _ledUpdateConcurrentQueue = new();
	private readonly HashSet<(int y, int x)> _ledUpdateHashSet = [];
	private readonly Lock _lock = new();

	private readonly Timer _timer = new( 10 );

	[GeneratedRegex( @"^:(\d+),(\d+)$", RegexOptions.Compiled )]
	private static partial Regex MyRegex();

	public AdminBoxx()
	{
		_usbSerialPortHelper.DataReceived += OnDataReceived;
		_usbSerialPortHelper.PortClosed += OnPortClosed;

		_timer.Elapsed += OnTimer;
	}

	public void Initialize()
	{
		_timer.Start();
	}

	public void Shutdown()
	{
		_timer.Stop();
	}

	public bool Connect()
	{
		var app = App.Instance!;

		IsConnected = _usbSerialPortHelper.Open();

		if ( IsConnected )
		{
			_pingCounter = 100;

			UpdateColors( _blueNoiseLedOrder, true );
		}

		app.Dispatcher.BeginInvoke( () =>
		{
			app.MainWindow.AdminBoxx_ConnectToAdminBoxx_MairaSwitch.IsOn = IsConnected;
		} );

		return IsConnected;
	}

	public void Disconnect()
	{
		var app = App.Instance!;

		IsConnected = false;

		_usbSerialPortHelper.Close();

		app.Dispatcher.BeginInvoke( () =>
		{
			app.MainWindow.AdminBoxx_ConnectToAdminBoxx_MairaSwitch.IsOn = false;
		} );
	}

	public void ResendAllLEDs( (int x, int y)[]? pattern = null )
	{
		pattern ??= _blueNoiseLedOrder;

		using ( _lock.EnterScope() )
		{
			foreach ( var (x, y) in pattern )
			{
				var coord = (y, x);

				if ( !_ledUpdateHashSet.Contains( coord ) )
				{
					_ledUpdateConcurrentQueue.Enqueue( coord );
					_ledUpdateHashSet.Add( coord );
				}
			}
		}
	}

	public void SetAllLEDsToColor( Color color, (int x, int y)[] pattern, bool forceUpdate, bool checkered = false, int evenOdd = 0 )
	{
		foreach ( var (x, y) in pattern )
		{
			if ( !checkered || ( ( ( x + y ) & 1 ) == evenOdd ) )
			{
				SetLEDToColor( y, x, color, forceUpdate );
			}
			else
			{
				SetLEDToColor( y, x, Disabled, forceUpdate );
			}
		}
	}

	public void SetAllLEDsToColorArray( Color[,] colors, (int x, int y)[] pattern, bool forceUpdate )
	{
		foreach ( var (x, y) in pattern )
		{
			var color = colors[ y, x ];

			SetLEDToColor( y, x, color, forceUpdate );
		}
	}

	public void SimulatorConnected()
	{
		UpdateColors( _blueNoiseLedOrder, true );
	}

	public void SimulatorDisconnected()
	{
		UpdateColors( _blueNoiseLedOrder, true );

		_inNumpadMode = false;

		_globalChatEnabled = true;

		_driverChatDisabled.Clear();
	}

	public void StartTestCycle()
	{
		_testState = 0;
		_testCounter = 1;
	}

	public void ReplayPlayingChanged()
	{
		var app = App.Instance!;

		_replayEnabled = ( app.Simulator.IsReplayPlaying );

		UpdateColors( _blueNoiseLedOrder, false );
	}

	public void SessionFlagsChanged()
	{
		var app = App.Instance!;

		// yellow flag / caution flag

		if ( (int) ( app.Simulator.SessionFlags & ( IRacingSdkEnum.Flags.Yellow | IRacingSdkEnum.Flags.YellowWaving | IRacingSdkEnum.Flags.Caution | IRacingSdkEnum.Flags.CautionWaving ) ) != 0 )
		{
			if ( !_shownYellowFlag )
			{
				_shownYellowFlag = true;

				WaveFlag( Yellow, 2 );
			}
		}
		else
		{
			_shownYellowFlag = false;
		}

		// one lap to green

		if ( (int) ( app.Simulator.SessionFlags & IRacingSdkEnum.Flags.OneLapToGreen ) != 0 )
		{
			if ( !_shownOneLapToGreenFlag )
			{
				_shownOneLapToGreenFlag = true;

				WaveFlag( Yellow, 1 );
			}
		}
		else
		{
			_shownOneLapToGreenFlag = false;
		}

		// start ready

		if ( (int) ( app.Simulator.SessionFlags & IRacingSdkEnum.Flags.StartReady ) != 0 )
		{
			if ( !_shownStartReadyFlag )
			{
				_shownStartReadyFlag = true;

				WaveFlag( Red, 1 );
			}
		}
		else
		{
			_shownStartReadyFlag = false;
		}

		// start set

		if ( (int) ( app.Simulator.SessionFlags & IRacingSdkEnum.Flags.StartSet ) != 0 )
		{
			if ( !_shownStartSetFlag )
			{
				_shownStartSetFlag = true;

				WaveFlag( Yellow, 1 );
			}
		}
		else
		{
			_shownStartSetFlag = false;
		}

		// start go / green flag

		if ( (int) ( app.Simulator.SessionFlags & ( IRacingSdkEnum.Flags.Green | IRacingSdkEnum.Flags.StartGo ) ) != 0 )
		{
			if ( !_shownGreenFlag )
			{
				_shownGreenFlag = true;

				WaveFlag( Green, 1 );
			}
		}
		else
		{
			_shownGreenFlag = false;
		}

		// white flag

		if ( (int) ( app.Simulator.SessionFlags & IRacingSdkEnum.Flags.White ) != 0 )
		{
			if ( !_shownWhiteFlag )
			{
				_shownWhiteFlag = true;

				WaveFlag( White, 3 );
			}
		}
		else
		{
			_shownWhiteFlag = false;
		}

		// checkered flag

		if ( (int) ( app.Simulator.SessionFlags & IRacingSdkEnum.Flags.Checkered ) != 0 )
		{
			if ( !_shownCheckeredFlag )
			{
				_shownCheckeredFlag = true;

				WaveFlag( White, 5, true );
			}
		}
		else
		{
			_shownCheckeredFlag = false;
		}

		// black flag

		if ( (int) ( app.Simulator.SessionFlags & IRacingSdkEnum.Flags.Black ) != 0 )
		{
			if ( !_shownBlackFlag )
			{
				_shownBlackFlag = true;

				WaveBlackFlag();
			}
		}
		else
		{
			_shownBlackFlag = false;
		}

		if ( (int) ( app.Simulator.SessionFlags & IRacingSdkEnum.Flags.Blue ) != 0 )
		{
			if ( !_shownBlueFlag )
			{
				_shownBlueFlag = true;

				WaveFlag( Blue, 1 );
			}
		}
		else
		{
			_shownBlueFlag = false;
		}

		if ( (int) ( app.Simulator.SessionFlags & IRacingSdkEnum.Flags.Red ) != 0 )
		{
			if ( !_shownRedFlag )
			{
				_shownRedFlag = true;

				WaveFlag( Red, 3 );
			}
		}
		else
		{
			_shownRedFlag = false;
		}
	}

	public void WaveBlackFlag()
	{
		var color = new Color( DataContext.DataContext.Instance.Settings.AdminBoxxBlackFlagR, DataContext.DataContext.Instance.Settings.AdminBoxxBlackFlagG, DataContext.DataContext.Instance.Settings.AdminBoxxBlackFlagB );

		WaveFlag( color, 3 );
	}

	private void WaveFlag( Color color, int numberOfTimes, bool checkered = false )
	{
		_brightness = 1f;

		_wavingFlagState = 0;
		_wavingFlagColor = color;
		_wavingFlagCheckered = checkered;
		_wavingFlagNumberOfTimes = numberOfTimes;
		_wavingFlagCounter = 1;
	}

	private void SetLEDToColor( int y, int x, Color color, bool forceUpdate )
	{
		if ( forceUpdate || ( _colors[ y, x ] != color ) )
		{
			_colors[ y, x ] = color;

			using ( _lock.EnterScope() )
			{
				var coord = (y, x);

				if ( !_ledUpdateHashSet.Contains( coord ) )
				{
					_ledUpdateConcurrentQueue.Enqueue( coord );
					_ledUpdateHashSet.Add( coord );
				}
			}
		}
	}

	private void UpdateColors( (int x, int y)[] pattern, bool forceUpdate )
	{
		var app = App.Instance!;

		if ( !app.Simulator.IsConnected )
		{
			SetAllLEDsToColor( Disabled, _wavingFlagLedOrder, forceUpdate );

			SetLEDToColor( 0, 1, Red, forceUpdate );
			SetLEDToColor( 0, 2, Yellow, forceUpdate );

			_connectState = 0;
			_connectCounter = 15;
		}
		else
		{
			if ( _inNumpadMode )
			{
				SetAllLEDsToColorArray( _numpadEnabledColors, pattern, forceUpdate );
			}
			else
			{
				if ( _replayEnabled )
				{
					SetAllLEDsToColorArray( _playbackEnabledColors, pattern, forceUpdate );
				}
				else
				{
					SetAllLEDsToColorArray( _playbackDisabledColors, pattern, forceUpdate );
				}
			}
		}
	}

	private void SendLED( int y, int x )
	{
		if ( IsConnected )
		{
			_pingCounter = 100;

			var brightness = _brightness * DataContext.DataContext.Instance.Settings.AdminBoxxBrightness;

			byte[] data =
			[
				129,
				(byte) ( y * 8 + x ),
				(byte) MathF.Round(_colors[ y, x ].R * brightness * 127),
				(byte) MathF.Round(_colors[ y, x ].G * brightness * 127),
				(byte) MathF.Round(_colors[ y, x ].B * brightness * 127),
				255
			];

			_usbSerialPortHelper.Write( data );
		}
	}

	private bool EnterNumpadMode( CarNumberCallback carNumberCallback, bool carNumberIsRequired = true )
	{
		if ( !_inNumpadMode )
		{
			_inNumpadMode = true;

			_carNumber = string.Empty;
			_carNumberCallback = carNumberCallback;
			_carNumberIsRequired = carNumberIsRequired;

			UpdateColors( _blueNoiseLedOrder, false );

			return true;
		}

		return false;
	}

	private bool LeaveNumpadMode( bool invokeCallback )
	{
		if ( _inNumpadMode )
		{
			_inNumpadMode = false;

			UpdateColors( _blueNoiseLedOrder, false );

			if ( invokeCallback )
			{
				_carNumberCallback?.Invoke();
			}

			_blackFlagDriveThrough = false;

			return true;
		}

		return false;
	}

	#region Handle button commands

	private void OnDataReceived( object? sender, string data )
	{
		var app = App.Instance!;

		if ( !app.Simulator.IsConnected )
		{
			return;
		}

		var match = ButtonPressRegex.Match( data );

		if ( match.Success )
		{
			var y = int.Parse( match.Groups[ 1 ].Value );
			var x = int.Parse( match.Groups[ 2 ].Value );

			app.Logger.WriteLine( $"[AdminBoxx] Button press detected: row={y}, col={x}" );

			switch ( y )
			{
				case 0:
				{
					switch ( x )
					{
						case 0: DoYellowFlag(); break;
						case 1: DoNumber( 1 ); break;
						case 2: DoNumber( 2 ); break;
						case 3: DoNumber( 3 ); break;
						case 4: DoBlackFlag(); break;
						case 5: DoClearFlag(); break;
						case 6: DoClearAllFlags(); break;
						case 7: DoChat(); break;
					}

					break;
				}

				case 1:
				{
					switch ( x )
					{
						case 0: DoTogglePaceMode(); break;
						case 1: DoNumber( 4 ); break;
						case 2: DoNumber( 5 ); break;
						case 3: DoNumber( 6 ); break;
						case 4: DoWaveByDriver(); break;
						case 5: DoEndOfLineDriver(); break;
						case 6: DoDisqualifyDriver(); break;
						case 7: DoRemoveDriver(); break;
					}

					break;
				}

				case 2:
				{
					switch ( x )
					{
						case 0: DoPlusOneLap(); break;
						case 1: DoNumber( 7 ); break;
						case 2: DoNumber( 8 ); break;
						case 3: DoNumber( 9 ); break;
						case 4: DoAdvanceToNextSession(); break;
						case 5: DoLive(); break;
						case 6: DoGoToPreviousIncident(); break;
						case 7: DoGoToNextIncident(); break;
					}

					break;
				}

				case 3:
				{
					switch ( x )
					{
						case 0: DoMinusOneLap(); break;
						case 1: DoEscape(); break;
						case 2: DoNumber( 0 ); break;
						case 3: DoEnter(); break;
						case 4: DoSlowMotion(); break;
						case 5: DoReverse(); break;
						case 6: DoForward(); break;
						case 7: DoFastForward(); break;
					}

					break;
				}
			}
		}
		else
		{
			app.Logger.WriteLine( $"[AdminBoxx] Unrecognized message: \"{data}\"" );
		}
	}

	private void DoNumber( int number )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[AdminBoxx] DoNumber( {number} ) >>>" );

		if ( _inNumpadMode )
		{
			_carNumber += $"{number}";

			switch ( number )
			{
				case 0: SetLEDToColor( 3, 2, Red, false ); break;
				case 1: SetLEDToColor( 0, 1, Red, false ); break;
				case 2: SetLEDToColor( 0, 2, Red, false ); break;
				case 3: SetLEDToColor( 0, 3, Red, false ); break;
				case 4: SetLEDToColor( 1, 1, Red, false ); break;
				case 5: SetLEDToColor( 1, 2, Red, false ); break;
				case 6: SetLEDToColor( 1, 3, Red, false ); break;
				case 7: SetLEDToColor( 2, 1, Red, false ); break;
				case 8: SetLEDToColor( 2, 2, Red, false ); break;
				case 9: SetLEDToColor( 2, 3, Red, false ); break;
			}

			app.AudioManager.Play( $"{number}", DataContext.DataContext.Instance.Settings.AdminBoxxVolume );
		}

		app.Logger.WriteLine( $"[AdminBoxx] <<< DoNumber( {number} )" );
	}

	private void DoEscape()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoEscape >>>" );

		if ( _inNumpadMode )
		{
			LeaveNumpadMode( false );

			PlayAudio( "cancel" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoEscape" );
	}

	private void DoEnter()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoEnter >>>" );

		if ( _inNumpadMode )
		{
			if ( !_carNumberIsRequired || ( _carNumber != string.Empty ) )
			{
				LeaveNumpadMode( true );

				if ( _carNumberCallback != ChatCallback )
				{
					PlayAudio( "enter" );
				}
			}
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoEnter" );
	}

	private void DoYellowFlag()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoYellowFlag >>>" );

		if ( !_inNumpadMode )
		{
			if ( ( app.Simulator.SessionFlags & ( IRacingSdkEnum.Flags.Caution | IRacingSdkEnum.Flags.CautionWaving ) ) == 0 )
			{
				app.ChatQueue.SendMessage( "!yellow" );

				PlayAudio( "throw_caution_flag" );
			}
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoYellowFlag" );
	}

	private void DoBlackFlag()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoBlackFlag >>>" );

		if ( !_inNumpadMode )
		{
			EnterNumpadMode( BlackFlagCallback );

			SetLEDToColor( 0, 4, Cyan, false );

			PlayAudio( "black_flag_stop_and_go" );
		}
		else if ( _carNumberCallback == BlackFlagCallback )
		{
			_blackFlagDriveThrough = !_blackFlagDriveThrough;

			if ( _blackFlagDriveThrough )
			{
				SetLEDToColor( 0, 4, Yellow, false );

				PlayAudio( "black_flag_drive_through" );
			}
			else
			{
				SetLEDToColor( 0, 4, Red, false );

				PlayAudio( "black_flag_stop_and_go" );
			}
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoBlackFlag" );
	}

	private void BlackFlagCallback()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] BlackFlagCallback >>>" );

		if ( _blackFlagDriveThrough )
		{
			app.ChatQueue.SendMessage( $"!black #{_carNumber} D" );
		}
		else
		{
			app.ChatQueue.SendMessage( $"!black #{_carNumber}" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< BlackFlagCallback" );
	}

	private void DoClearFlag()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoClearFlag >>>" );

		if ( !_inNumpadMode )
		{
			EnterNumpadMode( ClearFlagCallback );

			SetLEDToColor( 0, 5, Cyan, false );

			PlayAudio( "clear_black_flag" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoClearFlag" );
	}

	private void ClearFlagCallback()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] ClearFlagCallback >>>" );

		app.ChatQueue.SendMessage( $"!clear #{_carNumber}" );

		app.Logger.WriteLine( "[AdminBoxx] <<< ClearFlagCallback" );
	}

	private void DoClearAllFlags()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoClearAllFlags >>>" );

		if ( !_inNumpadMode )
		{
			app.ChatQueue.SendMessage( "!clearall" );

			PlayAudio( "clear_all_black_flags" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoClearAllFlags" );
	}

	private void DoChat()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoChat >>>" );

		if ( !_inNumpadMode )
		{
			EnterNumpadMode( ChatCallback, false );

			PlayAudio( "chat_toggle" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoChat" );
	}

	private void ChatCallback()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] ChatCallback >>>" );

		if ( _carNumber == string.Empty )
		{
			_driverChatDisabled.Clear();

			if ( _globalChatEnabled )
			{
				app.ChatQueue.SendMessage( "!nchat" );

				_globalChatEnabled = false;

				PlayAudio( "chat_disabled" );
			}
			else
			{
				app.ChatQueue.SendMessage( "!chat" );

				PlayAudio( "chat_enabled" );

				_globalChatEnabled = true;
			}
		}
		else if ( _driverChatDisabled.Contains( _carNumber ) )
		{
			app.ChatQueue.SendMessage( $"!chat #{_carNumber}" );

			_driverChatDisabled.Remove( _carNumber );
		}
		else
		{
			app.ChatQueue.SendMessage( $"!nchat #{_carNumber}" );

			_driverChatDisabled.Add( _carNumber );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< ChatCallback" );
	}

	private void DoTogglePaceMode()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoTogglePaceMode >>>" );

		if ( !_inNumpadMode )
		{
			_singleFilePaceMode = !_singleFilePaceMode;

			if ( _singleFilePaceMode )
			{
				app.ChatQueue.SendMessage( "!restart single" );

				PlayAudio( "single_file" );
			}
			else
			{
				app.ChatQueue.SendMessage( "!restart double" );

				PlayAudio( "double_file" );
			}
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoTogglePaceMode" );
	}

	private void DoWaveByDriver()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoWaveByDriver >>>" );

		if ( !_inNumpadMode )
		{
			EnterNumpadMode( WaveByDriverCallback );

			SetLEDToColor( 1, 4, Cyan, false );

			PlayAudio( "wave_by_driver" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoWaveByDriver" );
	}

	private void WaveByDriverCallback()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] WaveByDriverCallback >>>" );

		app.ChatQueue.SendMessage( $"!waveby #{_carNumber}" );

		app.Logger.WriteLine( "[AdminBoxx] <<< WaveByDriverCallback" );
	}

	private void DoEndOfLineDriver()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoEndOfLineDriver >>>" );

		if ( !_inNumpadMode )
		{
			EnterNumpadMode( EndOfLineDriverCallback );

			SetLEDToColor( 1, 5, Cyan, false );

			PlayAudio( "end_of_line_driver" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoEndOfLineDriver" );
	}

	private void EndOfLineDriverCallback()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] EndOfLineDriverCallback >>>" );

		app.ChatQueue.SendMessage( $"!eol #{_carNumber}" );

		app.Logger.WriteLine( "[AdminBoxx] <<< EndOfLineDriverCallback" );
	}

	private void DoDisqualifyDriver()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoDisqualifyDriver >>>" );

		if ( !_inNumpadMode )
		{
			EnterNumpadMode( DisqualifyDriverCallback );

			SetLEDToColor( 1, 6, Cyan, false );

			PlayAudio( "disqualify_driver" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoDisqualifyDriver" );
	}

	private void DisqualifyDriverCallback()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DisqualifyDriverCallback >>>" );

		app.ChatQueue.SendMessage( $"!dq #{_carNumber}" );

		app.Logger.WriteLine( "[AdminBoxx] <<< DisqualifyDriverCallback" );
	}

	private void DoRemoveDriver()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoRemoveDriver >>>" );

		if ( !_inNumpadMode )
		{
			EnterNumpadMode( RemoveDriverCallback );

			SetLEDToColor( 1, 7, Cyan, false );

			PlayAudio( "remove_driver" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoRemoveDriver" );
	}

	private void RemoveDriverCallback()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] RemoveDriverCallback >>>" );

		app.ChatQueue.SendMessage( $"!remove #{_carNumber}" );

		app.Logger.WriteLine( "[AdminBoxx] <<< RemoveDriverCallback" );
	}

	private void DoPlusOneLap()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoPlusOneLap >>>" );

		if ( !_inNumpadMode )
		{
			app.ChatQueue.SendMessage( "!pacelaps +1" );

			PlayAudio( "plus_one_lap" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoPlusOneLap" );
	}

	private void DoAdvanceToNextSession()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoAdvanceToNextSession >>>" );

		if ( !_inNumpadMode )
		{
			app.ChatQueue.SendMessage( "!advance" );

			PlayAudio( "advance_to_next_session" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoAdvanceToNextSession" );
	}

	private void DoLive()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoLive >>>" );

		if ( !_inNumpadMode && _replayEnabled )
		{
			app.Simulator.IRSDK.ReplaySetPlayPosition( IRacingSdkEnum.RpyPosMode.End, 0 );
			app.Simulator.IRSDK.ReplaySetPlaySpeed( 16, false );

			PlayAudio( "live" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoLive" );
	}

	private void DoGoToPreviousIncident()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoGoToPreviousIncident >>>" );

		if ( !_inNumpadMode && _replayEnabled )
		{
			app.Simulator.IRSDK.ReplaySearch( IRacingSdkEnum.RpySrchMode.PrevIncident );

			PlayAudio( "previous_incident" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoGoToPreviousIncident" );
	}

	private void DoGoToNextIncident()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoGoToNextIncident >>>" );

		if ( !_inNumpadMode && _replayEnabled )
		{
			app.Simulator.IRSDK.ReplaySearch( IRacingSdkEnum.RpySrchMode.NextIncident );

			PlayAudio( "next_incident" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoGoToNextIncident" );
	}

	private void DoMinusOneLap()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoMinusOneLap >>>" );

		if ( !_inNumpadMode )
		{
			app.ChatQueue.SendMessage( "!pacelaps -1" );

			PlayAudio( "minus_one_lap" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoMinusOneLap" );
	}

	private void DoSlowMotion()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoSlowMotion >>>" );

		if ( !_inNumpadMode && _replayEnabled )
		{
			var replayPlaySpeed = app.Simulator.ReplayPlaySpeed;

			if ( !app.Simulator.ReplayPlaySlowMotion )
			{
				if ( app.Simulator.ReplayPlaySpeed >= 0 )
				{
					replayPlaySpeed = 1;
				}
				else
				{
					replayPlaySpeed = -1;
				}
			}
			else
			{
				if ( app.Simulator.ReplayPlaySpeed >= 0 )
				{
					replayPlaySpeed++;
				}
				else
				{
					replayPlaySpeed--;
				}
			}

			app.Simulator.IRSDK.ReplaySetPlaySpeed( replayPlaySpeed, true );

			PlayAudio( "slow_motion" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoSlowMotion" );
	}

	private void DoReverse()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoReverse >>>" );

		if ( !_inNumpadMode && _replayEnabled )
		{
			var replayPlaySpeed = app.Simulator.ReplayPlaySpeed;

			if ( app.Simulator.ReplayPlaySlowMotion || ( replayPlaySpeed > 0 ) )
			{
				replayPlaySpeed = -1;
			}
			else
			{
				replayPlaySpeed--;
			}

			app.Simulator.IRSDK.ReplaySetPlaySpeed( replayPlaySpeed, false );

			PlayAudio( "rewind" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoReverse" );
	}

	private void DoForward()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoForward >>>" );

		if ( !_inNumpadMode && _replayEnabled )
		{
			var replayPlaySpeed = app.Simulator.ReplayPlaySpeed;

			if ( replayPlaySpeed != 1 )
			{
				replayPlaySpeed = 1;

				PlayAudio( "play" );
			}
			else
			{
				replayPlaySpeed = 0;

				PlayAudio( "pause" );
			}

			app.Simulator.IRSDK.ReplaySetPlaySpeed( replayPlaySpeed, false );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoForward" );
	}

	private void DoFastForward()
	{
		var app = App.Instance!;

		if ( !_inNumpadMode && _replayEnabled )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoFastForward >>>" );

			var replayPlaySpeed = app.Simulator.ReplayPlaySpeed;

			if ( app.Simulator.ReplayPlaySlowMotion || ( replayPlaySpeed <= 0 ) )
			{
				replayPlaySpeed = 2;
			}
			else
			{
				replayPlaySpeed++;
			}

			app.Simulator.IRSDK.ReplaySetPlaySpeed( replayPlaySpeed, false );

			PlayAudio( "fast_forward" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoFastForward" );
	}

	#endregion

	private static void PlayAudio( string key )
	{
		var app = App.Instance!;

		app.AudioManager.Play( key, DataContext.DataContext.Instance.Settings.AdminBoxxVolume );
	}

	private void OnPortClosed( object? sender, EventArgs e )
	{
		Disconnect();
	}

	private void OnTimer( object? sender, EventArgs e )
	{
		if ( _ledUpdateConcurrentQueue.TryDequeue( out var coord ) )
		{
			using ( _lock.EnterScope() )
			{
				_ledUpdateHashSet.Remove( coord );
			}

			SendLED( coord.y, coord.x );

			_pingCounter = 100;
		}

		if ( IsConnected )
		{
			if ( _pingCounter > 0 )
			{
				if ( Interlocked.Decrement( ref _pingCounter ) == 0 )
				{
					byte[] data = [ 128, 255 ];

					_usbSerialPortHelper.Write( data );

					_pingCounter = 100;
				}
			}
		}
	}

	public void Tick( App app )
	{
		if ( _connectCounter > 0 )
		{
			if ( Interlocked.Decrement( ref _connectCounter ) == 0 )
			{
				_connectCounter = 15;

				switch ( Interlocked.Increment( ref _connectState ) )
				{
					case 1:
					case 3:
					case 5:
						SetLEDToColor( 0, 3, Green, false );
						break;

					case 2:
					case 4:
						SetLEDToColor( 0, 3, Disabled, false );
						break;

					case 6:
						_connectCounter = 0;
						break;
				}
			}
		}
		else if ( _wavingFlagCounter > 0 )
		{
			if ( Interlocked.Decrement( ref _wavingFlagCounter ) == 0 )
			{
				var wavingFlagState = Interlocked.Increment( ref _wavingFlagState );

				if ( ( wavingFlagState & 1 ) == 1 )
				{
					_brightness = 1f;

					if ( wavingFlagState / 2 >= _wavingFlagNumberOfTimes )
					{
						UpdateColors( _wavingFlagLedOrder, true );
					}
					else
					{
						SetAllLEDsToColor( _wavingFlagColor, _wavingFlagLedOrder, true, _wavingFlagCheckered, 0 );

						_wavingFlagCounter = 30;
					}
				}
				else
				{
					_brightness = 0.25f;

					SetAllLEDsToColor( _wavingFlagColor, _wavingFlagLedOrder, true, _wavingFlagCheckered, 1 );

					_wavingFlagCounter = 30;
				}
			}
		}
		else if ( _testCounter > 0 )
		{
			if ( Interlocked.Decrement( ref _testCounter ) == 0 )
			{
				_testCounter = 120;

				switch ( Interlocked.Increment( ref _testState ) )
				{
					case 1:
						WaveFlag( Yellow, 2 );
						break;

					case 2:
						WaveFlag( Green, 2 );
						break;

					case 3:
						WaveFlag( White, 2 );
						break;

					case 4:
						WaveFlag( White, 2, true );
						break;

					case 5:
						var color = new Color( DataContext.DataContext.Instance.Settings.AdminBoxxBlackFlagR, DataContext.DataContext.Instance.Settings.AdminBoxxBlackFlagG, DataContext.DataContext.Instance.Settings.AdminBoxxBlackFlagB );
						WaveFlag( color, 2 );
						break;

					case 6:
						WaveFlag( Blue, 2 );
						break;

					case 7:
						WaveFlag( Red, 2 );
						break;

					case 8:
						SetAllLEDsToColorArray( _playbackDisabledColors, _blueNoiseLedOrder, false );
						break;

					case 9:
						SetAllLEDsToColorArray( _playbackEnabledColors, _blueNoiseLedOrder, false );
						break;

					case 10:
						SetAllLEDsToColorArray( _numpadEnabledColors, _blueNoiseLedOrder, false );
						break;

					case 11:
						_testCounter = 0;
						UpdateColors( _blueNoiseLedOrder, false );
						break;
				}
			}
		}
		else if ( _replayEnabled && !_inNumpadMode )
		{
			SetLEDToColor( 2, 5, ( app.Simulator.ReplayFrameNumEnd == 1 ) ? Cyan : Disabled, false );
			SetLEDToColor( 3, 4, app.Simulator.ReplayPlaySlowMotion ? Cyan : Disabled, false );
			SetLEDToColor( 3, 5, ( app.Simulator.ReplayPlaySpeed < 0 ) ? Cyan : Disabled, false );
			SetLEDToColor( 3, 6, ( app.Simulator.ReplayPlaySpeed == 1 ) || ( app.Simulator.ReplayPlaySlowMotion && ( app.Simulator.ReplayPlaySpeed > 1 ) ) ? Cyan : Disabled, false );
			SetLEDToColor( 3, 7, ( app.Simulator.ReplayPlaySpeed > 1 ) && !app.Simulator.ReplayPlaySlowMotion ? Cyan : Disabled, false );
		}
	}
}
