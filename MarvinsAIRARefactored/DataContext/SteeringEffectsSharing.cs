
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.FFB;

namespace MarvinsAIRARefactored.DataContext;

#region File model

/// <summary>
/// A shareable per-car steering effects file (see <see cref="FileExtension"/>): the steering effects page settings
/// of ONE car, plus - optionally applied on import - the settings of the steering effect nodes of that car's FFB
/// graph. The file type tag and the format version follow the FFB graph file precedent (FFBGraphExportFile): a
/// future release can evolve the format while this one rejects newer files with a clear message. Every value is
/// stored as invariant-culture text and validated one by one on import, so one bad value never sinks the file.
/// </summary>
[XmlRoot( "MairaSteeringEffects" )]
public class SteeringEffectsShareFile
{
	public const int CurrentFormatVersion = 1;

	public const string SteeringEffectsFileType = "SteeringEffects";
	public const string FileExtension = ".mairasteering";

	public int FormatVersion { get; set; } = CurrentFormatVersion;
	public string FileType { get; set; } = string.Empty;
	public string AppVersion { get; set; } = string.Empty;
	public string Author { get; set; } = string.Empty;
	public string CreatedUtc { get; set; } = string.Empty;
	public string GameName { get; set; } = Context.IRacingGameName;
	public string CarName { get; set; } = string.Empty;

	[XmlArrayItem( "Setting" )]
	public List<SteeringEffectsShareSetting> PageSettings { get; set; } = [];

	[XmlArrayItem( "Node" )]
	public List<SteeringEffectsShareNode> GraphNodes { get; set; } = [];
}

/// <summary>One steering effects page setting, keyed by its settings property name.</summary>
public class SteeringEffectsShareSetting
{
	[XmlAttribute]
	public string Name { get; set; } = string.Empty;

	[XmlAttribute]
	public string Value { get; set; } = string.Empty;
}

/// <summary>One steering effect node of the exporter's FFB graph - its module type and its setting values. Nodes
/// are matched onto the importer's graph by type and order, never by module id (ids differ between users).</summary>
public class SteeringEffectsShareNode
{
	[XmlAttribute]
	public string Type { get; set; } = string.Empty;

	[XmlElement( "Value" )]
	public List<SteeringEffectsShareNodeValue> Values { get; set; } = [];
}

public class SteeringEffectsShareNodeValue
{
	[XmlAttribute]
	public string Key { get; set; } = string.Empty;

	[XmlAttribute]
	public string Value { get; set; } = string.Empty;
}

#endregion

#region Import candidates

/// <summary>A validated graph node read from a file: the module type and its (clamped) setting values.</summary>
public sealed class SteeringEffectsShareNodeValues
{
	public required string ModuleType { get; init; }
	public Dictionary<string, float> Values { get; } = new( StringComparer.Ordinal );
}

/// <summary>
/// One file picked for import, read and validated (<see cref="Settings.ReadSteeringEffectsShareFile"/>) but not yet
/// applied. A non-empty <see cref="ErrorMessage"/> means the file is rejected; warnings never block the import.
/// </summary>
public sealed class SteeringEffectsImportCandidate
{
	public required string FilePath { get; init; }

	public string FileName { get => Path.GetFileName( FilePath ); }

	public Context? Car { get; internal set; } = null;
	public string CarLabel { get; internal set; } = string.Empty;
	public string Author { get; internal set; } = string.Empty;

	public string ErrorMessage { get; internal set; } = string.Empty;

	public bool IsValid { get => ErrorMessage.Length == 0; }

	// whether this car already has a car profile of its own (it gets replaced) or the import creates one
	public bool CarHasProfile { get; internal set; }

	public List<string> Warnings { get; } = [];

	// shown only while the "also import the graph nodes" option is on
	public List<string> GraphWarnings { get; } = [];

	public bool HasGraphNodes { get => GraphNodes.Count > 0; }

	internal List<(Settings.SteeringEffectsShareSettingSpec spec, object value)> PageValues { get; } = [];
	internal List<SteeringEffectsShareNodeValues> GraphNodes { get; } = [];
}

#endregion

/// <summary>
/// Per-car sharing of the steering effects settings (the export / import buttons on the steering effects page).
///
/// Export resolves, for each picked car, the value every steering effects page setting has for that car - the
/// car's own profile when the setting is per car, the shared value otherwise (it applies to that car too), and the
/// live value when the car is the one being driven - plus the settings of the steering effect nodes of the car's
/// FFB graph. Import writes the values into the car's own profile: the per-car profile (dry and, when the setting is
/// per wet/dry, wet) and every per-track profile the car already has when the setting is per track. A setting that
/// is not per car on the importing side is switched to per car first, after copying its current shared value into
/// every other car's profile so no other car changes. Profiles are only ever written, never removed (see the tuning
/// profile engine invariants), every bucket access is under <see cref="ContextSettingsLock"/>, and the live settings
/// are refreshed afterwards through an ordinary UpdateSettings( false ) read pass.
/// </summary>
public partial class Settings
{
	#region Setting table

