
using OpenMacroBoard.SDK;
using StreamDeckSharp;

using IDeviceContext = OpenMacroBoard.SDK.IDeviceContext;
using KeyEventArgs = OpenMacroBoard.SDK.KeyEventArgs;

namespace MarvinsAIRARefactored.Components
{
	public sealed class StreamDeck : IDisposable
	{
		public static readonly Guid DeviceGuid = new( "b4881b45-1b73-4b9b-b7b0-3e2f6a2dc5a1" ); // fake GUID

		private IDeviceContext? _deviceContext;
		private IMacroBoard? _board;
		private string _deviceName = "Stream Deck";

		public void Initialize()
		{
			_ = InitializeAsync();
		}

		private async Task InitializeAsync()
		{
			var app = App.Instance!;

			app.Logger.WriteLine( "[StreamDeck] InitializeAsync >>>" );

			try
			{
				_deviceContext = DeviceContext.Create().AddListener<StreamDeckListener>();

				var cancelSrc = new CancellationTokenSource( TimeSpan.FromMilliseconds( 750 ) );

				_board = await _deviceContext.OpenAsync( cancelSrc.Token ).ConfigureAwait( false );

				try
				{
					_deviceName = _board.GetType().Name;
				}
				catch
				{
					_deviceName = "Stream Deck";
				}

				_board.KeyStateChanged += OnKeyStateChanged;

				app.Logger.WriteLine( "[StreamDeck] Connected." );
			}
			catch ( OperationCanceledException )
			{
				app.Logger.WriteLine( "[StreamDeck] No device found (timeout)." );
			}
			catch ( Exception ex )
			{
				app.Logger.WriteLine( $"[StreamDeck] Initialize failed: {ex}" );
			}

			app.Logger.WriteLine( "[StreamDeck] <<< InitializeAsync" );
		}

		private void OnKeyStateChanged( object? sender, KeyEventArgs e )
		{
			var buttonIndex = e.Key;
			var isPressed = e.IsDown;

			App.Instance?.DirectInput.InjectStreamDeckInput( _deviceName, buttonIndex, isPressed );
		}

		public void Dispose()
		{
			try
			{
				if ( _board != null )
				{
					_board.KeyStateChanged -= OnKeyStateChanged;
					_board.Dispose();
					_board = null;
				}

				_deviceContext?.Dispose();
				_deviceContext = null;
			}
			catch
			{
				// best-effort cleanup
			}
		}
	}
}
