
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MarvinsAIRARefactored.Classes;

public sealed class ChromeSTTBridge : IDisposable
{
	public static string STTDirectory { get; private set; } = Path.Combine( App.DocumentsFolder, "STT" );

	private readonly HttpListener _httpListner = new();
	private CancellationTokenSource _cancellationTokenSource = new();
	private Task? _serverLoopTask;

	private readonly Lock _lock = new();
	private WebSocket? _webSocket;

	private volatile bool _clientConnected;
	private TaskCompletionSource<bool> _connectedTaskCompletionSource = new( TaskCreationOptions.RunContinuationsAsynchronously );

	public int Port { get; }
	public string HostURL => $"http://localhost:{Port}/";
	public bool IsClientConnected => _clientConnected;

	public Task SendStartAsync() => SendCommandAsync( new { type = "start" } );
	public Task SendStopAsync() => SendCommandAsync( new { type = "stop" } );
	public Task SendSetLanguageAsync( string language ) => SendCommandAsync( new { type = "setlanguage", language } );
	public Task SendSetStringsAsync( IDictionary<string, string> strings ) => SendCommandAsync( new { type = "setstrings", strings } );
	public Task<bool> WaitUntilConnectedAsync( TimeSpan timeout ) => _connectedTaskCompletionSource.Task.WaitAsync( timeout );
	public Task<bool> WaitUntilConnectedAsync() => _connectedTaskCompletionSource.Task;

	public event Action<string>? PartialText;
	public event Action<string>? FinalText;
	public event Action<string>? ErrorText;
	public event Action? SessionEnded;

	public ChromeSTTBridge( int port = 18888 )
	{
		Port = port;

		_httpListner.Prefixes.Add( HostURL );
	}

	public void Dispose() => _ = StopAsync();

	public void Start()
	{
		if ( _cancellationTokenSource.IsCancellationRequested )
		{
			Volatile.Write( ref _cancellationTokenSource, new CancellationTokenSource() );
		}

		_connectedTaskCompletionSource = new( TaskCreationOptions.RunContinuationsAsynchronously );
		_clientConnected = false;

		_httpListner.Start();

		_serverLoopTask = Task.Run( () => ServerLoop( _cancellationTokenSource.Token ) );

		UpdateStrings();
	}

	public async Task StopAsync()
	{
		await SendCommandAsync( new { type = "shutdown" } );

		_cancellationTokenSource.Cancel();

		try
		{
			_httpListner.Stop();
		}
		catch
		{
		}

		try
		{
			_httpListner.Close();
		}
		catch
		{
		}

		if ( _serverLoopTask != null )
		{
			try
			{
				await _serverLoopTask.ConfigureAwait( false );
			}
			catch
			{
			}
		}
	}

	public void UpdateStrings()
	{
		var localization = DataContext.DataContext.Instance.Localization;

		var strings = new Dictionary<string, string>
		{
			[ "title" ] = localization[ "STT_Title" ],
			[ "hint" ] = localization[ "STT_Hint" ],
			[ "button" ] = localization[ "STT_Button" ],
		};

		_ = SendSetStringsAsync( strings );
	}

	private async Task ServerLoop( CancellationToken cancellationToken )
	{
		while ( !cancellationToken.IsCancellationRequested )
		{
			HttpListenerContext httpListenerContext;

			try
			{
				httpListenerContext = await _httpListner.GetContextAsync().ConfigureAwait( false );
			}
			catch when ( cancellationToken.IsCancellationRequested )
			{
				break;
			}
			catch
			{
				continue;
			}

			_ = Task.Run( () => HandleContextAsync( httpListenerContext, cancellationToken ), cancellationToken );
		}
	}

	private async Task HandleContextAsync( HttpListenerContext httpListenerContext, CancellationToken cancellationToken )
	{
		try
		{
			var path = httpListenerContext.Request.Url?.AbsolutePath.TrimStart( '/' ) ?? string.Empty;

			if ( httpListenerContext.Request.IsWebSocketRequest && path.StartsWith( "ws", StringComparison.OrdinalIgnoreCase ) )
			{
				var httpListenerWebSocketContext = await httpListenerContext.AcceptWebSocketAsync( null ).ConfigureAwait( false );

				lock ( _lock ) _webSocket = httpListenerWebSocketContext.WebSocket;

				_clientConnected = true;
				_connectedTaskCompletionSource.TrySetResult( true );

				await ReceiveLoopAsync( httpListenerWebSocketContext.WebSocket, cancellationToken ).ConfigureAwait( false );

				return;
			}

			await ServeStaticAsync( httpListenerContext, path, cancellationToken ).ConfigureAwait( false );
		}
		catch ( Exception ex )
		{
			TryWriteErrorResponse( httpListenerContext, 500, ex.Message );
		}
	}

	private async Task ReceiveLoopAsync( WebSocket websocket, CancellationToken cancellationToken )
	{
		var buffer = new byte[ 64 * 1024 ];

		try
		{
			while ( ( websocket.State == WebSocketState.Open ) && !cancellationToken.IsCancellationRequested )
			{
				var result = await websocket.ReceiveAsync( buffer, cancellationToken ).ConfigureAwait( false );

				if ( result.MessageType == WebSocketMessageType.Close ) break;

				var json = Encoding.UTF8.GetString( buffer, 0, result.Count );

				ProcessInboundMessage( json );
			}
		}
		catch ( OperationCanceledException )
		{
		}
		catch ( Exception ex )
		{
			ErrorText?.Invoke( "WebSocket receive error: " + ex.Message );
		}
		finally
		{
			try
			{
				await websocket.CloseAsync( WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None ).ConfigureAwait( false );
			}
			catch
			{
			}

			lock ( _lock ) _webSocket = null;

			_clientConnected = false;

			if ( !_cancellationTokenSource.IsCancellationRequested )
			{
				_connectedTaskCompletionSource = new( TaskCreationOptions.RunContinuationsAsynchronously );
			}
		}
	}

