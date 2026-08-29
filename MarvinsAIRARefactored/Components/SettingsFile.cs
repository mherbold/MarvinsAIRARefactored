
using System.IO;
using System.Text.RegularExpressions;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.DataContext;

namespace MarvinsAIRARefactored.Components;

public partial class SettingsFile
{
	private static string SettingsFilePath { get; } = Path.Combine( App.DocumentsFolder, "Settings.xml" );
	private static string SettingsBackupFilePath { get; } = Path.Combine( App.DocumentsFolder, "Settings.xml.bak" );
	private static string BackupsFolderPath { get; } = Path.Combine( App.DocumentsFolder, "Backups" );

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

	// On top of the every-save Settings.xml.bak, each save also drops a timestamped copy of the pre-write
	// settings file into the Backups folder - but at most once per this many minutes.
	private const int TimestampedBackupIntervalMinutes = 15;

	// The Backups folder is pruned back to this many timestamped backups after each new one is created.
	private const int MaximumTimestampedBackups = 50;

	// When this app session last created a timestamped backup. Deliberately in memory only (never persisted),
	// so the first save after every app launch always produces a fresh backup.
	private DateTime? _lastTimestampedBackupDateTime = null;

	// Log lines for settings that have changed since the last save, keyed so repeated changes to the same
	// setting (e.g. window position while dragging) collapse to a single latest line instead of flooding
	// the log every frame. Flushed to the log and cleared when the settings file is actually written in Tick.
	private readonly Dictionary<string, string> _changedSettings = [];

	// RecordChangedSetting is reached from the iRacing telemetry thread (OnPropertyChanged calls it ahead of the
	// suppression guard) while Tick drains the dictionary on the UI thread, so every touch of it takes this lock.
	private readonly object _changedSettingsLock = new();

