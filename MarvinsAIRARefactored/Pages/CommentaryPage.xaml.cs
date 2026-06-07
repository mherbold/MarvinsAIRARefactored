
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Controls;

using AppDataContext = MarvinsAIRARefactored.DataContext.DataContext;

namespace MarvinsAIRARefactored.Pages;

// ---------------------------------------------------------------------------
// Lightweight view-models for the phrase editor
// ---------------------------------------------------------------------------

/// <summary>One editable phrase string within an event-key group.</summary>
internal sealed class PhraseEntryViewModel
{
	public string EventKey { get; init; } = "";
	public string Text { get; set; } = "";

	/// <summary>The text value at the time it was last saved to disk; used to detect changes and invalidate TTS cache.</summary>
	public string SavedText { get; set; } = "";
}

/// <summary>One event-key group containing its list of phrase entries.</summary>
internal sealed class PhraseEventKeyViewModel
{
	public string EventKey { get; init; } = "";
	public ObservableCollection<PhraseEntryViewModel> Phrases { get; init; } = [];
}

// ---------------------------------------------------------------------------

public partial class CommentaryPage : System.Windows.Controls.UserControl
{
	public CommentaryPage()
	{
		InitializeComponent();

		AppDataContext.Instance.Settings.PropertyChanged += Settings_PropertyChanged;

		// Re-subscribe to the new Settings instance whenever it is replaced (e.g. after settings file load).
		AppDataContext.Instance.PropertyChanged += ( _, e ) =>
		{
			if ( e.PropertyName == nameof( AppDataContext.Settings ) )
			{
				AppDataContext.Instance.Settings.PropertyChanged += Settings_PropertyChanged;
			}
		};

		Loaded += ( _, _ ) => App.Instance!.SpeechToText.UpdateSubscriptionUsage += UpdateSubscriptionUsage;
		Unloaded += ( _, _ ) => App.Instance!.SpeechToText.UpdateSubscriptionUsage -= UpdateSubscriptionUsage;
	}

	private void UpdateSubscriptionUsage( CancellationToken cancellationToken = default )
	{
		Dispatcher.InvokeAsync( async () =>
		{
			try
			{
				await ElevenLabs.UpdateSubscriptionUsageAsync( AppDataContext.Instance.Settings.CommentaryElevenLabsApiKey, SubscriptionUsage_MairaProgressBar, cancellationToken );
			}
			catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
			{
			}
			catch ( Exception ex )
			{
				App.Instance?.Logger.WriteLine( $"[CommentaryPage] UpdateSubscriptionUsage error: {ex.Message}" );
			}
		} );
	}

	private async void Settings_PropertyChanged( object? sender, System.ComponentModel.PropertyChangedEventArgs e )
	{
		var app = App.Instance!;

		switch ( e.PropertyName )
		{
			case nameof( MarvinsAIRARefactored.DataContext.Settings.CommentaryEnabled )
				when AppDataContext.Instance.Settings.CommentaryEnabled:
				await VerifyAndPopulateAsync();
				break;

			case nameof( MarvinsAIRARefactored.DataContext.Settings.CommentaryElevenLabsLanguage ):
				var newLang = AppDataContext.Instance.Settings.CommentaryElevenLabsLanguage;
				app.Logger.WriteLine( $"[CommentaryPage] Settings_PropertyChanged: language changed to '{newLang}', thread={System.Threading.Thread.CurrentThread.ManagedThreadId}" );
				app.Commentary.Initialize();
				app.Logger.WriteLine( $"[CommentaryPage] Commentary.Initialize() done, LoadedLanguage='{app.Commentary.Templates.LoadedLanguage}', PhraseCount={app.Commentary.Templates.Phrases.Count}" );
				await Dispatcher.InvokeAsync( PopulatePhraseEditor );
				app.Logger.WriteLine( $"[CommentaryPage] PopulatePhraseEditor() done, _phraseViewModels.Count={_phraseViewModels.Count}" );
				break;
		}
	}

	#region Page Activation

	/// <summary>Called from MainWindow when this page becomes active.</summary>
	public async void OnPageActivated()
	{
		UpdateLanguageOptions();
		LoadApiKeyIntoPasswordBox();

		if ( AppDataContext.Instance.Settings.CommentaryEnabled )
		{
			await VerifyAndPopulateAsync();
		}
		else
		{
			UpdateModelOptions();
			UpdateVoiceOptions();
			UpdateSubscriptionUsage();
		}

		PopulatePhraseEditor();
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

		app.Logger.WriteLine( "[CommentaryPage] UpdateLanguageOptions >>>" );

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

		app.Logger.WriteLine( "[CommentaryPage] <<< UpdateLanguageOptions" );
	}

