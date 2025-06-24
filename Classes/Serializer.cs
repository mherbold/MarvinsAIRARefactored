
using System.IO;
using System.Xml.Serialization;

namespace MarvinsAIRARefactored.Classes;

public static class Serializer
{
	public static object? Load( string filePath, Type type )
	{
		object? data = null;

		var app = App.Instance!;

		app.Logger.WriteLine( $"[Serializer] Load >>> ({filePath})" );

		try
		{
			var xmlSerializer = new XmlSerializer( type );

			using var fileStream = new FileStream( filePath, FileMode.Open );

			data = xmlSerializer.Deserialize( fileStream );

			fileStream.Close();
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[Serializer] Exception caught: {exception.Message.Trim()}" );
		}

		app.Logger.WriteLine( $"[Serializer] <<< Load" );

		return data;
	}

	public static void Save( string filePath, object data )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[Serializer] Save >>> ({filePath})" );

		var directoryName = Path.GetDirectoryName( filePath );

		if ( directoryName != null )
		{
			Directory.CreateDirectory( directoryName );
		}

		var xmlSerializer = new XmlSerializer( data.GetType() );

		using var streamWriter = new StreamWriter( filePath );

		xmlSerializer.Serialize( streamWriter, data );

		streamWriter.Close();

		app.Logger.WriteLine( $"[Serializer] <<< Save" );
	}
}
