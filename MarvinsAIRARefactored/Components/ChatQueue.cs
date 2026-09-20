
using System.Diagnostics;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

using IRSDKSharper;

namespace MarvinsAIRARefactored.Components;

public partial class ChatQueue
{
	private class Message
	{
		public required string MessageTemplate { get; set; }
		public required string? Value { get; set; }
	}

	// Update runs every UpdateInterval worker ticks (6 ticks at 60 Hz = 100 ms), so every tick delay below is in
	// 100 ms steps. iRacing opens/closes its chat box on its own render loop, some frames after it dequeues our
	// posted BeginChat/Cancel command, so each message runs a full open -> settle -> type -> settle -> close ->
	// settle cycle; a rapid stream of messages can never fold two cycles into each other.
	//
	// While the box is open and the sim is the foreground window, the sim has the keyboard too: a message-emitting
	// input mapped to a keyboard key (the arrow keys, say) is processed by the sim's own chat box as well as by
	// MAIRA, and a keystroke landing in the box ahead of our text spoils the "/" prefix - the private message ends
	// up in the default (all teams) channel. MAIRA cannot keep the sim from seeing the key, but it always knows
	// when one fired, because every such input also queues a message here. Two defenses: the box is not opened
	// until the inputs have been quiet for QuietPeriodMS (a knob that is being spammed reports its final value
	// once, after the presses stop), and if an input fires anyway while the box is open, the box is treated as
	// contaminated - it is cancelled untyped and reopened fresh once the inputs are quiet again.
	private const int UpdateInterval = 6;

	private const double QuietPeriodMS = 250.0;  // no new/updated message for this long before the box is opened
	private const int OpenSettleTicks = 2;       // 200 ms after BeginChat before typing into the box
	private const int TypeSettleTicks = 2;       // 200 ms after the Return key before closing the box
	private const int CloseSettleTicks = 2;      // 200 ms after Cancel before the box may be opened again

	private enum ChatState
	{
		Closed,     // nothing in flight; a queued message starts the next cycle once the inputs are quiet
		Opening,    // BeginChat posted, waiting for the box to settle open
		Typed,      // message keystrokes posted, waiting for the sim to take the Return before closing
		Closing     // Cancel posted, waiting for the box to settle closed
	}

	private readonly Lock _lock = new();

	private readonly List<Message> _messageList = [];

	private ChatState _chatState = ChatState.Closed;

	private int _settleTicks = 0;

	private long _lastQueuedTimestamp = 0;      // Stopwatch timestamp of the last SendMessage (the quiet-period clock)
	private long _beginChatTimestamp = 0;       // Stopwatch timestamp of the BeginChat that opened the current box
	private long _lastCommandTimestamp = 0;     // Stopwatch timestamp of the last BeginChat/Cancel/keystrokes, for the log

	private int _updateCounter = UpdateInterval + 0;

	public void SendMessage( string messageTemplate, string? value = null )
	{
		var app = App.Instance!;

		if ( app.Simulator.IsConnected )
		{
			using ( _lock.EnterScope() )
			{
				var messageUpdated = false;

				foreach ( var message in _messageList )
				{
					if ( message.MessageTemplate == messageTemplate )
					{
						message.Value = value;

						messageUpdated = true;
					}
				}

				if ( !messageUpdated )
				{
					_messageList.Add( new Message() { MessageTemplate = messageTemplate, Value = value } );
				}

				_lastQueuedTimestamp = Stopwatch.GetTimestamp();
			}
		}
	}