	private async void UpdateModelOptions()
	{
		var app = App.Instance!;

		var models = await TextToSpeech.GetModelsAsync();

		var localization = AppDataContext.Instance.Localization;

		var placeholder = new KeyValuePair<string, string>( "", localization[ "ModelNotSelected" ] );
		var items = ( models ?? _fallbackModels ).Prepend( placeholder ).ToList();

		Model_MairaComboBox.ItemsSource = items;

		// Auto-select the eleven_v3 model if the user has not chosen one yet and it is available.
		var settings = AppDataContext.Instance.Settings;

		if ( string.IsNullOrEmpty( settings.CommentaryElevenLabsModelId ) && models is not null )
		{
			var defaultModel = models.Keys.FirstOrDefault( id => id.StartsWith( "eleven_v3", StringComparison.OrdinalIgnoreCase ) );

			if ( defaultModel is not null )
			{
				settings.CommentaryElevenLabsModelId = defaultModel;
			}
		}
	}

	private static readonly Dictionary<string, string> _fallbackModels = new()
	{
		{ "", "(no models — verify API key)" },
	};

	private static readonly Dictionary<string, string> _fallbackVoices = new()
	{
		{ "", "(no voices — verify API key)" },
	};

	private async void UpdateVoiceOptions()
	{
		var app = App.Instance!;
		var voices = await TextToSpeech.GetVoicesAsync();

		app.Logger.WriteLine( $"[CommentaryPage] UpdateVoiceOptions: GetVoicesAsync returned {( voices is null ? "null" : $"{voices.Count} voice(s)" )}" );

		if ( voices is not null )
		{
			foreach ( var kv in voices )
			{
				app.Logger.WriteLine( $"[CommentaryPage] UpdateVoiceOptions:   id={kv.Key}, name={kv.Value}" );
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
		for ( var i = 0; i < VisualTreeHelper.GetChildrenCount( parent ); i++ )
		{
			var child = VisualTreeHelper.GetChild( parent, i );

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

			AppDataContext.Instance.Settings.CommentaryElevenLabsApiKey = key;
		}
		catch ( Exception ex )
		{
			App.Instance!.Logger.WriteLine( $"[CommentaryPage] Failed to save API key: {ex.Message}" );
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
			var result = await TextToSpeech.VerifyKeyAsync();

			if ( !result.IsRecognized )
			{
				VerifyResult_TextBlock.Text = localization[ "KeyInvalid" ];
				VerifyResult_TextBlock.Foreground = (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Status.Error.Text" );
				return;
			}

			string PermSymbol( ElevenLabs.PermissionStatus s ) => s == ElevenLabs.PermissionStatus.Granted ? localization[ "KeyPermissionGranted" ] : localization[ "KeyPermissionMissing" ];

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

			VerifyResult_TextBlock.Foreground = result.IsFullyFunctional ? (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Accent.Blue" ) : (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Status.Warning.Text" );

			UpdateVoiceOptions();
			UpdateModelOptions();
			UpdateSubscriptionUsage();
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[CommentaryPage] VerifyAndPopulateAsync error: {ex.Message}" );

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
		var phrase = app.Commentary.Templates.GetRandomPhrase( "TestPhrase" ) ?? "Testing voice.";

		app.TextToSpeech.Enqueue( slotIndex, phrase, priority: 1 );
	}

	#endregion

	// -------------------------------------------------------------------------
	#region Phrase Editor
	// -------------------------------------------------------------------------

	// Working copy: event-key → mutable list of phrases (built from loaded templates + user overrides)
	private readonly List<PhraseEventKeyViewModel> _phraseViewModels = [];

	/// <summary>
	/// Populates the phrase editor from the currently loaded templates (which already
	/// incorporate any user-customized file if one exists).
	/// </summary>
	private void PopulatePhraseEditor()
	{
		var templates = App.Instance!.Commentary.Templates;

		App.Instance!.Logger.WriteLine( $"[CommentaryPage] PopulatePhraseEditor: LoadedLanguage='{templates.LoadedLanguage}', PhraseCount={templates.Phrases.Count}, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}" );

		_phraseViewModels.Clear();

		foreach ( var kv in templates.Phrases.OrderBy( kv => kv.Key ) )
		{
			var vm = new PhraseEventKeyViewModel { EventKey = kv.Key };

			foreach ( var phrase in kv.Value )
			{
				vm.Phrases.Add( new PhraseEntryViewModel { EventKey = kv.Key, Text = phrase, SavedText = phrase } );
			}

			_phraseViewModels.Add( vm );
		}

		ApplyPhraseFilter();
		PhraseEditorStatus_TextBlock.Text = "";
	}

	/// <summary>Filters the visible event-key groups by the current search text.</summary>
	private void ApplyPhraseFilter()
	{
		var filter = PhraseSearch_MairaTextBox?.Value ?? "";

		var filtered = string.IsNullOrWhiteSpace( filter )
			? _phraseViewModels
			: _phraseViewModels
				.Where( vm => vm.EventKey.Contains( filter, StringComparison.OrdinalIgnoreCase )
						   || vm.Phrases.Any( p => p.Text.Contains( filter, StringComparison.OrdinalIgnoreCase ) ) )
				.ToList();

		App.Instance!.Logger.WriteLine( $"[CommentaryPage] ApplyPhraseFilter: filtered.Count={( filtered is List<PhraseEventKeyViewModel> l ? l.Count : _phraseViewModels.Count )}" );
		PhraseEventKeys_ItemsControl.ItemsSource = null;
		PhraseEventKeys_ItemsControl.ItemsSource = filtered;
	}

	private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;

	private void PhraseSearch_MairaTextBox_ValueChanged( object sender, RoutedEventArgs e )
	{
		if ( _searchDebounceTimer is null )
		{
			_searchDebounceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds( 1 ) };

			_searchDebounceTimer.Tick += ( _, _ ) =>
			{
				_searchDebounceTimer.Stop();
				ApplyPhraseFilter();
			};
		}

		_searchDebounceTimer.Stop();
		_searchDebounceTimer.Start();
	}

	/// <summary>Rebuilds the dictionary from the current view-models and saves it.</summary>
	private void SavePhrases()
	{
		var language = AppDataContext.Instance.Settings.CommentaryElevenLabsLanguage;

		// Delete cached audio for any phrase that has been modified since last save.
		foreach ( var vm in _phraseViewModels )
		{
			foreach ( var entry in vm.Phrases )
			{
				if ( entry.Text != entry.SavedText && !string.IsNullOrWhiteSpace( entry.SavedText ) )
				{
					TextToSpeech.DeleteCachedPhrasesForText( entry.SavedText, language );
				}

				entry.SavedText = entry.Text;
			}
		}

		var dict = _phraseViewModels
			.ToDictionary(
				vm => vm.EventKey,
				vm => vm.Phrases.Select( e => e.Text ).Where( t => !string.IsNullOrWhiteSpace( t ) ).ToArray() );

		UserCommentaryPhrases.Save( language, dict );

		// Reload templates so the commentary system picks up the new phrases
		App.Instance!.Commentary.Initialize( language );

		var localization = AppDataContext.Instance.Localization;
		PhraseEditorStatus_TextBlock.Text = localization[ "PhraseEditorSaved" ];
		PhraseEditorStatus_TextBlock.Foreground = (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Accent.Blue" );
	}

	private void PhraseTextBox_LostFocus( object sender, RoutedEventArgs e )
	{
		SavePhrases();
	}

	private void TestPhrase_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( sender is not MairaButton button )
		{
			return;
		}

		// Tag="{Binding}" inside a DataTemplate resolves to the row DataContext (PhraseEntryViewModel).
		// Fall back to DataContext in case MairaButton's Tag DP shadowing causes Tag to be null.
		var entry = button.Tag as PhraseEntryViewModel ?? button.DataContext as PhraseEntryViewModel;

		if ( entry is null || string.IsNullOrWhiteSpace( entry.Text ) )
		{
			return;
		}

		// Use the first enabled slot that has a voice configured; otherwise slot 0 (Enqueue handles missing voice gracefully).
		var slots = AppDataContext.Instance.Settings.CommentaryVoiceSlots;
		var slotIndex = slots.FindIndex( s => s.Enabled && !string.IsNullOrWhiteSpace( s.VoiceId ) );

		App.Instance!.TextToSpeech.Enqueue( slotIndex < 0 ? 0 : slotIndex, entry.Text, priority: 1 );
	}

	private void AddPhrase_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( sender is MairaButton button && button.Tag is PhraseEventKeyViewModel vm )
		{
			vm.Phrases.Add( new PhraseEntryViewModel { EventKey = vm.EventKey, Text = "" } );
			// No save yet — user will type and LostFocus will trigger save
		}
	}

	private void RemovePhrase_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( sender is MairaButton button && button.Tag is PhraseEntryViewModel entry )
		{
			var vm = _phraseViewModels.FirstOrDefault( v => v.EventKey == entry.EventKey );

			if ( vm is not null )
			{
				vm.Phrases.Remove( entry );
				SavePhrases();
			}
		}
	}

	private void ResetEventPhrases_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( sender is not MairaButton button || button.Tag is not PhraseEventKeyViewModel vm )
		{
			return;
		}

		// Load the built-in (embedded) phrases for this event key
		var language = AppDataContext.Instance.Settings.CommentaryElevenLabsLanguage;
		var builtIn = CommentaryTemplates.GetBuiltInPhrases( language );

		if ( builtIn is null || !builtIn.TryGetValue( vm.EventKey, out var defaults ) )
		{
			return;
		}

		vm.Phrases.Clear();

		foreach ( var phrase in defaults )
		{
			vm.Phrases.Add( new PhraseEntryViewModel { EventKey = vm.EventKey, Text = phrase } );
		}

		SavePhrases();
	}

	private void ResetAllPhrases_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var language = AppDataContext.Instance.Settings.CommentaryElevenLabsLanguage;

		UserCommentaryPhrases.Delete( language );

		// Reload templates from built-in
		App.Instance!.Commentary.Initialize( language );

		PopulatePhraseEditor();

		var localization = AppDataContext.Instance.Localization;
		PhraseEditorStatus_TextBlock.Text = localization[ "PhraseEditorResetAll" ];
		PhraseEditorStatus_TextBlock.Foreground = (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Accent.Blue" );
	}

	#endregion
}