	public void RecordChangedSetting( string key, string message )
	{
		lock ( _changedSettingsLock )
		{
			_changedSettings[ key ] = message;
		}
	}

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SettingsFile] Initialize >>>" );

		PauseSerialization = true;

		Settings.BeginStartupContextSettingsSuppression();

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

					loaded = true;

					app.Logger.WriteLine( "[SettingsFile] Settings restored from backup successfully" );
				}
				catch ( Exception ex )
				{
					app.Logger.WriteLine( $"[SettingsFile] Failed to restore settings from backup: {ex.Message}" );
				}
			}
			else if ( !loaded )
			{
				app.Logger.WriteLine( "[SettingsFile] No Settings.xml.bak available" );
			}

			// Last resort: the every-save backup is gone or just as broken as the settings file itself, so walk
			// the timestamped backups newest first and take the first one that loads. Each attempt follows the
			// same pattern as the Settings.xml.bak recovery above (copy over the settings file, retry the load).
			if ( !loaded )
			{
				var backupFilePaths = GetTimestampedBackupFilePaths();

				foreach ( var backupFilePath in backupFilePaths )
				{
					var backupFileName = Path.GetFileName( backupFilePath );

					app.Logger.WriteLine( $"[SettingsFile] Attempting to restore settings from backup {backupFileName}" );

					try
					{
						File.Copy( backupFilePath, SettingsFilePath, overwrite: true );

						DataContext.DataContext.Instance.Settings = (Settings) Serializer.Load<Settings>( SettingsFilePath );

						loaded = true;

						app.Logger.WriteLine( $"[SettingsFile] Recovered settings from backup {backupFileName}" );

						break;
					}
					catch ( Exception ex )
					{
						app.Logger.WriteLine( $"[SettingsFile] Failed to restore settings from backup {backupFileName}: {ex.Message}" );
					}
				}

				if ( !loaded )
				{
					app.Logger.WriteLine( "[SettingsFile] No usable backup available - starting with default settings" );
				}
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

		// One-time: merge context buckets from before the per-wheelbase context dimension was retired.
		// Buckets that differed only by wheelbase collapse into one, with the currently selected steering
		// device's bucket winning; persisted below so the legacy guids leave the file for good.
		var legacyWheelbaseContextsConsolidated = DataContext.DataContext.Instance.Settings.ConsolidateLegacyWheelbaseContexts();

		// Migrate the old percentage-based auto margin to the new Nm-based auto target (value, scope,
		// and input mappings). Must run before the controller profiles are initialized/applied so the
		// renamed profile mapping keys are in place.
		DataContext.DataContext.Instance.Settings.MigrateAutoMarginToAutoTarget();

		// Enforce the context switch hierarchy (per track requires per car, per track configuration requires
		// per track) on every scope in the settings file. Must run after the auto target migration above -
		// that one hands the new setting a fresh copy of the old setting's context switches.
		var contextSwitchHierarchyNormalized = DataContext.DataContext.Instance.Settings.NormalizeContextSwitchHierarchy();

		// One-time: seed the two renamed pedal RPM settings into every existing context bucket from the live
		// values. Their ContextSettings pairing is new, so an upgrading user's buckets hold the class initializer
		// defaults for them - and the first UpdateSettings( false ) below would stomp the user's live values with
		// those. Must run before anything can trigger that read pass.
		var pedalsRPMContextSettingsMigrated = DataContext.DataContext.Instance.Settings.MigratePedalsRPMContextSettings();

		// Migrate a pre-profiles settings file (snapshot the existing flat mappings into a
		// "Default" controller profile) and guarantee a valid active profile.
		DataContext.DataContext.Instance.Settings.EnsureControllerProfilesInitialized();

		// One-time: seed the non-car overlay layout from the existing top-level overlay position/scale values
		// so users upgrading from a version without per-car overlays keep their current layout.
		DataContext.DataContext.Instance.Settings.MigrateOverlayLayoutToNonCarBaseline();

		// Migrate a pre-graph settings file: turn the old fixed-function algorithm choices (live + per-context)
		// into built-in graph selections. Must run before the built-in sync below so its empty-selection guard
		// sees the pre-migration state (the sync's fallback repair fills the live selection).
		var legacyAlgorithmsMigrated = DataContext.DataContext.Instance.Settings.MigrateLegacyAlgorithmSelections();

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

		// Context-settings updates stay SUPPRESSED here on purpose. The live setting values still hold whatever
		// context was active when the app last closed, while the current context is (for now) the baseline - so
		// any property setter firing during the rest of startup would push those stale values into the baseline
		// buckets via UpdateSettings( true ). The first UpdateSettings( false ) in App.OnStartup re-baselines the
		// live values from the current context and clears the suppression flag itself when it finishes.

		PauseSerialization = false;

		// Persist a launch-time built-in graph sync now that serialization is un-paused (the setter ignores
		// a queue request while paused), so the recorded graph file hashes reach disk and the sync runs once.
		if ( builtInGraphsChanged || graphIdentitiesChanged || legacyWheelbaseContextsConsolidated || legacyAlgorithmsMigrated || contextSwitchHierarchyNormalized || pedalsRPMContextSettingsMigrated )
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
					// Flush the live working-copy mappings into the active controller profile so the
					// persisted store stays authoritative no matter where a mapping was edited.
					DataContext.DataContext.Instance.Settings.SaveCurrentControllerProfile();

					// serialization walks the context settings dictionary, which the telemetry thread grows
					// lazily on every car / session / weather change - take the lock so it cannot mutate
					// underneath the writer. Only the serialization itself runs under the lock: the disk write
					// below can block for a long time (OneDrive), and the telemetry thread must never wait on it.
					byte[] settingsBytes;

					lock ( Settings.ContextSettingsLock )
					{
						settingsBytes = Serializer.SaveToBytes( DataContext.DataContext.Instance.Settings );
					}

					if ( File.Exists( SettingsFilePath ) )
					{
						File.Copy( SettingsFilePath, SettingsBackupFilePath, overwrite: true );

						app.Logger.WriteLine( "[SettingsFile] Settings.xml backup created" );

						CreateTimestampedBackup( app );
					}

					Serializer.SaveBytes( SettingsFilePath, settingsBytes );

					var changedSettings = new List<string>();

					lock ( _changedSettingsLock )
					{
						changedSettings.AddRange( _changedSettings.Values );

						_changedSettings.Clear();
					}

					foreach ( var changedSetting in changedSettings )
					{
						app.Logger.WriteLine( changedSetting );
					}

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

	/// <summary>Copies the current (pre-write) settings file into the Backups folder under a timestamped name and
	/// prunes the folder back to the newest <see cref="MaximumTimestampedBackups"/> backups. Does nothing if this
	/// app session already made a backup within the last <see cref="TimestampedBackupIntervalMinutes"/> minutes.
	/// Entirely best effort - everything here is swallowed and logged as a warning (a OneDrive lock on the folder
	/// being the usual suspect) so a failed backup can never postpone or fail the actual settings save. Called from
	/// Tick outside Settings.ContextSettingsLock, alongside the other file work.</summary>
	private void CreateTimestampedBackup( App app )
	{
		var nowDateTime = DateTime.Now;

		if ( ( _lastTimestampedBackupDateTime != null ) && ( ( nowDateTime - _lastTimestampedBackupDateTime.Value ).TotalMinutes < TimestampedBackupIntervalMinutes ) )
		{
			return;
		}

		try
		{
			Directory.CreateDirectory( BackupsFolderPath );

			var backupFileName = $"Settings {nowDateTime:yyyy-MM-dd} {nowDateTime:HH-mm-ss}.xml";

			File.Copy( SettingsFilePath, Path.Combine( BackupsFolderPath, backupFileName ), overwrite: true );

			// Only a backup that actually reached disk restarts the interval - a failed one is retried next save.
			_lastTimestampedBackupDateTime = nowDateTime;

			app.Logger.WriteLine( $"[SettingsFile] Timestamped backup created ({backupFileName})" );

			var backupFilePaths = GetTimestampedBackupFilePaths();

			foreach ( var backupFilePathToPrune in backupFilePaths.Skip( MaximumTimestampedBackups ) )
			{
				File.Delete( backupFilePathToPrune );

				app.Logger.WriteLine( $"[SettingsFile] Pruned timestamped backup ({Path.GetFileName( backupFilePathToPrune )})" );
			}
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[SettingsFile] Warning - timestamped backup failed: {exception.Message}" );
		}
	}

	/// <summary>Returns the timestamped backups in the Backups folder, newest first. Only files whose name matches
	/// the exact backup pattern are returned, so anything else the user drops in that folder is never pruned or
	/// restored from. The timestamp format makes a descending ordinal file name sort a reverse-chronological sort.</summary>
	private static List<string> GetTimestampedBackupFilePaths()
	{
		if ( !Directory.Exists( BackupsFolderPath ) )
		{
			return [];
		}

		return [ .. Directory.GetFiles( BackupsFolderPath ).Where( backupFilePath => TimestampedBackupFileNameRegex().IsMatch( Path.GetFileName( backupFilePath ) ) ).OrderByDescending( backupFilePath => Path.GetFileName( backupFilePath ), StringComparer.Ordinal ) ];
	}

	[GeneratedRegex( @"^Settings \d{4}-\d{2}-\d{2} \d{2}-\d{2}-\d{2}\.xml$", RegexOptions.Compiled )]
	private static partial Regex TimestampedBackupFileNameRegex();
}
