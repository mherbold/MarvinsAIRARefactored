
using System.IO;
using System.Text;

namespace MarvinsAIRARefactored.Components;

public class Logger
{
	private readonly ReaderWriterLock _readerWriterLock = new();

	private FileStream? _fileStream = null;

	private readonly StringBuilder _stringBuilder = new( Environment.NewLine, 4 * 1024 );

	public void Initialize()
	{
		WriteLine( "[Logger] Initialize >>>" );

		var filePath = Path.Combine( App.DocumentsFolder, "MarvinsAIRA.log" );

		if ( File.Exists( filePath ) )
		{
			var lastWriteTime = File.GetLastWriteTime( filePath );

			if ( lastWriteTime.CompareTo( DateTime.Now.AddMinutes( -15 ) ) < 0 )
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

		_fileStream = new FileStream( filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite );

		WriteLine( "[Logger] <<< Initialize" );
	}

	public void Shutdown()
	{
		WriteLine( "[Logger] Shutdown >>>" );

		_fileStream?.Close();
		_fileStream?.Dispose();

		_fileStream = null;

		WriteLine( "[Logger] <<< Shutdown" );
	}

	public void WriteLine( string message )
	{
		var messageWithTime = $"{DateTime.Now} {message}";

		System.Diagnostics.Debug.WriteLine( messageWithTime );

		_stringBuilder.AppendLine( messageWithTime );

		if ( _fileStream != null )
		{
			try
			{
				_readerWriterLock.AcquireWriterLock( 250 );

				try
				{
					var builtString = _stringBuilder.ToString();

					var bytes = new UTF8Encoding( true ).GetBytes( builtString );

					_fileStream.Write( bytes, 0, bytes.Length );
					_fileStream.Flush();

					_stringBuilder.Clear();
				}
				finally
				{
					_readerWriterLock.ReleaseWriterLock();
				}
			}
			catch ( ApplicationException )
			{
			}
		}
	}
}