	/// <summary>A steering effects page setting that rides the per-car files, with its value range (floats only).</summary>
	internal sealed class SteeringEffectsShareSettingSpec
	{
		public string PropertyName { get; }
		public float Minimum { get; }
		public float Maximum { get; }

		public PropertyInfo SettingsProperty { get; }
		public PropertyInfo ContextSettingsProperty { get; }
		public PropertyInfo ContextSwitchesProperty { get; }

		public SteeringEffectsShareSettingSpec( string propertyName, float minimum = 0f, float maximum = 0f )
		{
			PropertyName = propertyName;
			Minimum = minimum;
			Maximum = maximum;

			SettingsProperty = typeof( Settings ).GetProperty( propertyName ) ?? throw new InvalidOperationException( $"[Settings] Unknown steering effects setting {propertyName}" );
			ContextSettingsProperty = typeof( ContextSettings ).GetProperty( propertyName ) ?? throw new InvalidOperationException( $"[Settings] Steering effects setting {propertyName} is not context scoped" );
			ContextSwitchesProperty = typeof( Settings ).GetProperty( $"{propertyName}ContextSwitches" ) ?? throw new InvalidOperationException( $"[Settings] Steering effects setting {propertyName} has no context switches" );
		}
	}

	// The settings on the steering effects page, in page order. The ranges mirror the clamps in each property setter.
	// The property names are the file's setting names - they are persisted settings names already, so they are stable.
	private static SteeringEffectsShareSettingSpec[]? _steeringEffectsShareSettingSpecs = null;

	internal static SteeringEffectsShareSettingSpec[] SteeringEffectsShareSettingSpecs
	{
		get
		{
			_steeringEffectsShareSettingSpecs ??=
			[
				new( nameof( SteeringEffectsUndersteerEnabled ) ),
				new( nameof( SteeringEffectsUndersteerMinimumThreshold ), 0f, 1f ),
				new( nameof( SteeringEffectsUndersteerMaximumThreshold ), 0f, 1f ),
				new( nameof( SteeringEffectsUndersteerPedalVibrationMinimumFrequency ), 0f, 1f ),
				new( nameof( SteeringEffectsUndersteerPedalVibrationMaximumFrequency ), 0f, 1f ),
				new( nameof( SteeringEffectsUndersteerPedalVibrationCurve ), -1f, 1f ),
				new( nameof( SteeringEffectsOversteerEnabled ) ),
				new( nameof( SteeringEffectsOversteerMinimumThreshold ), -1f, 2f ),
				new( nameof( SteeringEffectsOversteerMaximumThreshold ), -1f, 2f ),
				new( nameof( SteeringEffectsOversteerPedalVibrationMinimumFrequency ), 0f, 1f ),
				new( nameof( SteeringEffectsOversteerPedalVibrationMaximumFrequency ), 0f, 1f ),
				new( nameof( SteeringEffectsOversteerPedalVibrationCurve ), -1f, 1f ),
				new( nameof( SteeringEffectsSeatOfPantsEnabled ) ),
				new( nameof( SteeringEffectsSeatOfPantsMinimumThreshold ), 0f, 50f ),
				new( nameof( SteeringEffectsSeatOfPantsMaximumThreshold ), 0f, 50f ),
				new( nameof( SteeringEffectsSeatOfPantsAlgorithm ) ),
				new( nameof( SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequency ), 0f, 1f ),
				new( nameof( SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequency ), 0f, 1f ),
				new( nameof( SteeringEffectsSeatOfPantsPedalVibrationCurve ), -1f, 1f )
			];

			return _steeringEffectsShareSettingSpecs;
		}
	}

	// The FFB graph node types that carry the steering effects, in page order.
	private static readonly string[] SteeringEffectsShareNodeTypes =
	[
		FFBModuleRegistry.UndersteerForceType,
		FFBModuleRegistry.UndersteerVibrationType,
		FFBModuleRegistry.OversteerForceType,
		FFBModuleRegistry.OversteerVibrationType,
		FFBModuleRegistry.SeatOfPantsForceType,
		FFBModuleRegistry.SeatOfPantsVibrationType
	];

	#endregion

	#region Cars

	/// <summary>The cars that can be exported: every car with a profile of its own, plus the car being driven. A car
	/// is identified by its game and name (bridged games' cars can share a name with an iRacing car).</summary>
	public List<Context> GetSteeringEffectsShareCars()
	{
		var cars = new List<Context>();

		void AddCar( string gameName, string carName )
		{
			if ( !cars.Any( car => ( car.GameName == gameName ) && ( car.CarName == carName ) ) )
			{
				cars.Add( new Context { GameName = gameName, CarName = carName } );
			}
		}

		lock ( ContextSettingsLock )
		{
			foreach ( var context in ContextSettingsDictionary.Keys )
			{
				if ( context.CarName != Context.DefaultContextName )
				{
					AddCar( context.GameName, context.CarName );
				}
			}
		}

		var liveCar = GetLiveSteeringEffectsShareCar();

		if ( liveCar != null )
		{
			AddCar( liveCar.GameName, liveCar.CarName );
		}

		return [ .. cars.OrderBy( car => DescribeSteeringEffectsShareCar( car ), StringComparer.CurrentCultureIgnoreCase ) ];
	}

