
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using MarvinsAIRARefactored.Classes;

using ComboBox = System.Windows.Controls.ComboBox;

namespace MarvinsAIRARefactored.Components;

public partial class Localization : INotifyPropertyChanged
{
	private Dictionary<string, string> _defaults = [];
	private Dictionary<string, string> _translations = [];

	public event PropertyChangedEventHandler? PropertyChanged;

	public void OnPropertyChanged( [CallerMemberName] string? propertyName = null )
	{
		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
	}

	public string this[ string key ]
	{
		get
		{
			if ( _translations.TryGetValue( key, out var value ) )
			{
				return value;
			}
			else if ( _defaults.TryGetValue( key, out value ) )
			{
				return value;
			}
			else
			{
				return $"!{key}!";
			}
		}
	}

	public void LoadLanguage( string? languageCode = "default" )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[Localization] Loading language: {languageCode}" );

		var languagesDirectory = Path.Combine( App.DocumentsFolder, "Languages" );

		if ( !Directory.Exists( languagesDirectory ) )
		{
			Directory.CreateDirectory( languagesDirectory );
		}

		var languageFile = ( languageCode == "default" ) ? "Resources.resx" : $"Resources.{languageCode}.resx";

		var filePath = Path.Combine( languagesDirectory, languageFile );

		if ( File.Exists( filePath ) )
		{
			app.Logger.WriteLine( $"[Localization] Language found in user documents folder" );

			_translations = Misc.LoadResx( filePath );
		}
		else
		{
			filePath = Path.Combine( AppDomain.CurrentDomain.BaseDirectory, "Resources", languageFile );

			if ( File.Exists( filePath ) )
			{
				app.Logger.WriteLine( $"[Localization] Language found in app folder" );

				_translations = Misc.LoadResx( filePath );
			}
			else
			{
				app.Logger.WriteLine( $"[Localization] Language not found" );

				_translations = [];
			}
		}

		OnPropertyChanged( null );
	}

	public void LoadDefaultLanguage()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Localization] LoadDefaultLanguage >>>" );

		LoadLanguage();

		_defaults = _translations;

		OnPropertyChanged( null );

		app.Logger.WriteLine( "[Localization] <<< LoadDefaultLanguage" );
	}

	public static void SetLanguageComboBoxItemsSource( ComboBox comboBox )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Localization] SetLanguageComboBoxItemsSource >>>" );

		var languagesDirectory = Path.Combine( App.DocumentsFolder, "Languages" );

		if ( !Directory.Exists( languagesDirectory ) )
		{
			Directory.CreateDirectory( languagesDirectory );
		}

		var comboBoxItemsDictionary = new Dictionary<string, string> { { "default", "English" } };

		var regex = MyRegex();

		var files = Directory.GetFiles( languagesDirectory, "*.resx" );

		foreach ( var file in files )
		{
			var fileName = Path.GetFileName( file );

			var match = regex.Match( fileName );

			if ( match.Success )
			{
				var resxDictionary = Misc.LoadResx( file );

				if ( resxDictionary.TryGetValue( "ThisLanguage", out var value ) )
				{
					comboBoxItemsDictionary.Add( match.Groups[ "languageCode" ].Value, value );
				}
			}
		}

		comboBox.ItemsSource = comboBoxItemsDictionary;

		app.Logger.WriteLine( "[Localization] <<< SetLanguageComboBoxItemsSource" );
	}

	[GeneratedRegex( @"^Resources\.(?<languageCode>[a-z]{2}(-[A-Z]{2})?)\.resx$", RegexOptions.IgnoreCase, "en-US" )]
	private static partial Regex MyRegex();
}
