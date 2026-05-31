
using IRSDKSharper;
using PInvoke;

using static PInvoke.User32;

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
		if ( app.Simulator.WindowHandle is not null )
		{
			if ( key == '\r' )
			{
				var virtualKey = PInvoke.User32.VkKeyScanW( '\r' );

				var scanCode = User32.MapVirtualKey( virtualKey, MapVirtualKeyTranslation.MAPVK_VK_TO_VSC );

				var lParamDown = (IntPtr) ( 1 | ( scanCode << 16 ) | ( 0 << 24 ) | ( 0 << 29 ) | ( 0 << 30 ) | ( 0 << 31 ) );

				User32.PostMessage( (IntPtr) app.Simulator.WindowHandle, User32.WindowMessage.WM_KEYDOWN, (IntPtr) User32.VirtualKey.VK_RETURN, lParamDown );

				var lParamUp = (IntPtr) ( 1 | ( scanCode << 16 ) | ( 0 << 24 ) | ( 0 << 29 ) | ( 1 << 30 ) | ( 1 << 31 ) );

				User32.PostMessage( (IntPtr) app.Simulator.WindowHandle, User32.WindowMessage.WM_KEYUP, (IntPtr) User32.VirtualKey.VK_RETURN, lParamUp );
			}
			else
			{
				User32.PostMessage( (IntPtr) app.Simulator.WindowHandle, User32.WindowMessage.WM_CHAR, (IntPtr) key, IntPtr.Zero );
			}
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
