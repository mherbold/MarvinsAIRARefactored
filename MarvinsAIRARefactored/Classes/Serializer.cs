
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using System.Xml.Serialization;

using MarvinsAIRARefactored.DataContext;

namespace MarvinsAIRARefactored.Classes;

public static class Serializer
{
	public static object? GetDefaultValue( string propertyPath )
	{
		var settings = new Settings();

		var propertyName = propertyPath.Split( '.' ).Last();

		var propInfo = settings.GetType().GetProperty( propertyName ) ?? throw new Exception( $"[Settings] Could not determine default value of property '{propertyName}'" );

		return propInfo.GetValue( settings );
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

	public static T Load<T>( string filePath ) where T : notnull, new()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[Serializer] Load >>> {filePath}" );

		var instance = new T();

		var doc = XDocument.Load( filePath, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo );

		if ( doc.Root is not null )
		{
			ApplyOverlay( doc.Root, instance, parentPath: typeof( T ).Name );
		}

		app.Logger.WriteLine( $"[Serializer] <<< Load" );

		return instance;
	}

	private static void ApplyOverlay( XElement element, object target, string parentPath )
	{
		var targetType = target.GetType();

		if ( IsGenericDictionary( targetType, out var keyType, out var valueType ) )
		{
			ApplyDictionaryOverlay( element, (System.Collections.IDictionary) target, keyType!, valueType!, parentPath );

			return;
		}

		if ( IsGenericList( targetType, out var elemType ) )
		{
			ApplyListOverlay( element, target, elemType!, parentPath );

			return;
		}

		var props = targetType.GetProperties( BindingFlags.Public | BindingFlags.Instance );

		foreach ( var prop in props )
		{
			if ( !prop.CanWrite || prop.GetCustomAttribute<XmlIgnoreAttribute>() != null ) continue;

			var propElement = element.Element( prop.Name );

			if ( propElement is null ) continue;

			var propPath = $"{parentPath}.{prop.Name}";
			var pType = prop.PropertyType;

			if ( IsSimple( pType ) )
			{
				if ( TryParseSimple( propElement, pType, out var parsed ) )
				{
					prop.SetValue( target, parsed );
				}
				else
				{
					var fallback = GetDefaultValue( propPath );

					prop.SetValue( target, fallback );
				}
			}
			else
			{
				var current = prop.GetValue( target );

				current ??= Activator.CreateInstance( pType ) ?? throw new Exception( $"Could not create instance of '{pType.Name}'" );

				if ( IsGenericDictionary( pType, out var kT, out var vT ) )
				{
					ApplyDictionaryOverlay( propElement, (System.Collections.IDictionary) current, kT!, vT!, propPath );
				}
				else if ( IsGenericList( pType, out var eT ) )
				{
					ApplyListOverlay( propElement, current, eT!, propPath );
				}
				else
				{
					ApplyOverlay( propElement, current, propPath );
				}

				prop.SetValue( target, current );
			}
		}
	}

	private static void ApplyDictionaryOverlay( XElement dictElement, System.Collections.IDictionary dict, Type keyType, Type valueType, string parentPath )
	{
		foreach ( var itemEl in dictElement.Elements( "item" ) )
		{
			var keyWrapper = itemEl.Element( "key" );
			var valueWrapper = itemEl.Element( "value" );

			if ( keyWrapper is null || valueWrapper is null ) continue;

			var keyPayload = UnwrapPayload( keyWrapper );
			var valuePayload = UnwrapPayload( valueWrapper );

			object? keyObj;

			if ( IsSimple( keyType ) )
			{
				if ( !TryParseSimple( keyPayload, keyType, out keyObj ) ) continue;
			}
			else
			{
				if ( !TryDeserializeViaXmlSerializer( keyPayload, keyType, out keyObj ) ) continue;
			}

			var entryPath = $"{parentPath}[{KeyToString( keyObj )}]";

			var valueObj = dict.Contains( keyObj! ) ? dict[ keyObj! ] : CreateDefault( valueType );

			if ( valueObj is null ) continue;

			if ( IsSimple( valueType ) )
			{
				if ( TryParseSimple( valuePayload, valueType, out var parsedVal ) )
				{
					valueObj = parsedVal;
				}
				else
				{
					valueObj = GetDefaultValue( entryPath );
				}

				dict[ keyObj! ] = valueObj!;
			}
			else
			{
				ApplyOverlay( valuePayload, valueObj, entryPath );

				dict[ keyObj! ] = valueObj;
			}
		}
	}

	private static XElement UnwrapPayload( XElement wrapper )
	{
		var child = wrapper.Elements().FirstOrDefault();

		return child ?? wrapper;
	}

