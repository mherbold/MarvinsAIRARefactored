
using PInvoke;

using IRSDKSharper;

namespace MarvinsAIRARefactored.Components;

public partial class ChatQueue
{
	private readonly List<string> _messageList = [];

	private bool _chatWindowOpened = false;

	public void SendMessage( string message )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[ChatQueue] Sending message: {message}" );

		if ( app.Simulator.IsConnected )
		{
			_messageList.Add( $"{message}\r" );
		}
	}

	public void Tick( App app )
	{
		if ( _messageList.Count > 0 )
		{
			if ( _chatWindowOpened )
			{
				if ( app.Simulator.WindowHandle != null )
				{
					string chatMessage = _messageList[ 0 ];

					foreach ( var ch in chatMessage )
					{
						User32.PostMessage( (IntPtr) app.Simulator.WindowHandle, User32.WindowMessage.WM_CHAR, ch, 0 );
					}
				}

				_messageList.RemoveAt( 0 );

				if ( _messageList.Count > 0 )
				{
					_chatWindowOpened = false;
				}
			}
			else
			{
				app.Simulator.IRSDK.ChatComand( IRacingSdkEnum.ChatCommandMode.BeginChat, 0 );

				_chatWindowOpened = true;
			}
		}
		else
		{
			if ( _chatWindowOpened )
			{
				app.Simulator.IRSDK.ChatComand( IRacingSdkEnum.ChatCommandMode.Cancel, 0 );

				_chatWindowOpened = false;
			}
		}
	}
}