	private void ProcessInboundMessage( string json )
	{
		try
		{
			using var doc = JsonDocument.Parse( json );

			if ( !doc.RootElement.TryGetProperty( "type", out var tEl ) )
			{
				return;
			}

			var type = tEl.GetString();

			switch ( type )
			{
				case "stt":
				{
					var partial = doc.RootElement.TryGetProperty( "partial", out var i ) ? i.GetString() ?? string.Empty : string.Empty;
					var final = doc.RootElement.TryGetProperty( "final", out var f ) ? f.GetString() ?? string.Empty : string.Empty;

					if ( !string.IsNullOrWhiteSpace( partial ) )
					{
						PartialText?.Invoke( partial );
					}

					if ( !string.IsNullOrWhiteSpace( final ) )
					{
						FinalText?.Invoke( final );
					}

					break;
				}

				case "end":
				{
					SessionEnded?.Invoke();
					break;
				}

				case "error":
				{
					var message = doc.RootElement.TryGetProperty( "message", out var m ) ? m.GetString() ?? "unknown error" : "unknown error";

					ErrorText?.Invoke( message );

					break;
				}
			}
		}
		catch
		{
			ErrorText?.Invoke( "Invalid JSON from page." );
		}
	}

	private async Task SendCommandAsync( object message )
	{
		WebSocket? webSocket;

		lock ( _lock ) webSocket = _webSocket;

		if ( ( webSocket == null ) || ( webSocket.State != WebSocketState.Open ) )
		{
			return;
		}

		var json = JsonSerializer.Serialize( message );
		var bytes = Encoding.UTF8.GetBytes( json );

		try
		{
			await webSocket.SendAsync( bytes, WebSocketMessageType.Text, true, CancellationToken.None ).ConfigureAwait( false );
		}
		catch ( Exception ex )
		{
			ErrorText?.Invoke( "WebSocket send error: " + ex.Message );
		}
	}

	private static async Task ServeStaticAsync( HttpListenerContext httpListenerContext, string rawPath, CancellationToken cancellationToken )
	{
		var path = string.IsNullOrWhiteSpace( rawPath ) ? "index.html" : rawPath;

		if ( PathEndsWithSlash( path ) )
		{
			path = Path.Combine( path, "index.html" );
		}

		var fullPath = SafeCombine( STTDirectory, path );

		if ( ( fullPath is null ) || !fullPath.StartsWith( Path.GetFullPath( STTDirectory ), StringComparison.OrdinalIgnoreCase ) )
		{
			TryWriteErrorResponse( httpListenerContext, 403, "Forbidden" );

			return;
		}

		if ( !File.Exists( fullPath ) )
		{
			var fallback = Path.Combine( STTDirectory, "index.html" );

			if ( !File.Exists( fallback ) )
			{
				TryWriteErrorResponse( httpListenerContext, 404, $"File not found: {path}" );
				return;
			}

			fullPath = fallback;
		}

		byte[] bytes;

		try
		{
			bytes = await File.ReadAllBytesAsync( fullPath, cancellationToken ).ConfigureAwait( false );
		}
		catch ( Exception ex )
		{
			TryWriteErrorResponse( httpListenerContext, 500, ex.Message );

			return;
		}

		var mime = GetMimeType( fullPath );

		httpListenerContext.Response.ContentType = mime;
		httpListenerContext.Response.ContentLength64 = bytes.LongLength;

		try
		{
			await httpListenerContext.Response.OutputStream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
		}
		finally
		{
			try
			{
				httpListenerContext.Response.OutputStream.Close();
			}
			catch
			{
			}
		}
	}

	private static bool PathEndsWithSlash( string path ) => path.EndsWith( '/' ) || path.EndsWith( '\\' );

	private static string? SafeCombine( string baseDirectory, string relativePath )
	{
		try
		{
			return Path.GetFullPath( Path.Combine( baseDirectory, relativePath.Replace( '/', Path.DirectorySeparatorChar ) ) );
		}
		catch
		{
			return null;
		}
	}

	private static string GetMimeType( string filePath )
	{
		var extension = Path.GetExtension( filePath ).ToLowerInvariant();

		return extension switch
		{
			".html" => "text/html; charset=utf-8",
			".htm" => "text/html; charset=utf-8",
			".js" => "application/javascript; charset=utf-8",
			".mjs" => "application/javascript; charset=utf-8",
			".css" => "text/css; charset=utf-8",
			".json" => "application/json; charset=utf-8",
			".png" => "image/png",
			".jpg" => "image/jpeg",
			".jpeg" => "image/jpeg",
			".gif" => "image/gif",
			".svg" => "image/svg+xml",
			".ico" => "image/x-icon",
			_ => "application/octet-stream"
		};
	}

	private static void TryWriteErrorResponse( HttpListenerContext httpListenerContext, int status, string message )
	{
		try
		{
			var body = Encoding.UTF8.GetBytes( $"<pre>{WebUtility.HtmlEncode( message )}</pre>" );

			httpListenerContext.Response.StatusCode = status;
			httpListenerContext.Response.StatusDescription = message;
			httpListenerContext.Response.ContentType = "text/html; charset=utf-8";
			httpListenerContext.Response.ContentLength64 = body.LongLength;
			httpListenerContext.Response.OutputStream.Write( body, 0, body.Length );
			httpListenerContext.Response.OutputStream.Close();
		}
		catch
		{
			/* ignore */
		}
	}
}
