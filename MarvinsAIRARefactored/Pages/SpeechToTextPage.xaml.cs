
using System.Windows;
using System.Windows.Media;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;

using AppDataContext = MarvinsAIRARefactored.DataContext.DataContext;
using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class SpeechToTextPage : UserControl
{
	public SpeechToTextPage()
	{
		InitializeComponent();

		AppDataContext.Instance.Settings.PropertyChanged += Settings_PropertyChanged;

		Loaded += ( _, _ ) => App.Instance!.SpeechToText.UpdateSubscriptionUsage += UpdateSubscriptionUsage;
		Unloaded += ( _, _ ) => App.Instance!.SpeechToText.UpdateSubscriptionUsage -= UpdateSubscriptionUsage;
	}

	private void UpdateSubscriptionUsage( CancellationToken cancellationToken = default )
	{
		Dispatcher.InvokeAsync( async () =>
		{
			try
			{
				await ElevenLabs.UpdateSubscriptionUsageAsync( AppDataContext.Instance.Settings.SpeechToTextElevenLabsApiKey, SubscriptionUsage_MairaProgressBar, cancellationToken );
			}
			catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
			{
			}
			catch ( Exception ex )
			{
				App.Instance?.Logger.WriteLine( $"[SpeechToTextPage] UpdateSubscriptionUsage error: {ex.Message}" );
			}
		} );
	}

	public async void OnPageActivated()
	{
		UpdateRecordingDeviceOptions();
		LoadApiKeyIntoPasswordBox();
		await VerifyAndPopulateAsync();
	}

	#region User Control Events

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
		App.Instance!.SpeechToTextWindow?.ResetWindow();
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
			var result = await SpeechToText.VerifyApiKeyAsync();

			if ( !result.IsRecognized )
			{
				VerifyResult_TextBlock.Text = localization[ "KeyInvalid" ];
				VerifyResult_TextBlock.Foreground = (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Status.Error.Text" );
				return;
			}

			string permSymbol( ElevenLabs.PermissionStatus status ) => status == ElevenLabs.PermissionStatus.Granted ? localization[ "KeyPermissionGranted" ] : localization[ "KeyPermissionMissing" ];

			if ( result.IsFullyFunctional )
			{
				VerifyResult_TextBlock.Text = $"{localization[ "KeyVerified" ]}\n{permSymbol( result.SpeechToText )}  {localization[ "KeyPermissionSpeechToText" ]}\n{permSymbol( result.UserRead )}  {localization[ "KeyPermissionUserRead" ]}";
				VerifyResult_TextBlock.Foreground = (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Accent.Blue" );
			}
			else
			{
				VerifyResult_TextBlock.Text = $"{localization[ "KeyRecognized" ]}\n{permSymbol( result.SpeechToText )}  {localization[ "KeyPermissionSpeechToText" ]}\n{permSymbol( result.UserRead )}  {localization[ "KeyPermissionUserRead" ]}";
				VerifyResult_TextBlock.Foreground = (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Status.Warning.Text" );
			}

			UpdateSubscriptionUsage();
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[SpeechToTextPage] VerifyAndPopulateAsync error: {ex.Message}" );

			VerifyResult_TextBlock.Text = localization[ "KeyInvalid" ];
			VerifyResult_TextBlock.Foreground = (SolidColorBrush) System.Windows.Application.Current.FindResource( "Brush.Status.Error.Text" );
		}
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
