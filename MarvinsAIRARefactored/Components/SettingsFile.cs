
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

	// When the settings file (or its backup) is transiently locked by another process - most often
	// OneDrive syncing the Documents folder - the save is postponed and retried after this many ticks
	// instead of surfacing as a fatal worker-thread error.
	private const int SerializationRetryFrames = 60;

	// True while a save is being retried after a file-in-use failure, so the postpone (and eventual
	// recovery) is logged once per streak rather than on every retry.
	private bool _serializationPostponed = false;

	// Log lines for settings that have changed since the last save, keyed so repeated changes to the same
	// setting (e.g. window position while dragging) collapse to a single latest line instead of flooding
	// the log every frame. Flushed to the log and cleared when the settings file is actually written in Tick.
	private readonly Dictionary<string, string> _changedSettings = [];

	public void RecordChangedSetting( string key, string message )
	{
		_changedSettings[ key ] = message;
	}

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

		// One-time: seed the non-car overlay layout from the existing top-level overlay position/scale values
		// so users upgrading from a version without per-car overlays keep their current layout.
		DataContext.DataContext.Instance.Settings.MigrateOverlayLayoutToNonCarBaseline();

		// Every launch: sync the stored built-in graphs against the graph files shipped inside the app
		// (create missing ones, refresh any whose shipped file changed, purge retired ones) and repair the
		// selections. If anything changed, remember to persist below (the recorded file hashes must reach
		// disk or the sync would re-run on every launch).
		var builtInGraphsChanged = DataContext.DataContext.Instance.Settings.EnsureBuiltInFFBGraphsInitialized();

		// Backfill stable graph identities for any legacy graph that predates them (built-ins already carry
		// their fixed id from the shipped file). Persisted below so the ids never change across launches.
		var graphIdentitiesChanged = DataContext.DataContext.Instance.Settings.EnsureGraphIdentitiesAssigned();

		// Build the live FFB graph engine from the selected graph so it is ready to drive FFB
		// immediately; the first per-context reload will rebuild it with this car/track's values.
		app.RacingWheel.RebuildLiveEngine();

		// Populate the RacingWheelPage graph editor card tree from the selected graph.
		Settings.RebuildGraphEditorViewModel();

		Settings.SuppressUpdatingOfContextSettings = false;

		PauseSerialization = false;

		// Persist a launch-time built-in graph sync now that serialization is un-paused (the setter ignores
		// a queue request while paused), so the recorded graph file hashes reach disk and the sync runs once.
		if ( builtInGraphsChanged || graphIdentitiesChanged )
		{
			QueueForSerialization = true;
		}

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
				try
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

					foreach ( var changedSetting in _changedSettings.Values )
					{
						app.Logger.WriteLine( changedSetting );
					}

					_changedSettings.Clear();

					app.Logger.WriteLine( "[SettingsFile] Settings.xml file updated" );

					if ( _serializationPostponed )
					{
						_serializationPostponed = false;

						app.Logger.WriteLine( "[SettingsFile] Settings save recovered after being postponed" );
					}
				}
				catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
				{
					// The settings file or its backup is transiently locked by another process (most often
					// OneDrive syncing the Documents folder). Postpone the save and retry shortly instead of
					// letting it bubble up as a fatal worker-thread error. The pending changes and their log
					// lines are left intact so nothing is lost across the retry.
					_serializationCounter = SerializationRetryFrames;

					if ( !_serializationPostponed )
					{
						_serializationPostponed = true;

						app.Logger.WriteLine( $"[SettingsFile] Settings save postponed - file in use, will retry ({exception.Message})" );
					}
				}
			}
		}
	}
}