	/// <summary>The car being driven right now, or null when the sim is not running / has not named the car yet.</summary>
	public Context? GetLiveSteeringEffectsShareCar()
	{
		var simulator = App.Instance!.Simulator;

		if ( !simulator.IsConnected || ( simulator.CarScreenName == string.Empty ) )
		{
			return null;
		}

		return new Context { GameName = simulator.ContextGameName, CarName = simulator.CarScreenName };
	}

	private bool IsLiveSteeringEffectsShareCar( Context car )
	{
		var liveCar = GetLiveSteeringEffectsShareCar();

		return ( liveCar != null ) && ( liveCar.GameName == car.GameName ) && ( liveCar.CarName == car.CarName );
	}

	/// <summary>"Ferrari 296 GT3", or "BMW M4 GT3 · Le Mans Ultimate" for another game's car.</summary>
	public static string DescribeSteeringEffectsShareCar( Context car )
	{
		return car.IsIRacing ? car.CarName : WithGameName( car.CarName, car.GameName );
	}

	/// <summary>The suggested file name for a car's file - "Ferrari 296 GT3 - Marvin.mairasteering", with the game in
	/// parentheses for another game's car and the author part left out when no name is set. Characters a file name
	/// cannot hold are dropped.</summary>
	public static string BuildSteeringEffectsShareFileName( Context car, string author )
	{
		var fileName = car.IsIRacing ? car.CarName : $"{car.CarName} ({GameLabelFor( car.GameName )})";

		author = author.Trim();

		if ( author != string.Empty )
		{
			fileName = $"{fileName} - {author}";
		}

		fileName = string.Concat( fileName.Split( Path.GetInvalidFileNameChars() ) ).Trim().TrimEnd( '.' );

		if ( fileName == string.Empty )
		{
			fileName = "Steering effects";
		}

		return fileName + SteeringEffectsShareFile.FileExtension;
	}

	#endregion

	#region Context keys

	// The bucket key a setting with the given scope uses for this car on no particular track. Like Context( switches ),
	// the game only qualifies the key when it names a car.
	private static Context SteeringEffectsShareKey( bool perCar, bool perWetDry, Context car, string wetDryName )
	{
		var key = new Context
		{
			CarName = perCar ? car.CarName : Context.DefaultContextName,
			WetDryName = perWetDry ? wetDryName : Context.DryContextName
		};

		if ( perCar )
		{
			key.GameName = car.GameName;
		}

		return key;
	}

	// The buckets an imported value is written into for a setting with this (per car) scope: the car's per-car
	// profile (dry, and wet when per wet/dry), plus every per-track profile the car already has when per track - so the
	// value is in effect wherever the car is driven. Materialized up front because creating buckets grows the dictionary.
	private List<Context> GetSteeringEffectsShareTargetKeys( ContextSwitches contextSwitches, Context car )
	{
		var keys = new List<Context> { SteeringEffectsShareKey( true, contextSwitches.PerWetDry, car, Context.DryContextName ) };

		if ( contextSwitches.PerWetDry )
		{
			keys.Add( SteeringEffectsShareKey( true, true, car, Context.WetContextName ) );
		}

		if ( contextSwitches.PerTrack )
		{
			foreach ( var context in ContextSettingsDictionary.Keys )
			{
				if ( ( context.GameName == car.GameName ) && ( context.CarName == car.CarName ) && ( context.TrackName != Context.DefaultContextName ) &&
					( contextSwitches.PerTrackConfiguration || ( context.TrackConfigurationName == Context.DefaultContextName ) ) &&
					( contextSwitches.PerWetDry || ( context.WetDryName == Context.DryContextName ) ) )
				{
					keys.Add( context );
				}
			}
		}

		return keys;
	}

	// The bucket for a key, created through the normal first-visit path (FindContextSettings) when the car has none yet.
	private ContextSettings GetOrCreateSteeringEffectsShareBucket( Context key )
	{
		return ContextSettingsDictionary.TryGetValue( key, out var contextSettings ) ? contextSettings : FindContextSettings( key );
	}

	// A car-only bucket key in the shape of a per car scope (no track, dry unless the scope is per wet/dry).
	private static bool IsSteeringEffectsShareCarKey( Context context, bool perWetDry )
	{
		return ( context.CarName != Context.DefaultContextName ) && ( context.TrackName == Context.DefaultContextName ) && ( context.TrackConfigurationName == Context.DefaultContextName ) &&
			( perWetDry || ( context.WetDryName == Context.DryContextName ) );
	}

	#endregion

	#region Export

