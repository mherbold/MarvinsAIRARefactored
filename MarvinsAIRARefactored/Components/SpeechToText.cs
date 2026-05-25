
using System.Diagnostics;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Components;

public sealed class SpeechToText : IDisposable
{
	private ChromeSTTBridge? _chromeSTTBridge;
	private Process? _browserProcess;
	private string _language = "en-US";
	private bool _isEnabled = false;

	public string Language
	{
		get => _language;

		set
		{
			_language = value ?? "en-US";

			_ = _chromeSTTBridge?.SendSetLanguageAsync( _language );
		}
	}

	public void Dispose() => _ = DisableAsync();

	public async Task EnableAsync( int port = 18888 )
	{
		var app = App.Instance!;

		if ( !app.Simulator.IsConnected || _isEnabled )
		{
			return;
		}

		app.Logger.WriteLine( "[SpeechToText] >>> EnableAsync" );

		app.Logger.WriteLine( "[SpeechToText] Checking for Chrome/Edge" );

		if ( !ChromeLauncher.IsChromeOrEdgeInstalled() )
		{
			app.Logger.WriteLine( "[SpeechToText] Chrome/Edge not found. Please install Chrome or Edge to use STT." );

			app.SpeechToTextWindow?.SetFinalText( "Chrome/Edge not found. Please install Chrome or Edge to use STT." );
		}
		else
		{

			app.Logger.WriteLine( "[SpeechToText] Enabling Chrome STT bridge" );

			_chromeSTTBridge = new ChromeSTTBridge( port );

			_chromeSTTBridge.PartialText += text =>
			{
				app.EnsureSpeechToTextWindowExists();
				app.SpeechToTextWindow?.SetPartialText( text );
			};

			_chromeSTTBridge.FinalText += text =>
			{
				app.EnsureSpeechToTextWindowExists();
				app.SpeechToTextWindow?.SetFinalText( text );
			};

			_chromeSTTBridge.ErrorText += text =>
			{
				if ( text != "no-speech" )
				{
					app.EnsureSpeechToTextWindowExists();
					app.SpeechToTextWindow?.SetFinalText( text );
				}
			};

			_chromeSTTBridge.Start();

			// Launch the minimal browser window to the hosted page

			_browserProcess = ChromeLauncher.LaunchChromeTo( _chromeSTTBridge.HostURL );

			if ( _browserProcess == null )
			{
				app.Logger.WriteLine( "[SpeechToText] Failed to launch Chrome/Edge." );

				app.EnsureSpeechToTextWindowExists();
				app.SpeechToTextWindow?.SetFinalText( "Failed to launch Chrome/Edge." );

				await DisableAsync();
			}
			else
			{
				// Wait for the page to connect its WebSocket (up to 10s)

				var connected = await _chromeSTTBridge.WaitUntilConnectedAsync( TimeSpan.FromSeconds( 10 ) );

				if ( !connected )
				{
					app.Logger.WriteLine( "[SpeechToText] Browser page did not connect within 10s." );

					app.EnsureSpeechToTextWindowExists();
					app.SpeechToTextWindow?.SetFinalText( "[SpeechToText] Browser page did not connect within 10s." );

					await DisableAsync();
				}
				else
				{

					// Set initial language

					await _chromeSTTBridge.SendSetLanguageAsync( _language );

					app.Logger.WriteLine( "[SpeechToText] STT ready. First run: user must click 'Enable Microphone' and allow access." );

					_isEnabled = true;
				}
			}
		}

		app.UpdateSpeechToTextWindowVisibility();

		app.Logger.WriteLine( "[SpeechToText] << EnableAsync" );
	}

	public async Task DisableAsync()
	{
		if ( !_isEnabled && ( _chromeSTTBridge == null ) )
		{
			return;
		}

		var app = App.Instance!;

		app.Logger.WriteLine( "[SpeechToText] >>> DisableAsync" );

		try
		{
			if ( _chromeSTTBridge != null )
			{
				await _chromeSTTBridge.StopAsync();

				_chromeSTTBridge = null;
			}
		}
		catch
		{
			// ignore
		}

		try
		{
			if ( ( _browserProcess != null ) && !_browserProcess.HasExited )
			{
				_browserProcess.CloseMainWindow();
				_browserProcess.Dispose();
			}

			_browserProcess = null;
		}
		catch
		{
			// ignore
		}

		_isEnabled = false;

		app.UpdateSpeechToTextWindowVisibility();

		app.Logger.WriteLine( "[SpeechToText] << DisableAsync" );
	}

	public void SimulatorConnected()
	{
		var settings = DataContext.DataContext.Instance.Settings;

		if ( settings.SpeechToTextEnabled )
		{
			_ = EnableAsync();
		}
	}

	public void SimulatorDisconnected()
	{
		_ = DisableAsync();
	}

	public void UpdateStrings()
	{
		_chromeSTTBridge?.UpdateStrings();
	}
}
