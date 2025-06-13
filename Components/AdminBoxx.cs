
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media;

using Brushes = System.Windows.Media.Brushes;
using Color = MarvinsAIRARefactored.Classes.Color;
using Label = System.Windows.Controls.Label;
using Timer = System.Timers.Timer;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Components;

public partial class AdminBoxx
{
	public static Color Yellow { get; } = new( 1f, 1f, 0f );
	public static Color Green { get; } = new( 0f, 1f, 0f );
	public static Color White { get; } = new( 1f, 1f, 1f );
	public static Color Black { get; } = new( 0f, 0f, 0f );
	public static Color Blue { get; } = new( 0f, 0f, 1f );
	public static Color Red { get; } = new( 1f, 0f, 0f );
	public static Color Cyan { get; } = new( 0f, 1f, 1f );
	public static Color Magenta { get; } = new( 1f, 0f, 1f );

	public bool IsConnected { get; private set; } = false;

	private const int _numColumns = 8;
	private const int _numRows = 4;

	private readonly UsbSerialPortHelper _usbSerialPortHelper = new( "239A", "80F2" );

	private readonly Label[,] _labels = new Label[ _numRows, _numColumns ];
	private readonly Color[,] _colors = new Color[ _numRows, _numColumns ];

	public static readonly (int x, int y)[] _blueNoiseLedOrder =
	[
		(3, 2), (6, 0), (0, 3), (4, 1), (7, 2), (2, 0), (1, 3), (5, 2),
		(6, 3), (3, 0), (0, 0), (4, 2), (7, 0), (1, 0), (5, 3), (2, 2),
		(6, 1), (3, 3), (0, 2), (4, 0), (7, 3), (2, 1), (1, 2), (5, 0),
		(6, 2), (3, 1), (0, 1), (4, 3), (7, 1), (1, 1), (5, 1), (2, 3)
	];