	private void Update( App app )
	{
		if ( app.Simulator.WindowHandle == null )
		{
			// the sim window is gone (disconnected) - drop whatever is in flight so the next connect starts clean

			_chatState = ChatState.Closed;
			_settleTicks = 0;

			return;
		}

		if ( _settleTicks > 0 )
		{
			_settleTicks--;

			return;
		}

		using ( _lock.EnterScope() )
		{
			switch ( _chatState )
			{
				case ChatState.Closed:

					if ( ( _messageList.Count > 0 ) && ( Stopwatch.GetElapsedTime( _lastQueuedTimestamp ).TotalMilliseconds >= QuietPeriodMS ) )
					{
						app.Simulator.IRSDK.ChatComand( IRacingSdkEnum.ChatCommandMode.BeginChat, 0 );

						_beginChatTimestamp = Stopwatch.GetTimestamp();

						LogCommand( app, "BeginChat" );

						_chatState = ChatState.Opening;
						_settleTicks = OpenSettleTicks;
					}

					break;

				case ChatState.Opening:

					if ( _lastQueuedTimestamp > _beginChatTimestamp )
					{
						// a message-emitting input fired while the box was open, so its keystroke may already be
						// sitting in the box ahead of our text - close the box untyped; the message stays queued and
						// the quiet-period gate reopens a fresh box once the presses have stopped

						app.Simulator.IRSDK.ChatComand( IRacingSdkEnum.ChatCommandMode.Cancel, 0 );

						LogCommand( app, "Cancel (input fired while the box was open - retrying)" );

						_chatState = ChatState.Closing;
						_settleTicks = CloseSettleTicks;
					}
					else if ( _messageList.Count > 0 )
					{
						var message = _messageList[ 0 ];

						var stringToSend = message.MessageTemplate;

						if ( message.Value != null )
						{
							stringToSend += $" = {message.Value}";
						}

						stringToSend += '\r';

						foreach ( var ch in stringToSend )
						{
							SendKey( app, ch );
						}

						LogCommand( app, $"Sending message: {stringToSend}" );

						_messageList.RemoveAt( 0 );

						_chatState = ChatState.Typed;
						_settleTicks = TypeSettleTicks;
					}
					else
					{
						// nothing left to say (can't happen today - messages are only ever removed here - but the
						// box must not be left hanging open)

						app.Simulator.IRSDK.ChatComand( IRacingSdkEnum.ChatCommandMode.Cancel, 0 );

						LogCommand( app, "Cancel (nothing to send)" );

						_chatState = ChatState.Closing;
						_settleTicks = CloseSettleTicks;
					}

					break;

				case ChatState.Typed:

					// the Return key already sent the message and dismissed the box; Cancel is a harmless
					// belt-and-braces close in case the sim left it open, and every message gets its own fresh
					// BeginChat after the close settles

					app.Simulator.IRSDK.ChatComand( IRacingSdkEnum.ChatCommandMode.Cancel, 0 );

					LogCommand( app, "Cancel" );

					_chatState = ChatState.Closing;
					_settleTicks = CloseSettleTicks;

					break;

				case ChatState.Closing:

					_chatState = ChatState.Closed;

					break;
			}
		}
	}

	/// <summary>Logs a chat command with the time since the previous one, so a "went to all teams" report can be
	/// matched against the real gaps between BeginChat, the keystrokes, and Cancel.</summary>
	private void LogCommand( App app, string what )
	{
		var timestamp = Stopwatch.GetTimestamp();

		var elapsedMS = ( _lastCommandTimestamp == 0 ) ? 0.0 : Stopwatch.GetElapsedTime( _lastCommandTimestamp, timestamp ).TotalMilliseconds;

		_lastCommandTimestamp = timestamp;

		app.Logger.WriteLine( $"[ChatQueue] {what} (+{elapsedMS:F0} ms)" );
	}

	/// <summary>
	/// Posts one keystroke to the sim window. Printable characters go as WM_CHAR (what the chat box reads for
	/// text); Return goes as a full key down / up pair, which is what the box acts on to send.
	/// </summary>
	private static void SendKey( App app, char key )
	{
		if ( app.Simulator.WindowHandle is null )
		{
			return;
		}

		var hwnd = (HWND) (nint) app.Simulator.WindowHandle;

		if ( key == '\r' )
		{
			var scanCode = PInvoke.MapVirtualKey( (uint) VIRTUAL_KEY.VK_RETURN, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC );

			var lParamDown = new LPARAM( unchecked((nint) ( 1L | ( (long) scanCode << 16 ) )) );

			_ = PInvoke.PostMessage( hwnd, PInvoke.WM_KEYDOWN, new WPARAM( (nuint) VIRTUAL_KEY.VK_RETURN ), lParamDown );

			var lParamUp = new LPARAM( unchecked((nint) ( 1L | ( (long) scanCode << 16 ) | ( 1L << 30 ) | ( 1L << 31 ) )) );

			_ = PInvoke.PostMessage( hwnd, PInvoke.WM_KEYUP, new WPARAM( (nuint) VIRTUAL_KEY.VK_RETURN ), lParamUp );
		}
		else
		{
			_ = PInvoke.PostMessage( hwnd, PInvoke.WM_CHAR, new WPARAM( key ), new LPARAM( 0 ) );
		}
	}

	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter <= 0 )
		{
			_updateCounter = UpdateInterval;

			Update( app );
		}
	}
}
