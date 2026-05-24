
using System.IO;
using System.Windows;
using System.Windows.Media;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Controls;

using AppDataContext = MarvinsAIRARefactored.DataContext.DataContext;

namespace MarvinsAIRARefactored.Pages;

public partial class TextToSpeechPage : System.Windows.Controls.UserControl
{
	public TextToSpeechPage()
	{
		InitializeComponent();

		AppDataContext.Instance.Settings.PropertyChanged += Settings_PropertyChanged;
	}

	private async void Settings_PropertyChanged( object? sender, System.ComponentModel.PropertyChangedEventArgs e )
	{
		if ( e.PropertyName == nameof( MarvinsAIRARefactored.DataContext.Settings.TtsEnabled ) &&
			 AppDataContext.Instance.Settings.TtsEnabled )
		{
			await VerifyAndPopulateAsync();
		}
	}

	#region Page Activation

	/// <summary>Called from MainWindow when this page becomes active.</summary>
	public async void OnPageActivated()
	{
		UpdateLanguageOptions();
		LoadApiKeyIntoPasswordBox();

		if ( AppDataContext.Instance.Settings.TtsEnabled )
		{
			await VerifyAndPopulateAsync();
		}
		else
		{
			UpdateModelOptions();
			UpdateVoiceOptions();
			await UpdateSubscriptionUsageAsync();
		}
	}

	#endregion

	#region Helpers

	private void LoadApiKeyIntoPasswordBox()
	{
		try
		{
			ApiKey_MairaTextBox.Value = ElevenLabsKeyStore.LoadKey();
		}
		catch
		{
			// Key not yet saved — leave the box empty
		}
	}

	/// <summary>Populates the TTS language combo from available JSON template files.</summary>
	public void UpdateLanguageOptions()
	{
		var app = App.Instance!;
		var localization = AppDataContext.Instance.Localization;

		app.Logger.WriteLine( "[TextToSpeechPage] UpdateLanguageOptions >>>" );

		var options = CommentaryTemplates.GetAvailableLanguages()
			.ToDictionary(
				lang => lang,
				lang =>
				{
					if ( localization.Languages.TryGetValue( lang, out var label ) )
					{
						return label;
					}

					// "en-US" is stored under the "default" sentinel key
					var fallbackKey = lang == "en-US" ? "default" : lang;

					return localization.Languages.TryGetValue( fallbackKey, out label ) ? label : lang;
				} );

		TtsLanguage_MairaComboBox.ItemsSource = options.ToList();

		app.Logger.WriteLine( "[TextToSpeechPage] <<< UpdateLanguageOptions" );
	}

	private static readonly Dictionary<string, string> _fallbackModels = new()
	{
		{ "eleven_flash_v2_5", "Flash v2.5" },
		{ "eleven_turbo_v2_5", "Turbo v2.5" },
	};

	private async void UpdateModelOptions()
	{
		var app = App.Instance!;

		var models = await app.TextToSpeech.GetTtsModelsAsync();

		Model_MairaComboBox.ItemsSource = ( models ?? _fallbackModels ).ToList();
	}

	private static readonly Dictionary<string, string> _fallbackVoices = new()
	{
		{ "", "(no voices — verify API key)" },
	};

	private async void UpdateVoiceOptions()
	{
		var app = App.Instance!;
		var voices = await app.TextToSpeech.GetVoicesAsync();

		app.Logger.WriteLine( $"[TextToSpeechPage] UpdateVoiceOptions: GetVoicesAsync returned {( voices is null ? "null" : $"{voices.Count} voice(s)" )}" );

		if ( voices is not null )
		{
			foreach ( var kv in voices )
			{
				app.Logger.WriteLine( $"[TextToSpeechPage] UpdateVoiceOptions:   id={kv.Key}, name={kv.Value}" );
			}
		}

		var localization = AppDataContext.Instance.Localization;

		var placeholder = new KeyValuePair<string, string>( "", localization[ "VoiceNotSelected" ] );
		var items = ( voices ?? _fallbackVoices ).Prepend( placeholder ).ToList();

		foreach ( var expander in new[] { Slot0_Expander, Slot1_Expander, Slot2_Expander, Slot3_Expander, Slot4_Expander } )
		{
			// ExpanderContent is a plain DP — not registered as a logical child — so
			// LogicalTreeHelper won't cross that boundary. Access it directly instead.
			if ( expander.ExpanderContent is DependencyObject contentRoot )
			{
				var combo = FindLogicalChild<MairaComboBox>( contentRoot );

				if ( combo is not null )
				{
					combo.ItemsSource = items;
				}
			}
		}
	}

	private static T? FindLogicalChild<T>( DependencyObject parent ) where T : DependencyObject
	{
		if ( parent is T match )
		{
			return match;
		}

		foreach ( var child in LogicalTreeHelper.GetChildren( parent ) )
		{
			if ( child is DependencyObject dep )
			{
				var found = FindLogicalChild<T>( dep );

				if ( found is not null )
				{
					return found;
				}
			}
		}

		return null;
	}

