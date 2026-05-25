
using System.IO;
using System.Text;
using System.Threading.Channels;

namespace MarvinsAIRARefactored.Components;

public sealed class Logger
{
	private readonly object _syncRoot = new();
	private readonly Queue<string> _preInitQueue = new();
	private Channel<string>? _channel = null;
	private Task? _writerTask = null;
	private CancellationTokenSource? _cancellationTokenSource = null;
	private FileStream? _fileStream = null;
	private StreamWriter? _streamWriter = null;

	public void Initialize()
	{
		WriteLine( "[Logger] Initialize >>>" );

#if !ADMINBOXX

		var filePath = Path.Combine( App.DocumentsFolder, "MarvinsAIRA.log" );

#else

		var filePath = Path.Combine( App.DocumentsFolder, "AdminBoxx.log" );

#endif

		// Delete old log file if it's older than 240 minutes

		if ( File.Exists( filePath ) )
		{
			var lastWriteTime = File.GetLastWriteTime( filePath );

			if ( lastWriteTime.CompareTo( DateTime.Now.AddMinutes( -240 ) ) < 0 )
			{
				WriteLine( "[Logger] Deleting old log file" );

				try
				{
					File.Delete( filePath );
				}
				catch ( Exception exception )
				{
					WriteLine( $"[Logger] Exception caught: {exception.Message.Trim()}" );
				}
			}
		}

		WriteLine( "[Logger] Opening log file" );

		lock ( _syncRoot )
		{
			// If Initialize gets called twice, don't spin up multiple writers.

			if ( _writerTask != null )
			{
				return;
			}

			// Open stream + writer.

			_fileStream = new FileStream( filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite );

			// BOM-less UTF8 is usually nicer for log files; if you truly want BOM, change to "true".

			_streamWriter = new StreamWriter( _fileStream, new UTF8Encoding( encoderShouldEmitUTF8Identifier: true ) )
			{
				AutoFlush = true
			};

			_cancellationTokenSource = new CancellationTokenSource();

			// Single reader, multiple writers is exactly what we want for logging.

			_channel = Channel.CreateUnbounded<string>( new UnboundedChannelOptions
			{
				SingleReader = true,
				SingleWriter = false,
				AllowSynchronousContinuations = false
			} );

			var cancellationToken = _cancellationTokenSource.Token;

			_writerTask = Task.Run( () => WriterLoop( _channel.Reader, cancellationToken ), cancellationToken );

			// Flush anything that arrived before Initialize().

			while ( _preInitQueue.Count > 0 )
			{
				var queuedMessage = _preInitQueue.Dequeue();

				_channel.Writer.TryWrite( queuedMessage );
			}
		}

		WriteLine( "[Logger] <<< Initialize" );
	}

	public void Shutdown()
	{
		WriteLine( "[Logger] Shutting down" );

		ChannelWriter<string>? channelWriter = null;
		Task? writerTask = null;
		CancellationTokenSource? cancellationTokenSource = null;

		lock ( _syncRoot )
		{
			channelWriter = _channel?.Writer;
			writerTask = _writerTask;
			cancellationTokenSource = _cancellationTokenSource;

			_channel = null;
			_writerTask = null;
			_cancellationTokenSource = null;
		}

		try
		{
			// Stop accepting new messages and let the writer drain.

			channelWriter?.TryComplete();

			// Best-effort drain: wait briefly so we flush queued log lines.
			// Keep this small so "Shutdown" doesn’t feel sticky.

			writerTask?.Wait( millisecondsTimeout: 1500 );
		}
		catch
		{
			// If something goes sideways during shutdown, don't crash the app over logging.
		}
		finally
		{
			try
			{
				cancellationTokenSource?.Cancel();
			}
			catch
			{
			}

			lock ( _syncRoot )
			{
				_streamWriter?.Flush();
				_streamWriter?.Dispose();
				_streamWriter = null;

				_fileStream?.Dispose();
				_fileStream = null;
			}

			try
			{
				cancellationTokenSource?.Dispose();
			}
			catch
			{
			}
		}
	}

	public void WriteLine( string message )
	{
		var messageWithTime = $"{DateTime.Now} {message}";

		System.Diagnostics.Debug.WriteLine( messageWithTime );

		ChannelWriter<string>? channelWriter = null;

		lock ( _syncRoot )
		{
			channelWriter = _channel?.Writer;

			// Not initialized yet? Buffer so we can write it once Initialize() happens.

			if ( channelWriter == null )
			{
				_preInitQueue.Enqueue( messageWithTime );

				return;
			}
		}

		// Never block callers on disk I/O.
		// If the channel is somehow completed, we just drop (at shutdown) rather than hang the app.

		channelWriter.TryWrite( messageWithTime );
	}

	private async Task WriterLoop( ChannelReader<string> channelReader, CancellationToken cancellationToken )
	{
		try
		{
			await foreach ( var line in channelReader.ReadAllAsync( cancellationToken ) )
			{
				StreamWriter? streamWriter;

				lock ( _syncRoot )
				{
					streamWriter = _streamWriter;
				}

				if ( streamWriter == null )
				{
					continue;
				}

				try
				{
					// Single writer, so no locking required around the actual write.

					await streamWriter.WriteLineAsync( line ).ConfigureAwait( false );
				}
				catch ( Exception exception )
				{
					// Last resort: keep *some* visibility if file I/O fails.

					System.Diagnostics.Debug.WriteLine( $"[Logger] WriterLoop exception: {exception.Message.Trim()}" );
				}
			}
		}
		catch ( OperationCanceledException )
		{
			// Normal on shutdown.
		}
		catch ( Exception exception )
		{
			System.Diagnostics.Debug.WriteLine( $"[Logger] WriterLoop exception: {exception.Message.Trim()}" );
		}
	}
}
