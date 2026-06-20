
using System.IO;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.DataContext;

namespace MarvinsAIRARefactored.Components;

public class SettingsFile
{
	private static string SettingsFilePath { get; } = Path.Combine( App.DocumentsFolder, "Settings.xml" );
	private static string SettingsBackupFilePath { get; } = Path.Combine( App.DocumentsFolder, "Settings.xml.bak" );

	private bool _pauseSerialization = false;
	public bool PauseSerialization
	{
		get => _pauseSerialization;

		set
		{
			if ( value != _pauseSerialization )
			{
				_pauseSerialization = value;

				var app = App.Instance!;

				if ( value )
				{
					app.Logger.WriteLine( "[SettingsFile] Pausing serialization" );
				}
				else
				{
					app.Logger.WriteLine( "[SettingsFile] Un-pausing serialization" );
				}
			}
		}
	}

	private bool _queueForSerialization = false;
	public bool QueueForSerialization
	{
		private get => _queueForSerialization;

		set
		{
			if ( value != _queueForSerialization )
			{
				if ( !value || !PauseSerialization )
				{
					_queueForSerialization = value;
				}
			}
		}
	}

	private int _serializationCounter = 0;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SettingsFile] Initialize >>>" );

		PauseSerialization = true;

		Settings.SuppressUpdatingOfContextSettings = true;

		if ( File.Exists( SettingsFilePath ) )
		{
			var loaded = false;

			try
			{
				DataContext.DataContext.Instance.Settings = (Settings) Serializer.Load<Settings>( SettingsFilePath );

				loaded = true;
			}
			catch ( Exception ex )
			{
				app.Logger.WriteLine( $"[SettingsFile] Failed to load settings file: {ex.Message}" );
			}

			if ( !loaded && File.Exists( SettingsBackupFilePath ) )
			{
				app.Logger.WriteLine( "[SettingsFile] Attempting to restore settings from backup" );

				try
				{
					File.Copy( SettingsBackupFilePath, SettingsFilePath, overwrite: true );

					DataContext.DataContext.Instance.Settings = (Settings) Serializer.Load<Settings>( SettingsFilePath );

					app.Logger.WriteLine( "[SettingsFile] Settings restored from backup successfully" );
				}
				catch ( Exception ex )
				{
					app.Logger.WriteLine( $"[SettingsFile] Failed to restore settings from backup: {ex.Message}" );
				}
			}
			else if ( !loaded )
			{
				app.Logger.WriteLine( "[SettingsFile] No backup available - starting with default settings" );
			}
		}
		else
		{
			app.Logger.WriteLine( "[SettingsFile] Settings file does not exist - we will create a new one" );

			DataContext.DataContext.Instance.Settings.AppCurrentLanguageCode = DataContext.DataContext.Instance.Localization.ChooseInitialLanguage();

			// Brand-new install: no old auto-margin to migrate. Seed auto target to the default (clamped to
			// the wheel force) and mark the migration done so it never runs over these defaults.
			DataContext.DataContext.Instance.Settings.RacingWheelAutoTarget = 10f;
			DataContext.DataContext.Instance.Settings.RacingWheelAutoTargetMigrated = true;
		}

			// Migrate the old percentage-based auto margin to the new Nm-based auto target (value, scope,
			// and input mappings). Must run before the controller profiles are initialized/applied so the
			// renamed profile mapping keys are in place.
			DataContext.DataContext.Instance.Settings.MigrateAutoMarginToAutoTarget();

			// Migrate a pre-profiles settings file (snapshot the existing flat mappings into a
			// "Default" controller profile) and guarantee a valid active profile.
			DataContext.DataContext.Instance.Settings.EnsureControllerProfilesInitialized();

			Settings.SuppressUpdatingOfContextSettings = false;

			PauseSerialization = false;

			// Sync the AppShowSplashScreen setting with the DisableSplashScreen.txt file state
			// This ensures the setting reflects reality if the user manually deleted the file
			var disableSplashScreenFilePath = Path.Combine( App.DocumentsFolder, "DisableSplashScreen.txt" );
			var fileExists = File.Exists( disableSplashScreenFilePath );
			var showSplashScreen = !fileExists;

			if ( DataContext.DataContext.Instance.Settings.AppShowSplashScreen != showSplashScreen )
			{
				app.Logger.WriteLine( $"[SettingsFile] Syncing AppShowSplashScreen setting to {showSplashScreen} based on file presence" );

				DataContext.DataContext.Instance.Settings.AppShowSplashScreen = showSplashScreen;
			}

			app.Logger.WriteLine( "[SettingsFile] <<< Initialize" );
		}

	public void Tick( App app )
	{
		if ( QueueForSerialization )
		{
			if ( _serializationCounter == 0 )
			{
				app.Logger.WriteLine( "[SettingsFile] Queued for serialization" );
			}

			_serializationCounter = 60;

			QueueForSerialization = false;
		}

		if ( _serializationCounter > 0 )
		{
			_serializationCounter--;

			if ( _serializationCounter == 0 )
			{
				if ( File.Exists( SettingsFilePath ) )
				{
					File.Copy( SettingsFilePath, SettingsBackupFilePath, overwrite: true );

					app.Logger.WriteLine( "[SettingsFile] Settings.xml backup created" );
				}

				// Flush the live working-copy mappings into the active controller profile so the
				// persisted store stays authoritative no matter where a mapping was edited.
				DataContext.DataContext.Instance.Settings.SaveCurrentControllerProfile();

				Serializer.Save( SettingsFilePath, DataContext.DataContext.Instance.Settings );

				app.Logger.WriteLine( "[SettingsFile] Settings.xml file updated" );
			}
		}
	}
}
