
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

	private const int UpdateInterval = 6;

	private readonly Lock _lock = new();

	private readonly List<Message> _messageList = [];

	private bool _chatWindowOpened = false;

	private bool _chatWindowOpening = false;
	private bool _chatWindowClosing = false;

	private int _chatWindowOpeningCounter = 0;
	private int _chatWindowClosingCounter = 0;

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
			}
		}
	}

	private void Update( App app )
	{
		if ( app.Simulator.WindowHandle == null )
		{
			return;
		}

		using ( _lock.EnterScope() )
		{
			if ( _messageList.Count > 0 )
			{
				if ( !_chatWindowOpened )
				{
					if ( !_chatWindowOpening )
					{
						app.Simulator.IRSDK.ChatComand( IRacingSdkEnum.ChatCommandMode.BeginChat, 0 );

						_chatWindowClosing = false;
						_chatWindowOpening = true;
						_chatWindowOpeningCounter = 0;
					}
				}
				else
				{
					var message = _messageList[ 0 ];

					var stringToSend = message.MessageTemplate;

					if ( message.Value != null )
					{
						stringToSend += $" = {message.Value}";
					}

					stringToSend += '\r';

					app.Logger.WriteLine( $"[ChatQueue] Sending message: {stringToSend}" );

					foreach ( var ch in stringToSend )
					{
						SendKey( app, ch );
					}

					_messageList.RemoveAt( 0 );

					if ( _messageList.Count == 0 )
					{
						_chatWindowClosing = true;
						_chatWindowClosingCounter = 0;
					}
				}
			}
		}

		if ( _chatWindowOpening )
		{
			_chatWindowOpeningCounter++;

			if ( _chatWindowOpeningCounter >= 1 )
			{
				_chatWindowOpening = false;
				_chatWindowOpened = true;
			}
		}

		if ( _chatWindowClosing )
		{
			_chatWindowClosingCounter++;

			if ( _chatWindowClosingCounter >= 1 )
			{
				app.Simulator.IRSDK.ChatComand( IRacingSdkEnum.ChatCommandMode.Cancel, 0 );

				_chatWindowClosing = false;
				_chatWindowOpened = false;
			}
		}
	}

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