	/// <summary>Builds the file content for one car. Values come from the car's profile buckets (never creating one);
	/// the car being driven exports its live values.</summary>
	public SteeringEffectsShareFile BuildSteeringEffectsShareFile( Context car, string author )
	{
		var shareFile = new SteeringEffectsShareFile
		{
			FileType = SteeringEffectsShareFile.SteeringEffectsFileType,
			AppVersion = Misc.GetVersion(),
			Author = author.Trim(),
			CreatedUtc = DateTime.UtcNow.ToString( "o", CultureInfo.InvariantCulture ),
			GameName = car.GameName,
			CarName = car.CarName
		};

		var isLiveCar = IsLiveSteeringEffectsShareCar( car );

		lock ( ContextSettingsLock )
		{
			var baselineBucket = ContextSettingsDictionary.GetValueOrDefault( new Context() );

			foreach ( var spec in SteeringEffectsShareSettingSpecs )
			{
				object? value;

				if ( isLiveCar )
				{
					value = spec.SettingsProperty.GetValue( this );
				}
				else
				{
					var contextSwitches = (ContextSwitches) spec.ContextSwitchesProperty.GetValue( this )!;

					var key = SteeringEffectsShareKey( contextSwitches.PerCar, contextSwitches.PerWetDry, car, Context.DryContextName );

					var bucket = ContextSettingsDictionary.GetValueOrDefault( key ) ?? baselineBucket;

					value = ( bucket != null ) ? spec.ContextSettingsProperty.GetValue( bucket ) : spec.SettingsProperty.GetValue( this );
				}

				if ( value != null )
				{
					shareFile.PageSettings.Add( new SteeringEffectsShareSetting { Name = spec.PropertyName, Value = FormatSteeringEffectsShareValue( value ) } );
				}
			}

			AddSteeringEffectsShareGraphNodes( shareFile, car, isLiveCar, baselineBucket );
		}

		return shareFile;
	}

	// The steering effect nodes of the car's FFB graph. The live car reads the selected graph's in-memory values (they
	// ARE the live context's values); any other car resolves its graph selection and values from its profile, falling
	// back to the default profile, then the graph's own value, then the setting's default for a key it has never stored.
	private void AddSteeringEffectsShareGraphNodes( SteeringEffectsShareFile shareFile, Context car, bool isLiveCar, ContextSettings? baselineBucket )
	{
		ContextSettings? carBucket = null;

		var graphName = RacingWheelSelectedFFBGraphName;

		if ( !isLiveCar )
		{
			var graphContextSwitches = RacingWheelSelectedFFBGraphNameContextSwitches;

			carBucket = ContextSettingsDictionary.GetValueOrDefault( SteeringEffectsShareKey( graphContextSwitches.PerCar, graphContextSwitches.PerWetDry, car, Context.DryContextName ) ) ?? baselineBucket;

			if ( carBucket != null )
			{
				graphName = carBucket.RacingWheelSelectedFFBGraphName;
			}
		}

		if ( !RacingWheelFFBGraphs.TryGetValue( graphName, out var graph ) )
		{
			return;
		}

		foreach ( var moduleType in SteeringEffectsShareNodeTypes )
		{
			var descriptor = FFBModuleRegistry.TryGet( moduleType );

			if ( descriptor == null )
			{
				continue;
			}

			foreach ( var module in graph.Modules )
			{
				if ( module.ModuleType != moduleType )
				{
					continue;
				}

				var shareNode = new SteeringEffectsShareNode { Type = moduleType };

				foreach ( var settingDescriptor in descriptor.EffectiveSettings )
				{
					var compositeKey = FFBGraphValues.ComposeKey( graph.GraphId, module.ModuleId, settingDescriptor.Key );

					float value;

					if ( !isLiveCar && ( carBucket != null ) && carBucket.RacingWheelFFBGraphModuleValues.TryGetValue( compositeKey, out var carValue ) )
					{
						value = carValue;
					}
					else if ( !isLiveCar && ( baselineBucket != null ) && baselineBucket.RacingWheelFFBGraphModuleValues.TryGetValue( compositeKey, out var baselineValue ) )
					{
						value = baselineValue;
					}
					else if ( module.SettingValues.TryGetValue( settingDescriptor.Key, out var moduleValue ) )
					{
						value = moduleValue;
					}
					else
					{
						value = settingDescriptor.DefaultValue;
					}

					shareNode.Values.Add( new SteeringEffectsShareNodeValue { Key = settingDescriptor.Key, Value = value.ToString( "R", CultureInfo.InvariantCulture ) } );
				}

				shareFile.GraphNodes.Add( shareNode );
			}
		}
	}

	/// <summary>Writes one file per car into the folder and returns the written paths. A write failure stops the
	/// export and propagates (the page reports it).</summary>
	public List<string> ExportSteeringEffects( IReadOnlyList<Context> cars, string folderPath, string author )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[Settings] ExportSteeringEffects >>> ({cars.Count} cars to {folderPath})" );

		// persist any in-flight edits into the profiles first (never during the startup window - see the startup
		// suppression invariant)
		if ( !SuppressUpdatingOfContextSettings )
		{
			UpdateSettings( true );
		}

		var writtenPaths = new List<string>();

