
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using Color = MarvinsAIRARefactored.Classes.Color;
using Timer = System.Timers.Timer;

using IRSDKSharper;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public partial class AdminBoxx
{
	private enum Tone
	{
		None,
		AdminBoxx,
		Telemetry,
		Replay
	};

	public static Color Yellow { get; } = new( 1f, 1f, 0f );
	public static Color Green { get; } = new( 0f, 1f, 0f );
	public static Color White { get; } = new( 1f, 1f, 1f );
	public static Color Gray { get; } = new( 0.5f, 0.5f, 0.5f );
	public static Color Blue { get; } = new( 0f, 0f, 1f );
	public static Color Red { get; } = new( 1f, 0f, 0f );
	public static Color Cyan { get; } = new( 0f, 1f, 1f );
	public static Color Magenta { get; } = new( 1f, 0f, 1f );
	public static Color Disabled { get; } = new( 0f, 0f, 0f );

	public bool IsConnected { get; private set; } = false;
	public bool IsUpdating { get; private set; } = false;

	private const int _numColumns = 8;
	private const int _numRows = 4;

	private readonly UsbSerialPortHelper _usbSerialPortHelper = new( string.Empty, "MI_00", "239A", "80F2" );

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
		{ Green, Red, Red, Red, Green,    Disabled, Green,    Green    },
		{ Green, Red, Red, Red, Disabled, Disabled, Disabled, Disabled }
	};

	private static readonly Color[,] _numpadEnabledColors = new Color[ _numRows, _numColumns ]
	{
		{ Red, Cyan,   Cyan, Cyan,  Red, Red, Red, Red },
		{ Red, Cyan,   Cyan, Cyan,  Red, Red, Red, Red },
		{ Red, Cyan,   Cyan, Cyan,  Red, Red, Red, Red },
		{ Red, Yellow, Cyan, Green, Red, Red, Red, Red }
	};

	private bool _inNumpadMode = false;
	private bool _replayEnabled = false;
	private bool _singleFilePaceMode = false;
	private bool _globalChatEnabled = true;
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

	private int _wavingFlagCounter = 0;
	private int _wavingFlagNumberOfTimes = 0;
	private int _wavingFlagState = 0;
	private Color _wavingFlagColor = Disabled;
	private bool _wavingFlagCheckered = false;

	private int _testCounter = 0;
	private int _testState = 0;

	private int _sequenceCounter = 0;
	private int _sequenceState = 0;
	private int _sequenceBeepBlinkRemaining = 0;
	private Tone _sequenceBeepTone = Tone.AdminBoxx;
	private bool _sequenceBlink = false;
	private bool _sequenceBlinkState = false;
	private Color _sequenceBlinkColor = Disabled;
	private int _sequenceBlinkX = 0;
	private int _sequenceBlinkY = 0;
	private string? _sequenceSoundToPlay = null;
	private string? _sequenceDriverNumberToSay = null;
	private int _sequenceDriverNumberToSayIndex = 0;
	private int _sequenceDriverNumberState = 0;

	private int _cautionBlinkCounter = 60;

	private float _brightness = 1f;

	private string _carNumber = string.Empty;

	private delegate void CarNumberCallback();

	private CarNumberCallback? _carNumberCallback = null;

	private int _pingCounter = 0;

	private int _updatePyCurrentLine = 0;
	private string[]? _updatePyLines = null;

	private readonly ConcurrentQueue<(int y, int x)> _ledUpdateConcurrentQueue = new();
	private readonly HashSet<(int y, int x)> _ledUpdateHashSet = [];
	private readonly Lock _lock = new();

	private readonly Timer _timer = new( 10 );

	[GeneratedRegex( @"^V([\d.]+)$", RegexOptions.Compiled )]
	private static partial Regex VersionNumberRegex();
	private static readonly Regex StaticVersionNumberRegex = VersionNumberRegex();

	[GeneratedRegex( @"^:(\d+),(\d+)$", RegexOptions.Compiled )]
	private static partial Regex ButtonPressRegex();
	private static readonly Regex StaticButtonPressRegex = ButtonPressRegex();

	[GeneratedRegex( @"^N$", RegexOptions.Compiled )]
	private static partial Regex NextLineRegex();
	private static readonly Regex StaticNextLineRegex = NextLineRegex();

	[GeneratedRegex( @"^VERSION = ""(?<version>[\d.]+)""", RegexOptions.Compiled | RegexOptions.Multiline )]
	private static partial Regex CodePyVersionRegex();
	private static readonly Regex StaticCodePyVersionRegex = CodePyVersionRegex();

	public AdminBoxx()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] Constructor >>>" );

		_usbSerialPortHelper.DataReceived += OnDataReceived;
		_usbSerialPortHelper.PortClosed += OnPortClosed;

		_timer.Elapsed += OnTimer;

		app.Logger.WriteLine( "[AdminBoxx] <<< Constructor" );
	}

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] Initialize >>>" );

		string[] soundKeys = [
			"adminboxx_tone", "iracing_tone", "replay_tone",
			"0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
			"restart_is_double_file", "restart_is_single_file",
			"caution_extended_by_one_lap", "caution_shortened_by_one_lap",
			"chat_disabled", "chat_enabled",
			"all_penalties_cleared", "session_has_been_advanced", "one_lap_to_green",
			"black_flag_driver_number", "clear_driver_number", "wave_by_driver_number", "end_of_line_driver_number", "disqualify_driver_number", "remove_driver_number", "clear_command",
			"connected_to_adminboxx_app", "connected_to_iracing_simulator", "disconnected_from_iracing_simulator"
		];

		app.AudioManager.LoadSounds( "AdminBoxx", soundKeys );

		_timer.Start();

		_usbSerialPortHelper.Initialize();

		app.Logger.WriteLine( "[AdminBoxx] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] Shutdown >>>" );

		_timer.Stop();

		app.Logger.WriteLine( "[AdminBoxx] <<< Shutdown" );
	}

	public bool Connect()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] Connect >>>" );

		IsConnected = _usbSerialPortHelper.Open();

		if ( IsConnected )
		{
			app.Dispatcher.Invoke( () => MainWindow._adminBoxxPage.Test.Disabled = false );

			_pingCounter = 100;

			UpdateColors( _blueNoiseLedOrder, true );

			if ( app.Simulator.IsConnected )
			{
				RunSequence( 0, Tone.None, false, null, 0, 0, "connected_to_iracing_simulator" );
			}
			else
			{
				RunSequence( 3, Tone.AdminBoxx, true, Green, 3, 3, "connected_to_adminboxx_app" );
			}

			RequestVersionNumber();
		}

		app.Dispatcher.Invoke( () =>
		{
			MainWindow._adminBoxxPage.ConnectToAdminBoxx_MairaSwitch.IsOn = IsConnected;
		} );

		app.Logger.WriteLine( "[AdminBoxx] <<< Connect" );

		return IsConnected;
	}

	public void Disconnect()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] Disconnect >>>" );

		IsConnected = false;

		_usbSerialPortHelper.Close();

		app.Dispatcher.Invoke( () =>
		{
			MainWindow._adminBoxxPage.Test.Disabled = true;
			MainWindow._adminBoxxPage.ConnectToAdminBoxx_MairaSwitch.IsOn = false;
		} );

		app.Logger.WriteLine( "[AdminBoxx] <<< Disconnect" );
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

		if ( IsConnected )
		{
			RunSequence( 0, Tone.None, false, null, 0, 0, "connected_to_iracing_simulator" );
		}
	}

	public void SimulatorDisconnected()
	{
		UpdateColors( _blueNoiseLedOrder, true );

		_inNumpadMode = false;

		_singleFilePaceMode = false;
		_globalChatEnabled = true;

		if ( IsConnected )
		{
			RunSequence( 3, Tone.None, true, Green, 3, 3, "disconnected_from_iracing_simulator" );
		}
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

		if ( !IsConnected )
		{
			return;
		}

		// yellow flag / caution flag

		if ( ( app.Simulator.SessionFlags & ( IRacingSdkEnum.Flags.YellowWaving | IRacingSdkEnum.Flags.CautionWaving ) ) != 0 )
		{
			if ( !_shownYellowFlag )
			{
				_shownYellowFlag = true;

				WaveFlag( Yellow, 3 );

				if ( ( app.Simulator.SessionFlags & IRacingSdkEnum.Flags.CautionWaving ) != 0 )
				{
					RunSequence( 3, Tone.Telemetry );
				}
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

				// RunSequence( 1, Tone.Telemetry, false, null, 0, 0, "one_lap_to_green" );
				RunSequence( 1, Tone.Telemetry );
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

				RunSequence( 1, Tone.Telemetry );
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

				RunSequence( 1, Tone.Telemetry );
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

				WaveFlag( Green, 3 );

				RunSequence( 3, Tone.Telemetry );
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

				RunSequence( 3, Tone.Telemetry );
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

				RunSequence( 5, Tone.Telemetry );
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

				WaveFlag( Gray, 3 );

				RunSequence( 3, Tone.Telemetry );
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

				RunSequence( 1, Tone.Telemetry );
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

				RunSequence( 3, Tone.Telemetry );
			}
		}
		else
		{
			_shownRedFlag = false;
		}
	}

	private void WaveFlag( Color color, int numberOfTimes, bool checkered = false )
	{
		_brightness = 1f;

		_wavingFlagState = 0;
		_wavingFlagColor = color;
		_wavingFlagCheckered = checkered;
		_wavingFlagNumberOfTimes = numberOfTimes;
		_wavingFlagCounter = 1;

		_sequenceState = 0;
		_sequenceCounter = 0;
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

			SetLEDToColor( 3, 3, Green, forceUpdate );
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

	private void RequestVersionNumber()
	{
		if ( IsConnected )
		{
			var app = App.Instance!;

			app.Logger.WriteLine( $"[AdminBoxx] Requesting version number" );

			byte[] data = [ 130, 255 ];

			_usbSerialPortHelper.Write( data );
		}
	}

	private void StartCodePyUpdate( string codePy )
	{
		if ( IsConnected )
		{
			var app = App.Instance!;

			app.Logger.WriteLine( $"[AdminBoxx] Starting code.py update" );

			byte[] data = [ 131, 255 ];

			_usbSerialPortHelper.Write( data );

			_updatePyCurrentLine = 0;
			_updatePyLines = codePy.Split( [ "\r\n", "\r", "\n" ], StringSplitOptions.None );

			IsUpdating = true;

			app.MainWindow.UpdateStatus();
		}
	}

	private void SendNextCodePyLine()
	{
		if ( IsConnected )
		{
			if ( _updatePyLines != null )
			{
				if ( _updatePyCurrentLine < _updatePyLines.Length )
				{
					var data = new byte[] { 132 }.Concat( Encoding.ASCII.GetBytes( _updatePyLines[ _updatePyCurrentLine ] ) ).Concat( new byte[] { 255 } ).ToArray();

					_usbSerialPortHelper.Write( data );

					_updatePyCurrentLine++;
				}
				else
				{
					var app = App.Instance!;

					app.Logger.WriteLine( $"[AdminBoxx] Finishing code.py update" );

					byte[] data = [ 133, 255 ];

					_usbSerialPortHelper.Write( data );

					Disconnect();

					IsUpdating = false;

					app.MainWindow.UpdateStatus();
				}
			}
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

			return true;
		}

		return false;
	}

	private void OnDataReceived( object? sender, string data )
	{
		var app = App.Instance!;

		var match = StaticButtonPressRegex.Match( data );

		if ( match.Success )
		{
			if ( app.Simulator.IsConnected )
			{
				var y = int.Parse( match.Groups[ 1 ].Value );
				var x = int.Parse( match.Groups[ 2 ].Value );

				HandleButtonPress( y, x );
			}

			return;
		}

		match = StaticVersionNumberRegex.Match( data );

		if ( match.Success )
		{
			HandleVersionNumber( match.Groups[ 1 ].Value );

			return;
		}

		match = StaticNextLineRegex.Match( data );

		if ( match.Success )
		{
			SendNextCodePyLine();

			return;
		}

		app.Logger.WriteLine( $"[AdminBoxx] Unrecognized message: \"{data}\"" );
	}

	#region Handle button commands

	private void HandleButtonPress( int y, int x )
	{
		var app = App.Instance!;

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

	private void DoNumber( int number )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[AdminBoxx] DoNumber( {number} ) >>>" );

		if ( _inNumpadMode )
		{
			_carNumber += $"{number}";

			switch ( number )
			{
				case 0: SetLEDToColor( 3, 2, Magenta, false ); break;
				case 1: SetLEDToColor( 0, 1, Magenta, false ); break;
				case 2: SetLEDToColor( 0, 2, Magenta, false ); break;
				case 3: SetLEDToColor( 0, 3, Magenta, false ); break;
				case 4: SetLEDToColor( 1, 1, Magenta, false ); break;
				case 5: SetLEDToColor( 1, 2, Magenta, false ); break;
				case 6: SetLEDToColor( 1, 3, Magenta, false ); break;
				case 7: SetLEDToColor( 2, 1, Magenta, false ); break;
				case 8: SetLEDToColor( 2, 2, Magenta, false ); break;
				case 9: SetLEDToColor( 2, 3, Magenta, false ); break;
			}

			RunSequence( 0, Tone.AdminBoxx, false, null, 0, 0, $"{number}" );
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

			RunSequence( 1, Tone.AdminBoxx, false, null, 0, 0, "clear_command" );
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

				RunSequence( 3, Tone.AdminBoxx, true, Yellow, 0, 0 );
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

			RunSequence( 1, Tone.AdminBoxx, false, null, 0, 0, "black_flag_driver_number" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoBlackFlag" );
	}

	private void BlackFlagCallback()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] BlackFlagCallback >>>" );

		app.ChatQueue.SendMessage( $"!black #{_carNumber}" );

		RunSequence( 1 );

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

			RunSequence( 1, Tone.AdminBoxx, false, null, 0, 0, "clear_driver_number" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoClearFlag" );
	}

	private void ClearFlagCallback()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] ClearFlagCallback >>>" );

		app.ChatQueue.SendMessage( $"!clear #{_carNumber}" );

		RunSequence( 1 );

		app.Logger.WriteLine( "[AdminBoxx] <<< ClearFlagCallback" );
	}

	private void DoClearAllFlags()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoClearAllFlags >>>" );

		if ( !_inNumpadMode )
		{
			app.ChatQueue.SendMessage( "!clearall" );

			RunSequence( 1, Tone.AdminBoxx, true, White, 6, 0, "all_penalties_cleared" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoClearAllFlags" );
	}

	private void DoChat()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoChat >>>" );

		if ( !_inNumpadMode )
		{
			if ( _globalChatEnabled )
			{
				app.ChatQueue.SendMessage( "!nchat" );

				_globalChatEnabled = false;

				RunSequence( 1, Tone.AdminBoxx, true, White, 7, 0, "chat_disabled" );
			}
			else
			{
				app.ChatQueue.SendMessage( "!chat" );

				_globalChatEnabled = true;

				RunSequence( 1, Tone.AdminBoxx, true, White, 7, 0, "chat_enabled" );
			}
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoChat" );
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

				RunSequence( 1, Tone.AdminBoxx, true, White, 0, 1, "restart_is_single_file" );
			}
			else
			{
				app.ChatQueue.SendMessage( "!restart double" );

				RunSequence( 2, Tone.AdminBoxx, true, White, 0, 1, "restart_is_double_file" );
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

			RunSequence( 1, Tone.AdminBoxx, false, null, 0, 0, "wave_by_driver_number" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoWaveByDriver" );
	}

	private void WaveByDriverCallback()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] WaveByDriverCallback >>>" );

		app.ChatQueue.SendMessage( $"!waveby #{_carNumber}" );

		RunSequence( 1 );

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

			RunSequence( 1, Tone.AdminBoxx, false, null, 0, 0, "end_of_line_driver_number" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoEndOfLineDriver" );
	}

	private void EndOfLineDriverCallback()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] EndOfLineDriverCallback >>>" );

		app.ChatQueue.SendMessage( $"!eol #{_carNumber}" );

		RunSequence( 1 );

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

			RunSequence( 1, Tone.AdminBoxx, false, null, 0, 0, "disqualify_driver_number" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoDisqualifyDriver" );
	}

	private void DisqualifyDriverCallback()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DisqualifyDriverCallback >>>" );

		app.ChatQueue.SendMessage( $"!dq #{_carNumber}" );

		RunSequence( 1 );

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

			RunSequence( 1, Tone.AdminBoxx, false, null, 0, 0, "remove_driver_number" );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoRemoveDriver" );
	}

	private void RemoveDriverCallback()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] RemoveDriverCallback >>>" );

		app.ChatQueue.SendMessage( $"!remove #{_carNumber}" );

		RunSequence( 1 );

		app.Logger.WriteLine( "[AdminBoxx] <<< RemoveDriverCallback" );
	}

	private void DoPlusOneLap()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] DoPlusOneLap >>>" );

		if ( !_inNumpadMode )
		{
			app.ChatQueue.SendMessage( "!pacelaps +1" );

			RunSequence( 1, Tone.AdminBoxx, true, White, 0, 2, "caution_extended_by_one_lap" );
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

			RunSequence( 1, Tone.AdminBoxx, true, White, 4, 2, "session_has_been_advanced" );
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

			app.AudioManager.Play( "replay_tone", DataContext.DataContext.Instance.Settings.AdminBoxxVolume );
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

			app.AudioManager.Play( "replay_tone", DataContext.DataContext.Instance.Settings.AdminBoxxVolume );
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

			app.AudioManager.Play( "replay_tone", DataContext.DataContext.Instance.Settings.AdminBoxxVolume );
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

			RunSequence( 1, Tone.AdminBoxx, true, White, 0, 3, "caution_shortened_by_one_lap" );
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

			app.AudioManager.Play( "replay_tone", DataContext.DataContext.Instance.Settings.AdminBoxxVolume );
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

			app.AudioManager.Play( "replay_tone", DataContext.DataContext.Instance.Settings.AdminBoxxVolume );
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
			}
			else
			{
				replayPlaySpeed = 0;
			}

			app.Simulator.IRSDK.ReplaySetPlaySpeed( replayPlaySpeed, false );

			app.AudioManager.Play( "replay_tone", DataContext.DataContext.Instance.Settings.AdminBoxxVolume );
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

			app.AudioManager.Play( "replay_tone", DataContext.DataContext.Instance.Settings.AdminBoxxVolume );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< DoFastForward" );
	}

	#endregion

	private void HandleVersionNumber( string version )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AdminBoxx] HandleVersionNumber >>>" );

		app.Logger.WriteLine( $"[AdminBoxx] Version on AdminBoxx is {version}" );

		var assembly = Assembly.GetExecutingAssembly();

		var resourceName = assembly.GetManifestResourceNames().FirstOrDefault( name => name.EndsWith( "code.py" ) ) ?? throw new Exception( "Could not find code.py embedded resource" );

		using var resourceStream = assembly.GetManifestResourceStream( resourceName ) ?? throw new Exception( "Could not open code.py embedded resource as a resource stream" );
		using var reader = new StreamReader( resourceStream ) ?? throw new Exception( "Could not create stream reader to read in code.py embedded resource" );

		var codePy = reader.ReadToEnd();

		var match = StaticCodePyVersionRegex.Match( codePy );

		if ( !match.Success )
		{
			throw new Exception( "Could not extract version number from code.py embedded resource" );
		}

		var currentVersion = match.Groups[ "version" ].Value;

		app.Logger.WriteLine( $"[AdminBoxx] Current version is {currentVersion}" );

		if ( version != currentVersion )
		{
			app.Logger.WriteLine( $"[AdminBoxx] Versions do not match - updating AdminBoxx" );

			StartCodePyUpdate( codePy );
		}

		app.Logger.WriteLine( "[AdminBoxx] <<< HandleVersionNumber" );
	}

	private void RunSequence( int beepBlinkCount, Tone beepTone = Tone.AdminBoxx, bool blink = false, Color? blinkColor = null, int blinkX = 0, int blinkY = 0, string? key = null, string? driverNumberToSay = null )
	{
		_sequenceState = 0;
		_sequenceCounter = 1;

		_sequenceBeepBlinkRemaining = beepBlinkCount;

		_sequenceBeepTone = beepTone;

		_sequenceBlink = blink;
		_sequenceBlinkState = false;
		_sequenceBlinkColor = blinkColor ?? Disabled;
		_sequenceBlinkX = blinkX;
		_sequenceBlinkY = blinkY;

		_sequenceSoundToPlay = key;

		_sequenceDriverNumberToSay = driverNumberToSay;
		_sequenceDriverNumberToSayIndex = 0;
		_sequenceDriverNumberState = 0;
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
		if ( _wavingFlagCounter > 0 )
		{
			if ( Interlocked.Decrement( ref _wavingFlagCounter ) == 0 )
			{
				var wavingFlagState = Interlocked.Increment( ref _wavingFlagState );

				if ( ( wavingFlagState & 1 ) == 1 )
				{
					_brightness = 1f;

					if ( wavingFlagState / 2 >= _wavingFlagNumberOfTimes )
					{
						if ( _testCounter == 0 )
						{
							UpdateColors( _wavingFlagLedOrder, true );
						}
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

		if ( _testCounter > 0 )
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
						WaveFlag( Gray, 2 );
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
		else if ( _wavingFlagCounter == 0 )
		{
			if ( !_inNumpadMode && app.Simulator.IsConnected && ( ( _sequenceCounter == 0 ) || ( _sequenceState >= 3 ) ) )
			{
				if ( ( app.Simulator.SessionFlags & ( IRacingSdkEnum.Flags.Yellow | IRacingSdkEnum.Flags.YellowWaving | IRacingSdkEnum.Flags.Caution | IRacingSdkEnum.Flags.CautionWaving ) ) != 0 )
				{
					_cautionBlinkCounter--;

					SetLEDToColor( 0, 0, _cautionBlinkCounter >= 30 ? Yellow : Disabled, false );

					if ( _cautionBlinkCounter == 0 )
					{
						_cautionBlinkCounter = 60;
					}
				}

				if ( _globalChatEnabled )
				{
					SetLEDToColor( 0, 7, Green, false );
				}
				else
				{
					SetLEDToColor( 0, 7, Disabled, false );
				}

				if ( _singleFilePaceMode )
				{
					SetLEDToColor( 1, 0, Yellow, false );
				}
				else
				{
					SetLEDToColor( 1, 0, Green, false );
				}

				if ( _replayEnabled )
				{
					SetLEDToColor( 2, 5, ( app.Simulator.ReplayFrameNumEnd == 1 ) ? Magenta : Disabled, false );
					SetLEDToColor( 3, 4, app.Simulator.ReplayPlaySlowMotion ? Magenta : Disabled, false );
					SetLEDToColor( 3, 5, ( app.Simulator.ReplayPlaySpeed < 0 ) ? Magenta : Disabled, false );
					SetLEDToColor( 3, 6, ( app.Simulator.ReplayPlaySpeed == 1 ) || ( app.Simulator.ReplayPlaySlowMotion && ( app.Simulator.ReplayPlaySpeed > 1 ) ) ? Magenta : Disabled, false );
					SetLEDToColor( 3, 7, ( app.Simulator.ReplayPlaySpeed > 1 ) && !app.Simulator.ReplayPlaySlowMotion ? Magenta : Disabled, false );
				}
			}
		}

		if ( _sequenceCounter > 0 )
		{
			_sequenceCounter--;

			if ( _sequenceCounter == 0 )
			{
				switch ( _sequenceState )
				{
					case 0: // turn off all leds (if we want to blink)

						if ( _sequenceBlink )
						{
							SetAllLEDsToColor( Disabled, _blueNoiseLedOrder, false );
						}

						_sequenceState++;

						if ( _sequenceBeepBlinkRemaining == 0 )
						{
							_sequenceState++;
						}

						_sequenceCounter = 1;

						break;

					case 1: // beep / blink

						_sequenceBlinkState = !_sequenceBlinkState;

						if ( _sequenceBlinkState )
						{
							var key = _sequenceBeepTone switch
							{
								Tone.AdminBoxx => "adminboxx_tone",
								Tone.Telemetry => "iracing_tone",
								Tone.Replay => "replay_tone",
								_ => string.Empty
							};

							app.AudioManager.Play( key, DataContext.DataContext.Instance.Settings.AdminBoxxVolume );

							_sequenceBeepBlinkRemaining--;

							if ( _sequenceBlink )
							{
								SetLEDToColor( _sequenceBlinkY, _sequenceBlinkX, _sequenceBlinkColor, false );
							}
						}
						else
						{
							if ( _sequenceBlink )
							{
								SetLEDToColor( _sequenceBlinkY, _sequenceBlinkX, Disabled, false );
							}

							if ( _sequenceBeepBlinkRemaining == 0 )
							{
								_sequenceState++;
							}
						}

						_sequenceCounter = 30;

						break;

					case 2: // restore led colors

						if ( _sequenceBlink )
						{
							UpdateColors( _blueNoiseLedOrder, false );
						}

						_sequenceState++;
						_sequenceCounter = 1;

						break;

					case 3: // say phrase

						if ( _sequenceSoundToPlay != null )
						{
							app.AudioManager.Play( _sequenceSoundToPlay, DataContext.DataContext.Instance.Settings.AdminBoxxVolume );
						}

						_sequenceState++;
						_sequenceCounter = 1;

						break;

					case 4: // wait for phrase to finish

						if ( ( _sequenceSoundToPlay == null ) || !app.AudioManager.IsPlaying( _sequenceSoundToPlay ) )
						{
							_sequenceState++;
						}

						_sequenceCounter = 1;

						break;

					case 5: // say driver number

						if ( _sequenceDriverNumberToSay != null )
						{
							var numberToSay = _sequenceDriverNumberToSay[ (int) _sequenceDriverNumberToSayIndex ];

							if ( _sequenceDriverNumberState == 0 )
							{
								app.AudioManager.Play( $"{numberToSay}", DataContext.DataContext.Instance.Settings.AdminBoxxVolume );

								_sequenceDriverNumberState = 1;
							}
							else
							{
								if ( !app.AudioManager.IsPlaying( $"{numberToSay}" ) )
								{
									_sequenceDriverNumberToSayIndex++;

									if ( _sequenceDriverNumberToSayIndex == _sequenceDriverNumberToSay.Length )
									{
										_sequenceDriverNumberToSay = null;
									}
									else
									{
										_sequenceDriverNumberState = 0;
									}
								}
							}

							_sequenceCounter = 1;
						}

						break;
				}
			}
		}
	}
}
