
using IRSDKSharper;

using PInvoke;

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
		using ( _lock.EnterScope() )
		{
			if ( _messageList.Count > 0 )
			{
				if ( _chatWindowOpened )
				{
					if ( app.Simulator.WindowHandle != null )
					{
						var message = _messageList[ 0 ];

						var stringToSend = $"{message.MessageTemplate}";

						if ( message.Value != null )
						{
							stringToSend += $" = {message.Value}";
						}

						app.Logger.WriteLine( $"[ChatQueue] Sending message: {stringToSend}" );

						foreach ( var ch in stringToSend )
						{
							User32.PostMessage( (IntPtr) app.Simulator.WindowHandle, User32.WindowMessage.WM_CHAR, ch, 0 );
						}

						User32.PostMessage( (IntPtr) app.Simulator.WindowHandle, User32.WindowMessage.WM_CHAR, '\r', 0 );
					}

					_messageList.RemoveAt( 0 );

					_chatWindowOpened = false;
				}
				else
				{
					app.Simulator.IRSDK.ChatComand( IRacingSdkEnum.ChatCommandMode.BeginChat, 0 );

					_chatWindowOpened = true;
				}
			}
		}
	}

	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter == 0 )
		{
			_updateCounter = UpdateInterval;

			Update( app );
		}
	}
}