		foreach ( var car in cars )
		{
			var shareFile = BuildSteeringEffectsShareFile( car, author );

			var filePath = Path.Combine( folderPath, BuildSteeringEffectsShareFileName( car, author ) );

			// two car names that only differ by characters a file name cannot hold would land on the same file -
			// number the later one instead of overwriting the earlier one
			for ( var suffix = 2; writtenPaths.Contains( filePath, StringComparer.OrdinalIgnoreCase ); suffix++ )
			{
				var fileName = BuildSteeringEffectsShareFileName( car, author );

				filePath = Path.Combine( folderPath, $"{Path.GetFileNameWithoutExtension( fileName )} ({suffix}){SteeringEffectsShareFile.FileExtension}" );
			}

			Serializer.Save( filePath, shareFile );

			writtenPaths.Add( filePath );
		}

		app.Logger.WriteLine( "[Settings] <<< ExportSteeringEffects" );

		return writtenPaths;
	}

	private static string FormatSteeringEffectsShareValue( object value )
	{
		return value switch
		{
			float floatValue => floatValue.ToString( "R", CultureInfo.InvariantCulture ),
			bool boolValue => boolValue ? "True" : "False",
			_ => value.ToString() ?? string.Empty
		};
	}

	#endregion

	#region Import

	/// <summary>
	/// Reads and validates one file. Never throws: anything that makes the file unusable (not a steering effects
	/// file, a newer format, no car, nothing usable in it) becomes the candidate's error message; unknown or unreadable
	/// values are skipped and out-of-range values are limited, each reported as a warning.
	/// </summary>
	public SteeringEffectsImportCandidate ReadSteeringEffectsShareFile( string filePath )
	{
		var app = App.Instance!;
		var localization = DataContext.Instance.Localization;

		app.Logger.WriteLine( $"[Settings] ReadSteeringEffectsShareFile >>> ({filePath})" );

		var candidate = new SteeringEffectsImportCandidate { FilePath = filePath };

		SteeringEffectsShareFile? shareFile = null;

		try
		{
			var xmlSerializer = new XmlSerializer( typeof( SteeringEffectsShareFile ) );

			using var fileStream = File.OpenRead( filePath );
			using var xmlReader = XmlReader.Create( fileStream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null } );

			shareFile = xmlSerializer.Deserialize( xmlReader ) as SteeringEffectsShareFile;
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[Settings] Could not read steering effects file: {exception.Message}" );
		}

		if ( ( shareFile == null ) || ( shareFile.FileType != SteeringEffectsShareFile.SteeringEffectsFileType ) )
		{
			candidate.ErrorMessage = localization[ "SteeringEffectsImportInvalidFile" ];
		}
		else if ( shareFile.FormatVersion > SteeringEffectsShareFile.CurrentFormatVersion )
		{
			candidate.ErrorMessage = localization[ "SteeringEffectsImportNewerVersion" ];
		}
		else if ( string.IsNullOrWhiteSpace( shareFile.CarName ) || ( shareFile.CarName.Trim() == Context.DefaultContextName ) )
		{
			candidate.ErrorMessage = localization[ "SteeringEffectsImportNoCar" ];
		}
		else
		{
			var gameName = string.IsNullOrWhiteSpace( shareFile.GameName ) ? Context.IRacingGameName : shareFile.GameName.Trim();

			candidate.Car = new Context { GameName = gameName, CarName = shareFile.CarName.Trim() };
			candidate.CarLabel = DescribeSteeringEffectsShareCar( candidate.Car );
			candidate.Author = shareFile.Author?.Trim() ?? string.Empty;

			ValidateSteeringEffectsShareValues( shareFile, candidate );

			if ( ( candidate.PageValues.Count == 0 ) && ( candidate.GraphNodes.Count == 0 ) )
			{
				candidate.ErrorMessage = localization[ "SteeringEffectsImportEmpty" ];
			}
		}

		app.Logger.WriteLine( $"[Settings] <<< ReadSteeringEffectsShareFile ({( candidate.IsValid ? $"{candidate.PageValues.Count} settings, {candidate.GraphNodes.Count} graph nodes" : candidate.ErrorMessage )})" );

		return candidate;
	}

	private static void ValidateSteeringEffectsShareValues( SteeringEffectsShareFile shareFile, SteeringEffectsImportCandidate candidate )
	{
		var localization = DataContext.Instance.Localization;

		var ignoredCount = 0;
		var clampedCount = 0;

		foreach ( var shareSetting in shareFile.PageSettings ?? [] )
		{
			var spec = SteeringEffectsShareSettingSpecs.FirstOrDefault( settingSpec => settingSpec.PropertyName == shareSetting.Name );

			if ( ( spec == null ) || !TryParseSteeringEffectsShareValue( spec, shareSetting.Value ?? string.Empty, out var value, out var clamped ) )
			{
				ignoredCount++;

				continue;
			}

			if ( clamped )
			{
				clampedCount++;
			}

			// a repeated setting - the last one wins, like a hand-edited file would expect
			candidate.PageValues.RemoveAll( pageValue => pageValue.spec == spec );
			candidate.PageValues.Add( (spec, value) );
		}

		foreach ( var shareNode in shareFile.GraphNodes ?? [] )
		{
			var descriptor = SteeringEffectsShareNodeTypes.Contains( shareNode.Type ) ? FFBModuleRegistry.TryGet( shareNode.Type ) : null;

			if ( descriptor == null )
			{
				ignoredCount += Math.Max( 1, shareNode.Values?.Count ?? 0 );

				continue;
			}

			var nodeValues = new SteeringEffectsShareNodeValues { ModuleType = shareNode.Type };

			foreach ( var shareNodeValue in shareNode.Values ?? [] )
			{
				var settingDescriptor = descriptor.EffectiveSettings.FirstOrDefault( effectiveSetting => effectiveSetting.Key == shareNodeValue.Key );

				if ( ( settingDescriptor == null ) || !float.TryParse( shareNodeValue.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) || !float.IsFinite( value ) )
				{
					ignoredCount++;

					continue;
				}

				// switches and choices store whole numbers
				var limitedValue = settingDescriptor.Clamp( ( settingDescriptor.Type == FFBSettingType.Knob ) ? value : MathF.Round( value ) );

				if ( limitedValue != value )
				{
					clampedCount++;
				}

				nodeValues.Values[ settingDescriptor.Key ] = limitedValue;
			}

			if ( nodeValues.Values.Count > 0 )
			{
				candidate.GraphNodes.Add( nodeValues );
			}
		}

		if ( ignoredCount > 0 )
		{
			candidate.Warnings.Add( string.Format( localization[ "SteeringEffectsImportIgnoredValues" ], ignoredCount ) );
		}

		if ( clampedCount > 0 )
		{
			candidate.Warnings.Add( string.Format( localization[ "SteeringEffectsImportClampedValues" ], clampedCount ) );
		}
	}

	private static bool TryParseSteeringEffectsShareValue( SteeringEffectsShareSettingSpec spec, string text, out object value, out bool clamped )
	{
		var propertyType = spec.SettingsProperty.PropertyType;

		value = false;
		clamped = false;

		text = text.Trim();

		if ( propertyType == typeof( bool ) )
		{
			if ( bool.TryParse( text, out var boolValue ) )
			{
				value = boolValue;

				return true;
			}
		}
		else if ( propertyType == typeof( float ) )
		{
			if ( float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue ) && float.IsFinite( floatValue ) )
			{
				var limitedValue = Math.Clamp( floatValue, spec.Minimum, spec.Maximum );

				clamped = limitedValue != floatValue;
				value = limitedValue;

				return true;
			}
		}
		else if ( propertyType.IsEnum )
		{
			// names only - a bare number would parse too, so it has to name a defined value
			if ( Enum.TryParse( propertyType, text, ignoreCase: false, out var enumValue ) && ( enumValue != null ) && Enum.IsDefined( propertyType, enumValue ) && !int.TryParse( text, out _ ) )
			{
				value = enumValue;

				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Prepares the read candidates for the import summary: marks every valid file whose car another LATER file also
	/// targets as rejected (the last one wins), notes whether each car already has a profile of its own, and lists the
	/// graph nodes the car's FFB graph lacks (shown only while the graph option is on). Reads buckets, creates none.
	/// </summary>
	public void PrepareSteeringEffectsImportCandidates( IReadOnlyList<SteeringEffectsImportCandidate> candidates )
	{
		var localization = DataContext.Instance.Localization;

		for ( var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++ )
		{
			var candidate = candidates[ candidateIndex ];

			if ( !candidate.IsValid || ( candidate.Car == null ) )
			{
				continue;
			}

			var hasLaterFileForSameCar = candidates.Skip( candidateIndex + 1 ).Any( laterCandidate => laterCandidate.IsValid && ( laterCandidate.Car != null ) && ( laterCandidate.Car.CompareTo( candidate.Car ) == 0 ) );

			if ( hasLaterFileForSameCar )
			{
				candidate.ErrorMessage = localization[ "SteeringEffectsImportDuplicateCar" ];
			}
		}

		lock ( ContextSettingsLock )
		{
			var graphContextSwitches = RacingWheelSelectedFFBGraphNameContextSwitches;

			foreach ( var candidate in candidates )
			{
				if ( !candidate.IsValid || ( candidate.Car == null ) )
				{
					continue;
				}

				candidate.CarHasProfile = ContextSettingsDictionary.ContainsKey( SteeringEffectsShareKey( true, false, candidate.Car, Context.DryContextName ) );

				candidate.GraphWarnings.Clear();

				if ( !candidate.HasGraphNodes )
				{
					continue;
				}

				// the graph the import will land on - the car's own selection when it has a profile, otherwise the
				// current selection (a new profile starts from the current values)
				var graphName = RacingWheelSelectedFFBGraphName;

				if ( graphContextSwitches.PerCar && ContextSettingsDictionary.TryGetValue( SteeringEffectsShareKey( true, graphContextSwitches.PerWetDry, candidate.Car, Context.DryContextName ), out var carBucket ) )
				{
					graphName = carBucket.RacingWheelSelectedFFBGraphName;
				}

				RacingWheelFFBGraphs.TryGetValue( graphName, out var graph );

				var missingNodeNames = new List<string>();

				foreach ( var moduleType in SteeringEffectsShareNodeTypes )
				{
					if ( candidate.GraphNodes.Any( graphNode => graphNode.ModuleType == moduleType ) && ( ( graph == null ) || !graph.Modules.Any( module => module.ModuleType == moduleType ) ) )
					{
						var descriptor = FFBModuleRegistry.TryGet( moduleType );

						missingNodeNames.Add( ( descriptor != null ) ? localization[ descriptor.LocalizationKey ] : moduleType );
					}
				}

				if ( missingNodeNames.Count > 0 )
				{
					candidate.GraphWarnings.Add( string.Format( localization[ "SteeringEffectsImportMissingNodes" ], string.Join( ", ", missingNodeNames ) ) );
				}
			}
		}
	}

	/// <summary>The settings the import will switch to per car (they are shared by every car right now), by name.</summary>
	public List<string> DescribeSteeringEffectsImportScopeChanges( IReadOnlyList<SteeringEffectsImportCandidate> candidates, bool includeGraphNodes )
	{
		var localization = DataContext.Instance.Localization;

		var validCandidates = candidates.Where( candidate => candidate.IsValid ).ToList();

		var labels = new List<string>();

		foreach ( var spec in SteeringEffectsShareSettingSpecs )
		{
			if ( !validCandidates.Any( candidate => candidate.PageValues.Any( pageValue => pageValue.spec == spec ) ) )
			{
				continue;
			}

			var contextSwitches = (ContextSwitches) spec.ContextSwitchesProperty.GetValue( this )!;

			if ( contextSwitches.PerCar )
			{
				continue;
			}

			var binding = BuildBindings().FirstOrDefault( tuningProfileBinding => tuningProfileBinding.PropertyBaseName == spec.PropertyName );

			if ( binding != null )
			{
				var ( groupLabel, subGroupLabel, label ) = DescribeTuningProfileSetting( binding );

				labels.Add( string.Join( " - ", new[] { groupLabel, subGroupLabel, label }.Where( labelPart => labelPart.Length > 0 ) ) );
			}
			else
			{
				labels.Add( spec.PropertyName );
			}
		}

		if ( includeGraphNodes && validCandidates.Any( candidate => candidate.HasGraphNodes ) && !RacingWheelSelectedFFBGraphNameContextSwitches.PerCar )
		{
			labels.Add( localization[ "FFBGraph" ] );
		}

		return labels;
	}

	/// <summary>Applies the valid candidates (see the class summary for where the values land).</summary>
	public void ImportSteeringEffects( IReadOnlyList<SteeringEffectsImportCandidate> candidates, bool includeGraphNodes )
	{
		var app = App.Instance!;

		var validCandidates = candidates.Where( candidate => candidate.IsValid && ( candidate.Car != null ) ).ToList();

		app.Logger.WriteLine( $"[Settings] ImportSteeringEffects >>> ({validCandidates.Count} files, graph nodes {( includeGraphNodes ? "included" : "skipped" )})" );

		// persist in-flight edits into the profiles first, so the shared values copied below are the ones on screen
		// (never during the startup window - see the startup suppression invariant)
		if ( !SuppressUpdatingOfContextSettings )
		{
			UpdateSettings( true );
		}

		var importGraphNodes = includeGraphNodes && validCandidates.Any( candidate => candidate.HasGraphNodes );

		lock ( ContextSettingsLock )
		{
			// first switch every imported setting that is shared by all cars over to per car - otherwise the value
			// written into the car's profile would never be read
			foreach ( var spec in SteeringEffectsShareSettingSpecs )
			{
				if ( !validCandidates.Any( candidate => candidate.PageValues.Any( pageValue => pageValue.spec == spec ) ) )
				{
					continue;
				}

				var contextSwitches = (ContextSwitches) spec.ContextSwitchesProperty.GetValue( this )!;

				if ( !contextSwitches.PerCar )
				{
					PromoteSteeringEffectsSettingToPerCar( spec.ContextSettingsProperty, contextSwitches );
				}
			}

			var graphContextSwitches = RacingWheelSelectedFFBGraphNameContextSwitches;

			if ( importGraphNodes && !graphContextSwitches.PerCar )
			{
				PromoteSteeringEffectsGraphToPerCar( graphContextSwitches );
			}

			foreach ( var candidate in validCandidates )
			{
				var car = candidate.Car!;

				app.Logger.WriteLine( $"[Settings] Importing steering effects for {car.GameName}|{car.CarName} from {candidate.FileName}" );

				foreach ( var ( spec, value ) in candidate.PageValues )
				{
					var contextSwitches = (ContextSwitches) spec.ContextSwitchesProperty.GetValue( this )!;

					foreach ( var key in GetSteeringEffectsShareTargetKeys( contextSwitches, car ) )
					{
						spec.ContextSettingsProperty.SetValue( GetOrCreateSteeringEffectsShareBucket( key ), value );
					}
				}

				if ( importGraphNodes && candidate.HasGraphNodes )
				{
					foreach ( var key in GetSteeringEffectsShareTargetKeys( graphContextSwitches, car ) )
					{
						WriteSteeringEffectsShareGraphNodes( candidate.GraphNodes, GetOrCreateSteeringEffectsShareBucket( key ) );
					}
				}
			}
		}

		// an ordinary read pass loads the imported values when they landed on the car being driven (and rebuilds the
		// FFB engine and graph editor); for any other car it changes nothing live
		UpdateSettings( false );

		app.SettingsFile.QueueForSerialization = true;

		app.Logger.WriteLine( "[Settings] <<< ImportSteeringEffects" );
	}

	// Switches a setting that every car shares over to per car WITHOUT changing any car: the shared value is copied
	// into every car profile in the new shape first (the profiles keep a stale copy of whatever was live when they
	// were created). A car with no profile yet gets the live value - the shared value - when it is first visited.
	private void PromoteSteeringEffectsSettingToPerCar( PropertyInfo contextSettingsProperty, ContextSwitches contextSwitches )
	{
		App.Instance!.Logger.WriteLine( $"[Settings] Switching {contextSettingsProperty.Name} to per car for the steering effects import" );

		foreach ( var ( context, contextSettings ) in ContextSettingsDictionary )
		{
			if ( !IsSteeringEffectsShareCarKey( context, contextSwitches.PerWetDry ) )
			{
				continue;
			}

			if ( ContextSettingsDictionary.TryGetValue( SteeringEffectsShareKey( false, contextSwitches.PerWetDry, context, context.WetDryName ), out var sharedContextSettings ) )
			{
				contextSettingsProperty.SetValue( contextSettings, contextSettingsProperty.GetValue( sharedContextSettings ) );
			}
		}

		contextSwitches.PerCar = true;
	}

	// The FFB graph's version of the above: the graph selection and every module value ride the graph-selection scope,
	// so both are copied (module values are written, never removed - a missing key would leak another context's value).
	private void PromoteSteeringEffectsGraphToPerCar( ContextSwitches graphContextSwitches )
	{
		App.Instance!.Logger.WriteLine( "[Settings] Switching the FFB graph selection to per car for the steering effects import" );

		foreach ( var ( context, contextSettings ) in ContextSettingsDictionary )
		{
			if ( !IsSteeringEffectsShareCarKey( context, graphContextSwitches.PerWetDry ) )
			{
				continue;
			}

			if ( ContextSettingsDictionary.TryGetValue( SteeringEffectsShareKey( false, graphContextSwitches.PerWetDry, context, context.WetDryName ), out var sharedContextSettings ) )
			{
				contextSettings.RacingWheelSelectedFFBGraphName = sharedContextSettings.RacingWheelSelectedFFBGraphName;

				foreach ( var ( compositeKey, value ) in sharedContextSettings.RacingWheelFFBGraphModuleValues )
				{
					contextSettings.RacingWheelFFBGraphModuleValues[ compositeKey ] = value;
				}
			}
		}

		graphContextSwitches.PerCar = true;
	}

	// Writes the file's nodes into a profile, onto the graph that profile has selected: matched by module type, then
	// by order among the nodes of that type. A node type the graph lacks is skipped (the summary warned about it).
	private void WriteSteeringEffectsShareGraphNodes( List<SteeringEffectsShareNodeValues> graphNodes, ContextSettings contextSettings )
	{
		if ( !RacingWheelFFBGraphs.TryGetValue( contextSettings.RacingWheelSelectedFFBGraphName, out var graph ) )
		{
			return;
		}

		foreach ( var moduleType in SteeringEffectsShareNodeTypes )
		{
			var descriptor = FFBModuleRegistry.TryGet( moduleType );

			if ( descriptor == null )
			{
				continue;
			}

			var fileNodes = graphNodes.Where( graphNode => graphNode.ModuleType == moduleType ).ToList();
			var graphModules = graph.Modules.Where( module => module.ModuleType == moduleType ).ToList();

			for ( var nodeIndex = 0; nodeIndex < Math.Min( fileNodes.Count, graphModules.Count ); nodeIndex++ )
			{
				var graphModule = graphModules[ nodeIndex ];

				foreach ( var ( settingKey, value ) in fileNodes[ nodeIndex ].Values )
				{
					contextSettings.RacingWheelFFBGraphModuleValues[ FFBGraphValues.ComposeKey( graph.GraphId, graphModule.ModuleId, settingKey ) ] = value;

					// the per-context sync only carries the keys a module holds - give it the key, at the default the
					// engine already uses for a missing one, so the imported value is picked up
					if ( !graphModule.SettingValues.ContainsKey( settingKey ) )
					{
						var settingDescriptor = descriptor.EffectiveSettings.FirstOrDefault( effectiveSetting => effectiveSetting.Key == settingKey );

						if ( settingDescriptor != null )
						{
							graphModule.SettingValues[ settingKey ] = settingDescriptor.DefaultValue;
						}
					}
				}
			}
		}
	}

	#endregion
}