	public static readonly (int x, int y)[] _wavingFlagLedOrder =
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
		{ Green, Black, Black, Black, Cyan,  Cyan,  Green, Green },
		{ Green, Black, Black, Black, Cyan,  Cyan,  Cyan,  Cyan  },
		{ Green, Black, Black, Black, Green, Black, Black, Black },
		{ Green, Black, Black, Black, Black, Black, Black, Black }
	};

	private static readonly Color[,] _playbackEnabledColors = new Color[ _numRows, _numColumns ]
	{
		{ Green, Black, Black, Black, Cyan,    Cyan,    Green,   Green   },
		{ Green, Black, Black, Black, Cyan,    Cyan,    Cyan,    Cyan    },
		{ Green, Black, Black, Black, Green,   Magenta, Magenta, Magenta },
		{ Green, Black, Black, Black, Magenta, Magenta, Magenta, Magenta }
	};

	private static readonly Color[,] _numpadEnabledColors = new Color[ _numRows, _numColumns ]
	{
		{ Black, Cyan, Cyan, Cyan,  Black, Black, Black, Black },
		{ Black, Cyan, Cyan, Cyan,  Black, Black, Black, Black },
		{ Black, Cyan, Cyan, Cyan,  Black, Black, Black, Black },
		{ Black, Red,  Cyan, Green, Black, Black, Black, Black }
	};

	private static readonly Regex ButtonPressRegex = MyRegex();

	private bool _globalChatEnabled = true;
	private HashSet<string> _driverChatDisabled = [];

	private bool _ledColorsDirty = false;
	private bool _inNumpadMode = false;
	private bool _blackFlagDriveThrough = false;
	private bool _carNumberIsRequired = false;

	private bool _shownYellowFlag = false;
	private bool _shownGreenFlag = false;
	private bool _shownWhiteFlag = false;
	private bool _shownCheckeredFlag = false;
	private bool _shownBlackFlag = false;
	private bool _shownBlueFlag = false;
	private bool _shownRedFlag = false;

	private int _wavingFlagCounter = 0;
	private int _wavingFlagState = 0;
	private Color _wavingFlagColor = Black;
	private bool _wavingFlagCheckered = false;

	private float _brightness = 1f;

	private string _carNumber = string.Empty;

	private delegate void CarNumberCallback();

	private CarNumberCallback? _carNumberCallback = null;

	private int _clearCounter = 0;
	private bool _set = false;
	private int _setY = 0;
	private int _setX = 0;

	private readonly ConcurrentQueue<(int y, int x)> _ledUpdateConcurrentQueue = new();
	private readonly HashSet<(int y, int x)> _ledUpdateHashSet = [];
	private readonly Lock _lock = new();

	private readonly Timer _timer = new( 10 );

	[GeneratedRegex( @"^:(\d+),(\d+)$", RegexOptions.Compiled )]
	private static partial Regex MyRegex();

	public AdminBoxx()
	{
		_usbSerialPortHelper.DataReceived += OnDataReceived;

		_timer.Elapsed += OnTimer;
	}

	public void Initialize()
	{
		var app = App.Instance;

		if ( app != null )
		{
			foreach ( Label label in app.MainWindow.AdminBoxx_LED_Grid.Children.OfType<Label>() )
			{
				var row = Grid.GetRow( label );
				var col = Grid.GetColumn( label );

				_labels[ row, col ] = label;
				_colors[ row, col ] = new( 0, 0, 0 );
			}
		}

		_timer.Start();
	}

	public void Shutdown()
	{
		_timer.Stop();
	}

	public bool Connect()
	{
		var app = App.Instance;

		if ( app != null )
		{
			IsConnected = _usbSerialPortHelper.Open();

			if ( IsConnected )
			{
				SetAllLEDsToColor( White, _blueNoiseLedOrder, true );

				UpdateColors( _blueNoiseLedOrder, true );
			}
		}

		return IsConnected;
	}

	public void Disconnect()
	{
		IsConnected = false;

		_usbSerialPortHelper.Close();
	}

	public void ResendAllLEDs( (int x, int y)[]? pattern = null )
	{
		if ( pattern == null )
		{
			pattern = _blueNoiseLedOrder;
		}

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
				SetLEDToColor( y, x, Black, forceUpdate );
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

	public void ReplayPlayingChanged()
	{
		var app = App.Instance;

		if ( app != null )
		{
			if ( !_inNumpadMode )
			{

			}
		}
	}

	public void SessionFlagsChanged()
	{
		var app = App.Instance;

		if ( app != null )
		{
			if ( (int) ( app.Simulator.SessionFlags & ( IRSDKSharper.IRacingSdkEnum.Flags.Yellow | IRSDKSharper.IRacingSdkEnum.Flags.YellowWaving | IRSDKSharper.IRacingSdkEnum.Flags.Caution | IRSDKSharper.IRacingSdkEnum.Flags.CautionWaving ) ) != 0 )
			{
				if ( !_shownYellowFlag )
				{
					_shownYellowFlag = true;

					WaveFlag( Yellow );
				}
			}
			else
			{
				_shownYellowFlag = false;
			}

			if ( (int) ( app.Simulator.SessionFlags & ( IRSDKSharper.IRacingSdkEnum.Flags.Green | IRSDKSharper.IRacingSdkEnum.Flags.StartGo ) ) != 0 )
			{
				if ( !_shownGreenFlag )
				{
					_shownGreenFlag = true;

					WaveFlag( Green );
				}
			}
			else
			{
				_shownGreenFlag = false;
			}

			if ( (int) ( app.Simulator.SessionFlags & IRSDKSharper.IRacingSdkEnum.Flags.White ) != 0 )
			{
				if ( !_shownWhiteFlag )
				{
					_shownWhiteFlag = true;

					WaveFlag( White );
				}
			}
			else
			{
				_shownWhiteFlag = false;
			}

			if ( (int) ( app.Simulator.SessionFlags & IRSDKSharper.IRacingSdkEnum.Flags.Checkered ) != 0 )
			{
				if ( !_shownCheckeredFlag )
				{
					_shownCheckeredFlag = true;

					WaveFlag( White, true );
				}
			}
			else
			{
				_shownCheckeredFlag = false;
			}

			if ( (int) ( app.Simulator.SessionFlags & IRSDKSharper.IRacingSdkEnum.Flags.Black ) != 0 )
			{
				if ( !_shownBlackFlag )
				{
					_shownBlackFlag = true;

					WaveFlag( Magenta );
				}
			}
			else
			{
				_shownBlackFlag = false;
			}

			if ( (int) ( app.Simulator.SessionFlags & IRSDKSharper.IRacingSdkEnum.Flags.Blue ) != 0 )
			{
				if ( !_shownBlueFlag )
				{
					_shownBlueFlag = true;

					WaveFlag( Blue );
				}
			}
			else
			{
				_shownBlueFlag = false;
			}

			if ( (int) ( app.Simulator.SessionFlags & IRSDKSharper.IRacingSdkEnum.Flags.Red ) != 0 )
			{
				if ( !_shownRedFlag )
				{
					_shownRedFlag = true;

					WaveFlag( Red );
				}
			}
			else
			{
				_shownRedFlag = false;
			}
		}
	}

	public void WaveFlag( Color color, bool checkered = false )
	{
		_wavingFlagCounter = 30;
		_wavingFlagState = 0;
		_wavingFlagColor = color;
		_wavingFlagCheckered = checkered;

		SetAllLEDsToColor( color, _wavingFlagLedOrder, true, _wavingFlagCheckered, 0 );
	}

	private void SetLEDToColor( int y, int x, Color color, bool forceUpdate )
	{
		if ( forceUpdate || ( _colors[ y, x ] != color ) )
		{
			_colors[ y, x ] = color;

			_ledColorsDirty = true;

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
		var app = App.Instance;

		if ( app != null )
		{
			if ( !app.Simulator.IsConnected )
			{
				SetAllLEDsToColor( Red, pattern, forceUpdate );
			}
			else
			{
				if ( _inNumpadMode )
				{
					SetAllLEDsToColorArray( _numpadEnabledColors, pattern, forceUpdate );
				}
				else
				{
					if ( app.Simulator.IsReplayPlaying )
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
	}

	private void SendLED( int y, int x )
	{
		if ( IsConnected )
		{
			var brightness = _brightness * DataContext.DataContext.Instance.Settings.AdminBoxxBrightness;

			byte[] data =
			[
				128,
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
		var app = App.Instance;

		if ( app != null )
		{
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

				_setY = y;
				_setX = x;

				_set = true;

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
	}

	private void DoNumber( int number )
	{
		var app = App.Instance;

		if ( app != null )
		{
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

				app.AudioManager.Play( $"{number}" );
			}

			app.Logger.WriteLine( $"[AdminBoxx] <<< DoNumber( {number} )" );
		}
	}

	private void DoEscape()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoEscape >>>" );

			if ( _inNumpadMode )
			{
				LeaveNumpadMode( false );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoEscape" );
		}
	}

	private void DoEnter()
	{
		var app = App.Instance;

		if ( app != null )
		{
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
	}

	private void DoYellowFlag()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoYellowFlag >>>" );

			if ( !_inNumpadMode )
			{
				app.ChatQueue.SendMessage( "!yellow" );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoYellowFlag" );
		}
	}

	private void DoBlackFlag()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoBlackFlag >>>" );

			if ( !_inNumpadMode )
			{
				EnterNumpadMode( BlackFlagCallback );

				SetLEDToColor( 0, 4, Red, false );
			}
			else if ( _carNumberCallback == BlackFlagCallback )
			{
				_blackFlagDriveThrough = !_blackFlagDriveThrough;

				if ( _blackFlagDriveThrough )
				{
					SetLEDToColor( 0, 4, Yellow, false );
				}
				else
				{
					SetLEDToColor( 0, 4, Red, false );
				}
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoBlackFlag" );
		}
	}

	private void BlackFlagCallback()
	{
		var app = App.Instance;

		if ( app != null )
		{
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
	}

	private void DoClearFlag()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoClearFlag >>>" );

			if ( !_inNumpadMode )
			{
				EnterNumpadMode( ClearFlagCallback );

				SetLEDToColor( 0, 5, Cyan, false );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoClearFlag" );
		}
	}

	private void ClearFlagCallback()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] ClearFlagCallback >>>" );

			app.ChatQueue.SendMessage( $"!clear #{_carNumber}" );

			app.Logger.WriteLine( "[AdminBoxx] <<< ClearFlagCallback" );
		}
	}

	private void DoClearAllFlags()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoClearAllFlags >>>" );

			if ( !_inNumpadMode )
			{
				app.ChatQueue.SendMessage( "!clearall" );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoClearAllFlags" );
		}
	}

	private void DoChat()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoChat >>>" );

			if ( !_inNumpadMode )
			{
				EnterNumpadMode( ChatCallback, false );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoChat" );
		}
	}

	private void ChatCallback()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] ChatCallback >>>" );

			if ( _carNumber == string.Empty )
			{
				_driverChatDisabled.Clear();

				if ( _globalChatEnabled )
				{
					app.ChatQueue.SendMessage( "!nchat" );

					_globalChatEnabled = false;
				}
				else
				{
					app.ChatQueue.SendMessage( "!chat" );

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
	}

	private void DoTogglePaceMode()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoTogglePaceMode >>>" );

			if ( !_inNumpadMode )
			{
				switch ( app.Simulator.PaceMode )
				{
					case IRSDKSharper.IRacingSdkEnum.PaceMode.DoubleFileStart:
					case IRSDKSharper.IRacingSdkEnum.PaceMode.DoubleFileRestart:
						app.ChatQueue.SendMessage( "!restart single" );
						break;

					default:
						app.ChatQueue.SendMessage( "!restart double" );
						break;
				}
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoTogglePaceMode" );
		}
	}

	private void DoWaveByDriver()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoWaveByDriver >>>" );

			if ( !_inNumpadMode )
			{
				EnterNumpadMode( WaveByDriverCallback );

				SetLEDToColor( 1, 4, Cyan, false );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoWaveByDriver" );
		}
	}

	private void WaveByDriverCallback()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] WaveByDriverCallback >>>" );

			app.ChatQueue.SendMessage( $"!waveby #{_carNumber}" );

			app.Logger.WriteLine( "[AdminBoxx] <<< WaveByDriverCallback" );
		}
	}

	private void DoEndOfLineDriver()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoEndOfLineDriver >>>" );

			if ( !_inNumpadMode )
			{
				EnterNumpadMode( EndOfLineDriverCallback );

				SetLEDToColor( 1, 5, Cyan, false );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoEndOfLineDriver" );
		}
	}

	private void EndOfLineDriverCallback()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] EndOfLineDriverCallback >>>" );

			app.ChatQueue.SendMessage( $"!eol #{_carNumber}" );

			app.Logger.WriteLine( "[AdminBoxx] <<< EndOfLineDriverCallback" );
		}
	}

	private void DoDisqualifyDriver()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoDisqualifyDriver >>>" );

			if ( !_inNumpadMode )
			{
				EnterNumpadMode( DisqualifyDriverCallback );

				SetLEDToColor( 1, 6, Cyan, false );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoDisqualifyDriver" );
		}
	}

	private void DisqualifyDriverCallback()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DisqualifyDriverCallback >>>" );

			app.ChatQueue.SendMessage( $"!dq #{_carNumber}" );

			app.Logger.WriteLine( "[AdminBoxx] <<< DisqualifyDriverCallback" );
		}
	}

	private void DoRemoveDriver()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoRemoveDriver >>>" );

			if ( !_inNumpadMode )
			{
				EnterNumpadMode( RemoveDriverCallback );

				SetLEDToColor( 1, 7, Cyan, false );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoRemoveDriver" );
		}
	}

	private void RemoveDriverCallback()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] RemoveDriverCallback >>>" );

			app.ChatQueue.SendMessage( $"!remove #{_carNumber}" );

			app.Logger.WriteLine( "[AdminBoxx] <<< RemoveDriverCallback" );
		}
	}

	private void DoPlusOneLap()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoPlusOneLap >>>" );

			if ( !_inNumpadMode )
			{
				app.ChatQueue.SendMessage( "!pacelaps +1" );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoPlusOneLap" );
		}
	}

	private void DoAdvanceToNextSession()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoAdvanceToNextSession >>>" );

			if ( !_inNumpadMode )
			{
				app.ChatQueue.SendMessage( "!advance" );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoAdvanceToNextSession" );
		}
	}

	private void DoLive()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoLive >>>" );

			if ( !_inNumpadMode )
			{
				app.Simulator.IRSDK.ReplaySetPlayPosition( IRSDKSharper.IRacingSdkEnum.RpyPosMode.End, 0 );
				app.Simulator.IRSDK.ReplaySetPlaySpeed( 16, false );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoLive" );
		}
	}

	private void DoGoToPreviousIncident()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoGoToPreviousIncident >>>" );

			if ( !_inNumpadMode )
			{
				app.Simulator.IRSDK.ReplaySearch( IRSDKSharper.IRacingSdkEnum.RpySrchMode.PrevIncident );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoGoToPreviousIncident" );
		}
	}

	private void DoGoToNextIncident()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoGoToNextIncident >>>" );

			if ( !_inNumpadMode )
			{
				app.Simulator.IRSDK.ReplaySearch( IRSDKSharper.IRacingSdkEnum.RpySrchMode.NextIncident );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoGoToNextIncident" );
		}
	}

	private void DoMinusOneLap()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoMinusOneLap >>>" );

			if ( !_inNumpadMode )
			{
				app.ChatQueue.SendMessage( "!pacelaps -1" );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoMinusOneLap" );
		}
	}

	private void DoSlowMotion()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoSlowMotion >>>" );

			if ( !_inNumpadMode )
			{
				var replayPlaySpeed = app.Simulator.ReplayPlaySpeed;

				if ( !app.Simulator.ReplayPlaySlowMotion )
				{
					if ( app.Simulator.ReplayPlaySpeed > 0 )
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
					if ( app.Simulator.ReplayPlaySpeed > 0 )
					{
						replayPlaySpeed++;
					}
					else
					{
						replayPlaySpeed--;
					}
				}

				app.Simulator.IRSDK.ReplaySetPlaySpeed( replayPlaySpeed, true );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoSlowMotion" );
		}
	}

	private void DoReverse()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoReverse >>>" );

			if ( !_inNumpadMode )
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
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoReverse" );
		}
	}

	private void DoForward()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[AdminBoxx] DoForward >>>" );

			if ( !_inNumpadMode )
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
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoForward" );
		}
	}

	private void DoFastForward()
	{
		var app = App.Instance;

		if ( app != null )
		{
			if ( !_inNumpadMode )
			{
				app.Logger.WriteLine( "[AdminBoxx] DoFastForward >>>" );

				var replayPlaySpeed = app.Simulator.ReplayPlaySpeed;

				if ( app.Simulator.ReplayPlaySlowMotion || ( replayPlaySpeed < 0 ) )
				{
					replayPlaySpeed = 1;
				}
				else
				{
					replayPlaySpeed++;
				}

				app.Simulator.IRSDK.ReplaySetPlaySpeed( replayPlaySpeed, false );
			}

			app.Logger.WriteLine( "[AdminBoxx] <<< DoFastForward" );
		}
	}

	#endregion

	private void OnTimer( object? sender, EventArgs e )
	{
		if ( _ledUpdateConcurrentQueue.TryDequeue( out var coord ) )
		{
			using ( _lock.EnterScope() )
			{
				_ledUpdateHashSet.Remove( coord );
			}

			SendLED( coord.y, coord.x );
		}
	}

	public void Tick( App app )
	{
		if ( _wavingFlagCounter > 0 )
		{
			if ( Interlocked.Decrement( ref _wavingFlagCounter ) == 0 )
			{
				switch ( Interlocked.Increment( ref _wavingFlagState ) )
				{
					case 0:
					case 2:
						_brightness = 1f;
						_wavingFlagCounter = 30;
						SetAllLEDsToColor( _wavingFlagColor, _wavingFlagLedOrder, true, _wavingFlagCheckered, 0 );
						break;

					case 1:
					case 3:
						_brightness = 0.25f;
						_wavingFlagCounter = 30;
						SetAllLEDsToColor( _wavingFlagColor, _wavingFlagLedOrder, true, _wavingFlagCheckered, 1 );
						break;

					case 4:
						_brightness = 1f;
						UpdateColors( _wavingFlagLedOrder, true );
						break;
				}
			}
		}

		if ( _clearCounter > 0 )
		{
			_clearCounter--;

			if ( _clearCounter == 0 )
			{
				app.Dispatcher.BeginInvoke( () =>
				{
					for ( var y = 0; y < _numRows; y++ )
					{
						for ( var x = 0; x < _numColumns; x++ )
						{
							_labels[ y, x ].BorderBrush = Brushes.White;
						}
					}
				} );
			}
		}

		if ( _set )
		{
			_set = false;

			app.Dispatcher.BeginInvoke( () =>
			{
				_labels[ _setY, _setX ].BorderBrush = Brushes.Red;
			} );

			_clearCounter = 20;
		}

		if ( _ledColorsDirty )
		{
			_ledColorsDirty = false;

			app.Dispatcher.BeginInvoke( () =>
			{
				for ( var y = 0; y < _numRows; y++ )
				{
					for ( var x = 0; x < _numColumns; x++ )
					{
						_labels[ y, x ].Background = new SolidColorBrush( System.Windows.Media.Color.FromScRgb( 1f, _colors[ y, x ].R * 0.75f, _colors[ y, x ].G * 0.75f, _colors[ y, x ].B * 0.75f ) );
					}
				}
			} );
		}
	}
}