	private static T? FindVisualChild<T>( DependencyObject parent ) where T : DependencyObject
	{
		for ( var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount( parent ); i++ )
		{
			var child = System.Windows.Media.VisualTreeHelper.GetChild( parent, i );

			if ( child is T match )
			{
				return match;
			}

			var found = FindVisualChild<T>( child );

			if ( found is not null )
			{
				return found;
			}
		}

		return null;
	}

	private async Task UpdateSubscriptionUsageAsync()
	{
		var localization = AppDataContext.Instance.Localization;

		SubscriptionUsage_TextBlock.Text = localization[ "SubscriptionUsageLoading" ];

		var info = await App.Instance!.TextToSpeech.GetSubscriptionAsync();

		if ( info is null )
		{
			SubscriptionUsage_TextBlock.Text = localization[ "SubscriptionUsageUnavailable" ];
			return;
		}

		SubscriptionUsage_TextBlock.Text = string.Format(
			localization[ "SubscriptionUsage" ],
			info.CharactersUsed,
			info.CharacterLimit,
			info.PercentUsed,
			info.CharactersRemaining );
	}

	#endregion

	#region Button Handlers

	private void ApiKey_MairaTextBox_ValueChanged( object sender, RoutedEventArgs e )
	{
		var key = ApiKey_MairaTextBox.Value.Trim();

		// Write back the trimmed value so pasted whitespace is silently removed
		if ( key != ApiKey_MairaTextBox.Value )
		{
			ApiKey_MairaTextBox.Value = key;
			return; // ValueChanged will fire again with the trimmed value
		}

		try
		{
			ElevenLabsKeyStore.SaveKey( key );

			AppDataContext.Instance.Settings.ApiKey = key;
		}
		catch ( Exception ex )
		{
			App.Instance!.Logger.WriteLine( $"[TextToSpeechPage] Failed to save API key: {ex.Message}" );
		}
	}

	private async void VerifyKey_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		VerifyResult_TextBlock.Text = "…";

		await VerifyAndPopulateAsync();
	}

	/// <summary>
	/// Runs key verification, updates the result text block, and — when the key is recognized —
	/// populates the voice and model drop-downs.
	/// </summary>
	private async Task VerifyAndPopulateAsync()
	{
		var app = App.Instance!;
		var localization = AppDataContext.Instance.Localization;

		try
		{
			var result = await app.TextToSpeech.VerifyKeyAsync();

			if ( !result.IsRecognized )
			{
				VerifyResult_TextBlock.Text = localization[ "KeyInvalid" ];
				VerifyResult_TextBlock.Foreground = (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Status.Error.Text" );
				return;
			}

			string PermSymbol( PermissionStatus s ) =>
				s == PermissionStatus.Granted
					? localization[ "KeyPermissionGranted" ]
					: localization[ "KeyPermissionMissing" ];

			var lines = new System.Text.StringBuilder();

			if ( result.IsFullyFunctional )
			{
				lines.AppendLine( localization[ "KeyVerified" ] );
			}
			else
			{
				lines.AppendLine( localization[ "KeyRecognized" ] );
			}

			lines.AppendLine( $"{PermSymbol( result.TextToSpeech )}  {localization[ "KeyPermissionTextToSpeech" ]}" );
			lines.AppendLine( $"{PermSymbol( result.VoiceRead )}  {localization[ "KeyPermissionVoiceRead" ]}" );
			lines.AppendLine( $"{PermSymbol( result.ModelsRead )}  {localization[ "KeyPermissionModelsRead" ]}" );
			lines.Append( $"{PermSymbol( result.UserRead )}  {localization[ "KeyPermissionUserRead" ]}" );

			VerifyResult_TextBlock.Text = lines.ToString();

			VerifyResult_TextBlock.Foreground = result.IsFullyFunctional
				? (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Accent.Blue" )
				: (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Status.Warning.Text" );

			UpdateVoiceOptions();
			UpdateModelOptions();
			await UpdateSubscriptionUsageAsync();
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[TextToSpeechPage] VerifyAndPopulateAsync error: {ex.Message}" );

			VerifyResult_TextBlock.Text = localization[ "KeyInvalid" ];
			VerifyResult_TextBlock.Foreground = (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Status.Error.Text" );
		}
	}

	private void Slot0Test_MairaButton_Click( object sender, RoutedEventArgs e ) => TestSlot( 0 );
	private void Slot1Test_MairaButton_Click( object sender, RoutedEventArgs e ) => TestSlot( 1 );
	private void Slot2Test_MairaButton_Click( object sender, RoutedEventArgs e ) => TestSlot( 2 );
	private void Slot3Test_MairaButton_Click( object sender, RoutedEventArgs e ) => TestSlot( 3 );
	private void Slot4Test_MairaButton_Click( object sender, RoutedEventArgs e ) => TestSlot( 4 );

	private static void TestSlot( int slotIndex )
	{
		var app = App.Instance!;
		var key = $"TestPhrase{slotIndex}";
		var phrase = app.Commentary.Templates.GetRandomPhrase( key )
			?? app.Commentary.Templates.GetRandomPhrase( "TestPhrase0" )
			?? "Testing voice.";

		app.TextToSpeech.Enqueue( slotIndex, phrase, priority: 1 );
	}

	#endregion
}