	private static bool TryDeserializeViaXmlSerializer( XElement payload, Type type, out object? obj )
	{
		obj = null;

		try
		{
			var root = new XmlRootAttribute( payload.Name.LocalName );

			if ( !string.IsNullOrEmpty( payload.Name.NamespaceName ) )
			{
				root.Namespace = payload.Name.NamespaceName;
			}

			var ser = new XmlSerializer( type, root );

			using var r = payload.CreateReader();

			r.MoveToContent();

			obj = ser.Deserialize( r );

			return obj is not null;
		}
		catch
		{
			return false;
		}
	}

	private static string KeyToString( object? key ) => key switch
	{
		null => "(null)",
		Guid g => g.ToString( "D" ),
		_ => key.ToString() ?? "(key)"
	};

	private static void ApplyListOverlay( XElement listElement, object listObj, Type elemType, string parentPath )
	{
		var addMethod = listObj.GetType().GetMethod( "Add" );

		if ( addMethod is null ) return;

		listObj.GetType().GetMethod( "Clear" )?.Invoke( listObj, null );

		foreach ( var itemEl in listElement.Elements() )
		{
			if ( IsSimple( elemType ) )
			{
				if ( TryParseSimple( itemEl, elemType, out var parsed ) )
				{
					addMethod.Invoke( listObj, [ parsed ] );
				}
				else
				{
					var fallback = GetDefaultValue( $"{parentPath}[]" );

					addMethod.Invoke( listObj, [ fallback ] );
				}
			}
			else
			{
				var item = CreateDefault( elemType );

				if ( item is null ) continue;

				ApplyOverlay( itemEl, item, $"{parentPath}[]" );

				addMethod.Invoke( listObj, [ item ] );
			}
		}
	}

	private static bool TryParseSimple( XElement el, Type targetType, out object? value )
	{
		value = null;

		if ( targetType.IsEnum )
		{
			var s = (string?) el;

			if ( s != null && Enum.TryParse( targetType, s, true, out var e ) )
			{
				value = e;

				return true;
			}

			return false;
		}

		var converter = TypeDescriptor.GetConverter( targetType );

		if ( converter.CanConvertFrom( typeof( string ) ) )
		{
			try
			{
				value = converter.ConvertFrom( null, CultureInfo.InvariantCulture, (string) el );

				return true;
			}
			catch
			{
				return false;
			}
		}

		if ( targetType == typeof( Guid ) )
		{
			var s = (string?) el;

			if ( Guid.TryParse( s, out var g ) ) { value = g; return true; }

			return false;
		}

		if ( targetType == typeof( TimeSpan ) )
		{
			var s = (string?) el;

			if ( TimeSpan.TryParse( s, CultureInfo.InvariantCulture, out var ts ) ) { value = ts; return true; }

			return false;
		}

		return false;
	}

	private static bool IsSimple( Type t )
	{
		if ( t.IsPrimitive ) return true;
		if ( t.IsEnum ) return true;

		if ( t == typeof( string ) || t == typeof( decimal ) || t == typeof( DateTime ) || t == typeof( Guid ) || t == typeof( TimeSpan ) )
		{
			return true;
		}

		var underlying = Nullable.GetUnderlyingType( t );

		return underlying != null && IsSimple( underlying );
	}

	private static object? CreateDefault( Type t ) => Activator.CreateInstance( t );

	private static bool IsGenericDictionary( Type t, out Type? keyType, out Type? valueType )
	{
		keyType = null;
		valueType = null;

		if ( t.IsGenericType && t.GetGenericTypeDefinition() == typeof( IDictionary<,> ) )
		{
			var args = t.GetGenericArguments();

			keyType = args[ 0 ];
			valueType = args[ 1 ];

			return true;
		}

		var iDict = t.GetInterfaces().FirstOrDefault( i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof( IDictionary<,> ) );

		if ( iDict is not null )
		{
			var args = iDict.GetGenericArguments();

			keyType = args[ 0 ];
			valueType = args[ 1 ];

			return true;
		}

		return false;
	}

	private static bool IsGenericList( Type t, out Type? elemType )
	{
		elemType = null;

		if ( t.IsGenericType && t.GetGenericTypeDefinition() == typeof( List<> ) )
		{
			elemType = t.GetGenericArguments()[ 0 ];

			return true;
		}

		var iEnum = t.GetInterfaces().FirstOrDefault( i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof( IEnumerable<> ) );

		if ( iEnum is not null )
		{
			elemType = iEnum.GetGenericArguments()[ 0 ];

			return typeof( System.Collections.IList ).IsAssignableFrom( t );
		}

		return false;
	}
}
