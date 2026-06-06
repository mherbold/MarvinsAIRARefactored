
using System.Windows;
using System.Windows.Media;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Controls;

using AppDataContext = MarvinsAIRARefactored.DataContext.DataContext;
using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class SpeechToTextPage : UserControl
{
	private SttSubscriptionInfo? _lastSubscriptionInfo;
	private int _sessionCharactersUsed;

	public SpeechToTextPage()
	{
		InitializeComponent();

		AppDataContext.Instance.Settings.PropertyChanged += Settings_PropertyChanged;

		Loaded += ( _, _ ) => App.Instance!.SpeechToText.TranscriptReceived += SpeechToText_TranscriptReceived;
		Unloaded += ( _, _ ) => App.Instance!.SpeechToText.TranscriptReceived -= SpeechToText_TranscriptReceived;
	}

	public async void OnPageActivated()
	{
		UpdateLanguageOptions();
		UpdateRecordingDeviceOptions();
		LoadApiKeyIntoPasswordBox();
		await VerifyAndPopulateAsync();
	}

	#region User Control Events

	private void SpeechToText_TranscriptReceived( string text, int charactersCharged )
	{
		_sessionCharactersUsed += charactersCharged;

		var displayText = charactersCharged > 0 ? "* " + text : text;

		Dispatcher.InvokeAsync( () =>
		{
			LastTranscription_TextBlock.Text = displayText;
			UpdateSubscriptionUsageDisplay();
		} );
	}

	private async void Settings_PropertyChanged( object? sender, System.ComponentModel.PropertyChangedEventArgs e )
	{
		switch ( e.PropertyName )
		{
			case nameof( MarvinsAIRARefactored.DataContext.Settings.SpeechToTextElevenLabsApiKey ):
				await VerifyAndPopulateAsync();
				break;
		}
	}

	private void ResetOverlayWindow_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.SpeechToTextWindow?.ResetWindow();
	}

	private void ApiKey_MairaTextBox_ValueChanged( object sender, RoutedEventArgs e )
	{
		var key = ApiKey_MairaTextBox.Value.Trim();

		if ( key != ApiKey_MairaTextBox.Value )
		{
			ApiKey_MairaTextBox.Value = key;
			return;
		}

		try
		{
			AppDataContext.Instance.Settings.SpeechToTextElevenLabsApiKey = key;
		}
		catch ( Exception ex )
		{
			App.Instance!.Logger.WriteLine( $"[SpeechToTextPage] Failed to save STT API key: {ex.Message}" );
		}
	}

	private async void VerifyKey_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		VerifyResult_TextBlock.Text = "…";
		await VerifyAndPopulateAsync();
	}

	#endregion

	#region Logic

	private void LoadApiKeyIntoPasswordBox()
	{
		try
		{
			ApiKey_MairaTextBox.Value = AppDataContext.Instance.Settings.SpeechToTextElevenLabsApiKey;
		}
		catch
		{
			ApiKey_MairaTextBox.Value = string.Empty;
		}
	}

	private async Task VerifyAndPopulateAsync()
	{
		var app = App.Instance!;
		var localization = AppDataContext.Instance.Localization;

		try
		{
			var result = await app.SpeechToText.VerifyApiKeyAsync();

			if ( !result.IsRecognized )
			{
				VerifyResult_TextBlock.Text = localization[ "KeyInvalid" ];
				VerifyResult_TextBlock.Foreground = (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Status.Error.Text" );
				return;
			}

			string permSymbol( PermissionStatus status ) =>
				status == PermissionStatus.Granted
					? localization[ "KeyPermissionGranted" ]
					: localization[ "KeyPermissionMissing" ];

			if ( result.IsFullyFunctional )
			{
				VerifyResult_TextBlock.Text =
					$"{localization[ "KeyVerified" ]}\n{permSymbol( result.SpeechToText )}  {localization[ "KeyPermissionSpeechToText" ]}\n{permSymbol( result.UserRead )}  {localization[ "KeyPermissionUserRead" ]}";
				VerifyResult_TextBlock.Foreground = (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Accent.Blue" );
			}
			else
			{
				VerifyResult_TextBlock.Text =
					$"{localization[ "KeyRecognized" ]}\n{permSymbol( result.SpeechToText )}  {localization[ "KeyPermissionSpeechToText" ]}\n{permSymbol( result.UserRead )}  {localization[ "KeyPermissionUserRead" ]}";
				VerifyResult_TextBlock.Foreground = (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Status.Warning.Text" );
			}

			await UpdateSubscriptionUsageAsync();
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[SpeechToTextPage] VerifyAndPopulateAsync error: {ex.Message}" );
			VerifyResult_TextBlock.Text = localization[ "KeyInvalid" ];
			VerifyResult_TextBlock.Foreground = (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Status.Error.Text" );
		}
	}

	private async Task UpdateSubscriptionUsageAsync()
	{
		SubscriptionUsage_MairaProgressBar.Value = 0;

		var info = await App.Instance!.SpeechToText.GetSubscriptionUsageAsync();

		if ( info is null )
		{
			_lastSubscriptionInfo = null;
			return;
		}

		_lastSubscriptionInfo = info;
		_sessionCharactersUsed = App.Instance!.SpeechToText.SessionCharactersUsed;
		LastTranscription_TextBlock.Text = string.Empty;
		UpdateSubscriptionUsageDisplay();
	}

	private void UpdateSubscriptionUsageDisplay()
	{
		if ( _lastSubscriptionInfo is null )
		{
			SubscriptionUsage_MairaProgressBar.Value = 0;
			return;
		}

		var adjustedCharacterCount = _lastSubscriptionInfo.CharacterCount + _sessionCharactersUsed;
		var percent = _lastSubscriptionInfo.CharacterLimit > 0
			? adjustedCharacterCount * 100.0 / _lastSubscriptionInfo.CharacterLimit
			: 0.0;

		SubscriptionUsage_MairaProgressBar.Value = Math.Clamp( percent, 0.0, 100.0 );
	}

	public void UpdateLanguageOptions()
	{
		var app = App.Instance!;
		var localization = AppDataContext.Instance.Localization;

		app.Logger.WriteLine( "[SpeechToTextPage] UpdateLanguageOptions >>>" );

		var options = CommentaryTemplates.GetAvailableLanguages()
			.ToDictionary(
				lang => lang,
				lang =>
				{
					if ( localization.Languages.TryGetValue( lang, out var label ) )
					{
						return label;
					}

					var fallbackKey = lang == "en-US" ? "default" : lang;

					return localization.Languages.TryGetValue( fallbackKey, out label ) ? label : lang;
				} );

		Language_MairaComboBox.ItemsSource = options.ToList();

		app.Logger.WriteLine( "[SpeechToTextPage] <<< UpdateLanguageOptions" );
	}

	public void UpdateRecordingDeviceOptions()
	{
		var app = App.Instance!;
		var localization = AppDataContext.Instance.Localization;
		var settings = AppDataContext.Instance.Settings;

		app.Logger.WriteLine( "[SpeechToTextPage] UpdateRecordingDeviceOptions >>>" );

		app.SpeechToText.RefreshRecordingDevices();

		var options = new Dictionary<string, string>
		{
			{ SpeechToText.DefaultRecordingDeviceName, localization[ "DefaultWindowsSoundDevice" ] }
		};

		app.SpeechToText.RecordingDevices.ToList().ForEach( deviceName => options[ deviceName ] = deviceName );

		if ( !options.ContainsKey( settings.SpeechToTextRecordingDevice ) )
		{
			options.Add( settings.SpeechToTextRecordingDevice, $"{localization[ "DeviceNotFound" ]} [{settings.SpeechToTextRecordingDevice}]" );
		}

		RecordingDevice_MairaComboBox.ItemsSource = options.ToList();

		app.Logger.WriteLine( "[SpeechToTextPage] <<< UpdateRecordingDeviceOptions" );
	}

	#endregion
}
