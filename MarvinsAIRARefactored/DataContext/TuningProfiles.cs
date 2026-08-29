
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.FFB;

namespace MarvinsAIRARefactored.DataContext;

// Which context dimensions a tuning profile owns. The dimensions are hierarchical (a track scope only means
// something inside a car scope, a track configuration scope only inside a track scope), so the legal car/track/
// configuration combinations collapse onto this single ordered value; the wet/dry dimension is independent of it.
public enum TuningProfileDims
{
	None,
	Car,
	CarTrack,
	CarTrackConfig
}

// The weather badge of a logical profile: Any when its shape does not own the wet/dry dimension at all.
public enum TuningProfileWeather
{
	Any,
	Dry,
	Wet
}

// The scope a setting (or a whole logical profile) is tuned at. This is the normalized form of ContextSwitches:
// the 16 raw switch combinations reduce to the 8 legal shapes.
public readonly record struct TuningProfileShape( TuningProfileDims Dims, bool PerWetDry )
{
	// Assumes the switches are normalized (SettingsFile does that at load), so the deepest enabled dimension
	// alone determines the shape.
	public static TuningProfileShape From( ContextSwitches contextSwitches )
	{
		var dims = TuningProfileDims.None;

		if ( contextSwitches.PerTrackConfiguration )
		{
			dims = TuningProfileDims.CarTrackConfig;
		}
		else if ( contextSwitches.PerTrack )
		{
			dims = TuningProfileDims.CarTrack;
		}
		else if ( contextSwitches.PerCar )
		{
			dims = TuningProfileDims.Car;
		}

		return new TuningProfileShape( dims, contextSwitches.PerWetDry );
	}

	public ContextSwitches ToSwitches()
	{
		return new ContextSwitches( Dims >= TuningProfileDims.Car, Dims >= TuningProfileDims.CarTrack, Dims >= TuningProfileDims.CarTrackConfig, PerWetDry );
	}

	public bool IsDefault { get => ( Dims == TuningProfileDims.None ) && !PerWetDry; }
}

// One context-scoped setting: the Settings property, the ContextSettings property holding its per-context value,
// and the ContextSwitches property saying what scope it is currently tuned at. Only PropertyInfos are kept -
// never a ContextSwitches instance, since the load-time migrations hand out fresh instances.
internal sealed class TuningProfileBinding
{
	public required string PropertyBaseName { get; init; }
	public required PropertyInfo SettingsProperty { get; init; }
	public required PropertyInfo ContextSettingsProperty { get; init; }
	public required PropertyInfo ContextSwitchesProperty { get; init; }
	public required bool IsRetired { get; init; }

	// The setting's own "Format{Name}String( T? )" display string builder, when it has one - the same one the
	// settings pages show, so a bucket value renders as "25%", "28 Nm" or "Ratio of velocities (Vy/Vx)" instead of
	// a raw number or a humanized enum name. Null for the bools and names that have no formatter of their own (the
	// generic path already renders those).
	public required MethodInfo? DisplayStringFormatter { get; init; }
}

// One line of a profile's diff: what changed, what it is now, and what it would be without this profile. The name
// is carried in the three levels the manager prints it in rather than as one composed string (a composed label just
// ellipsized away the part that identifies the setting): the section (a settings page category, or the graph for an
// FFB row), the group inside that section (the page section the control sits in, or the module), and the setting's
// own label. The middle level is empty for a setting that has no group of its own - the row then sits directly
// under its section.
public abstract class TuningProfileRow
{
	public string GroupLabel { get; internal set; } = string.Empty;
	public string SubGroupLabel { get; internal set; } = string.Empty;
	public string Label { get; internal set; } = string.Empty;
	public string ValueString { get; internal set; } = string.Empty;
	public string DefaultValueString { get; internal set; } = string.Empty;

	// The three levels folded back onto one line, for the places with no room to show them separately (the log line
	// a revert writes).
	public string FullLabel { get => string.Join( " - ", new[] { GroupLabel, SubGroupLabel, Label }.Where( labelPart => labelPart.Length > 0 ) ); }

	// The scope badge the default profile puts beside a setting that is tuned somewhere else (per car, per car +
	// track + weather, ...), saying what the value on this row is the fallback FOR. Empty on every other profile -
	// all of their rows share the profile's own shape, so a badge on each of them would say nothing.
	public string ScopeLabel { get; internal set; } = string.Empty;

	// The same scope broken back out into its four dimensions. The manager draws one small icon per dimension in
	// place of the label (the long combinations ate most of the setting name) and shows the composed string above as
	// the icon cluster's tooltip, so these are all false exactly where ScopeLabel is empty.
	public bool ScopePerCar { get; internal set; }
	public bool ScopePerTrack { get; internal set; }
	public bool ScopePerTrackConfiguration { get; internal set; }
	public bool ScopePerWetDry { get; internal set; }

	// Whether this row's value differs from the one in the column beside it. Only the default profile fills this in -
	// it lists every setting, so it needs to say which of them have been moved off the factory value. Every row on
	// every other profile is a difference by construction, so marking them all would say nothing.
	public bool IsChanged { get; internal set; }
}

// A plain per-context setting row (one of the paired Settings / ContextSettings properties).
public sealed class TuningProfileFlatRow : TuningProfileRow
{
	internal TuningProfileBinding Binding { get; init; } = null!;
}

// An FFB graph module setting row, keyed "{graphId}/{moduleId}/{settingKey}" in the bucket's value overlay.
public sealed class TuningProfileFFBRow : TuningProfileRow
{
	internal string ValueKey { get; init; } = string.Empty;
	internal FFBSettingDescriptor Descriptor { get; init; } = null!;
	internal float Value { get; init; }
	internal float DefaultValue { get; init; }
}

// One logical profile: a physical context bucket seen through one scope shape. A bucket that several shapes cover
// yields several logical profiles - that is correct, each one lists the settings tuned at its own shape.
public sealed class TuningProfile
{
	public TuningProfileShape Shape { get; internal set; }
	public Context Key { get; internal set; } = new();
	public bool IsDefaultProfile { get; internal set; }
	public TuningProfileWeather Weather { get; internal set; }

	// The context label WITHOUT the weather - the manager shows the weather as a badge beside it, and a profile
	// scoped to nothing but the weather is left with an empty label (there the badge is the whole identity).
	public string Label { get; internal set; } = string.Empty;

	// The same label with the weather folded back in, for the places that show a profile on one line with no
	// badge column of their own (the copy target picker, the log lines).
	public string LabelWithWeather { get; internal set; } = string.Empty;

	public bool IsLive { get; internal set; }
	public IReadOnlyList<TuningProfileRow> Rows { get; internal set; } = [];

	internal ContextSettings Bucket { get; init; } = null!;
}

// What a clean up pass removed or repaired (or would remove or repair, on a dry run).
public sealed class TuningProfileCleanUpResult
{
	public int RemovedModuleValueKeys { get; internal set; }
	public int RepairedGraphSelections { get; internal set; }
	public int RemovedUnreachableBuckets { get; internal set; }
	public int RemovedEmptyBuckets { get; internal set; }

	public int Total { get => RemovedModuleValueKeys + RepairedGraphSelections + RemovedUnreachableBuckets + RemovedEmptyBuckets; }
}

// Tuning profile support: the parts of Settings that reason ABOUT the per-context tuning buckets rather than
// holding settings themselves. Kept in its own partial so the (very large) Settings.cs stays a plain settings list.
public partial class Settings
{
	#region Context switch hierarchy

	// Enforces the context switch hierarchy on every scope in the settings file: a per track scope requires a per
	// car scope, and a per track configuration scope requires a per track scope (per wet/dry is independent). An
	// older settings file can hold an illegal combination - and an orphaned child dimension silently keys buckets
	// that no longer match anything the app builds - so this runs once at load, right after the migrations that
	// hand out context switch instances. The 12 pedal slot properties are aliases forwarding to 6 shared instances,
	// so the instances are deduplicated by reference before being normalized. Returns true when anything changed so
	// the caller can persist the cleaned-up file.
	public bool NormalizeContextSwitchHierarchy()
	{
		var app = App.Instance!;

		var visitedContextSwitches = new HashSet<object>( ReferenceEqualityComparer.Instance );

		var changed = false;

		var settingsProperties = typeof( Settings ).GetProperties( BindingFlags.Public | BindingFlags.Instance );

		foreach ( var settingsProperty in settingsProperties )
		{
			if ( settingsProperty.PropertyType == typeof( ContextSwitches ) )
			{
				if ( ( settingsProperty.GetValue( this ) is ContextSwitches contextSwitches ) && visitedContextSwitches.Add( contextSwitches ) )
				{
					changed |= contextSwitches.Normalize();
				}
			}
		}

		if ( changed )
		{
			app.Logger.WriteLine( "[Settings] Normalized the context switch hierarchy (per track requires per car, per track configuration requires per track)" );
		}

		return changed;
	}

	#endregion

	#region Tuning profile bindings

	// The factory values, for the default profile's rows - it sits at the bottom of the stack, so it has no bucket
	// above it to compare against.
	private static readonly ContextSettings PristineContextSettings = new();

	// The factory value of a binding. Almost always the pristine initializer, but the graph selection initializes
	// to an empty string and only becomes real when the built-in graphs are installed - its effective factory value
	// is the flagship built-in, and writing the empty string on a revert would leave the engine with no graph.
	private object? GetTuningProfileFactoryValue( TuningProfileBinding binding )
	{
		if ( binding.PropertyBaseName == nameof( RacingWheelSelectedFFBGraphName ) )
		{
			return FallbackGraphName( RacingWheelFFBGraphs );
		}

		return binding.ContextSettingsProperty.GetValue( PristineContextSettings );
	}

	// The component prefixes every context-scoped settings property carries; used as the group label when the
	// mappable action catalog does not cover the setting.
	private static readonly string[] TuningProfileGroupPrefixes = [ "RacingWheel", "SteeringEffects", "Pedals", "TyphoonWind", "GTensioner" ];

	private static TuningProfileBinding[]? _tuningProfileBindings = null;

	// The context-scoped settings, paired exactly the way the UpdateSettings reflection loop pairs them: a public
	// read/write Settings property that is not a display string, has a "{Name}ContextSwitches" property beside it,
	// and has a matching property on ContextSettings. Cached for the life of the app - but PropertyInfos only,
	// never the ContextSwitches instances (the load-time migrations replace those wholesale, so a cached instance
	// would report a stale scope).
	private static TuningProfileBinding[] BuildBindings()
	{
		if ( _tuningProfileBindings != null )
		{
			return _tuningProfileBindings;
		}

		var bindings = new List<TuningProfileBinding>();

		var settingsProperties = typeof( Settings ).GetProperties( BindingFlags.Public | BindingFlags.Instance );

		foreach ( var settingsProperty in settingsProperties )
		{
			if ( !settingsProperty.CanRead || !settingsProperty.CanWrite || settingsProperty.Name.EndsWith( "String" ) )
			{
				continue;
			}

			var contextSwitchesProperty = typeof( Settings ).GetProperty( $"{settingsProperty.Name}ContextSwitches" );

			if ( ( contextSwitchesProperty == null ) || ( contextSwitchesProperty.PropertyType != typeof( ContextSwitches ) ) )
			{
				continue;
			}

			var contextSettingsProperty = typeof( ContextSettings ).GetProperty( settingsProperty.Name );

			// the FFB module values are a composite-key dictionary rather than a paired value - they ride the graph
			// selection scope and are surfaced as FFB rows instead
			if ( ( contextSettingsProperty == null ) || ( contextSettingsProperty.PropertyType == typeof( FFBGraphValues ) ) )
			{
				continue;
			}

			bindings.Add( new TuningProfileBinding
			{
				PropertyBaseName = settingsProperty.Name,
				SettingsProperty = settingsProperty,
				ContextSettingsProperty = contextSettingsProperty,
				ContextSwitchesProperty = contextSwitchesProperty,
				IsRetired = MappableActionCatalog.IsRetiredSetting( settingsProperty.Name ),
				DisplayStringFormatter = FindDisplayStringFormatter( settingsProperty.Name, settingsProperty.PropertyType )
			} );
		}

		var retiredCount = bindings.Count( binding => binding.IsRetired );
		var formattedCount = bindings.Count( binding => binding.DisplayStringFormatter != null );

		App.Instance?.Logger.WriteLine( $"[Settings] Tuning profiles bound {bindings.Count} context settings ({retiredCount} of them retired)" );
		App.Instance?.Logger.WriteLine( $"[Settings] Tuning profile display strings cover {formattedCount} of {bindings.Count} flat bindings" );

		_tuningProfileBindings = [ .. bindings ];

		return _tuningProfileBindings;
	}

	// Every settings page value that shows as something other than a bare number has a "Format{Name}String" builder
	// beside its property, taking the value to format and defaulting to the live one. Resolved once at table build
	// so the rows can borrow it; anything without one (or with a signature this cannot call) stays on the generic
	// path. The one parameter has to be the nullable form of the setting's own type - "float?" for the numbers, the
	// nullable enum for the choice settings - so a boxed bucket value always lands in it.
	private static MethodInfo? FindDisplayStringFormatter( string propertyBaseName, Type settingsPropertyType )
	{
		var formatter = typeof( Settings ).GetMethod( $"Format{propertyBaseName}String", BindingFlags.NonPublic | BindingFlags.Instance );

		if ( ( formatter == null ) || ( formatter.ReturnType != typeof( string ) ) )
		{
			return null;
		}

		var parameters = formatter.GetParameters();

		return ( ( parameters.Length == 1 ) && ( Nullable.GetUnderlyingType( parameters[ 0 ].ParameterType ) == settingsPropertyType ) ) ? formatter : null;
	}

	// Everything a tuning profile pass needs, snapshotted once: the binding table with each binding's CURRENT
	// scope shape, the graphs indexed by their stable id, the default bucket, and the shapes actually in use with
	// the context each one resolves to right now. Keeps the reflection and the sim lookups out of the inner loops.
	private sealed class TuningProfileScope
	{
		public required TuningProfileBinding[] Bindings;
		public required TuningProfileShape[] BindingShapes;
		public required TuningProfileShape FFBShape;
		public required List<TuningProfileShape> InUseShapes;
		public required List<Context> LiveContexts;
		public required Dictionary<string, FFBGraph> GraphsById;
		public required ContextSettings DefaultContextSettings;

		// Pristine clones of the shipped built-in graph files, one per graph name for the life of this pass - see
		// ResolveFFBDefaultValue. Cloning is not free, so it never happens per row.
		private readonly Dictionary<string, FFBGraph?> _shippedGraphsByName = new( StringComparer.Ordinal );

		public bool IsLiveContext( Context context )
		{
			foreach ( var liveContext in LiveContexts )
			{
				if ( liveContext.CompareTo( context ) == 0 )
				{
					return true;
				}
			}

			return false;
		}

		// THE baseline for an FFB module value, used everywhere a "what would this be without this profile" value
		// is needed: (1) the default bucket's entry for the key, (2) the value the SHIPPED built-in graph file
		// carries, (3) the setting descriptor's default. Step 2 matters a lot - a built-in graph's shipped tuning
		// is nowhere near the descriptor defaults (Strength ships at 0.1 against a default of 1.0), so falling
		// straight through to the descriptor would paint untouched car buckets as changed and stamp raw defaults
		// into the wheel feel on a delete or a revert. The graph's own SettingValues are deliberately NOT the
		// baseline: the read sync writes the active context's values into them, so they are context-tainted.
		// Pass useDefaultBucket false for the default bucket's own rows - nothing sits below it, so its baseline
		// is the factory value alone.
		public float ResolveFFBDefaultValue( FFBGraph graph, FFBModuleData module, FFBSettingDescriptor settingDescriptor, string compositeKey, bool useDefaultBucket = true )
		{
			if ( useDefaultBucket && DefaultContextSettings.RacingWheelFFBGraphModuleValues.TryGetValue( compositeKey, out var storedDefaultValue ) )
			{
				return storedDefaultValue;
			}

			if ( graph.IsBuiltIn )
			{
				if ( !_shippedGraphsByName.TryGetValue( graph.Name, out var shippedGraph ) )
				{
					shippedGraph = FFBBuiltInGraphs.CreateGraph( FFBGraphExportFile.FFBGraphType, graph.Name );

					_shippedGraphsByName[ graph.Name ] = shippedGraph;
				}

				var shippedModule = shippedGraph?.Modules.Find( candidate => candidate.ModuleId == module.ModuleId );

				if ( ( shippedModule != null ) && shippedModule.SettingValues.TryGetValue( settingDescriptor.Key, out var shippedValue ) )
				{
					return shippedValue;
				}
			}

			return settingDescriptor.DefaultValue;
		}
	}

	private TuningProfileScope BuildTuningProfileScope()
	{
		var bindings = BuildBindings();

		var bindingShapes = new TuningProfileShape[ bindings.Length ];

		var inUseShapes = new List<TuningProfileShape>();
		var liveContexts = new List<Context>();

		for ( var bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++ )
		{
			var contextSwitches = (ContextSwitches?) bindings[ bindingIndex ].ContextSwitchesProperty.GetValue( this );

			bindingShapes[ bindingIndex ] = ( contextSwitches != null ) ? TuningProfileShape.From( contextSwitches ) : default;

			AddInUseShape( inUseShapes, liveContexts, bindingShapes[ bindingIndex ] );
		}

		var ffbShape = TuningProfileShape.From( RacingWheelSelectedFFBGraphNameContextSwitches );

		AddInUseShape( inUseShapes, liveContexts, ffbShape );

		return new TuningProfileScope
		{
			Bindings = bindings,
			BindingShapes = bindingShapes,
			FFBShape = ffbShape,
			InUseShapes = inUseShapes,
			LiveContexts = liveContexts,
			GraphsById = BuildGraphsById(),
			DefaultContextSettings = FindContextSettings( new Context() )
		};
	}

	private static void AddInUseShape( List<TuningProfileShape> inUseShapes, List<Context> liveContexts, TuningProfileShape shape )
	{
		if ( !inUseShapes.Contains( shape ) )
		{
			inUseShapes.Add( shape );

			liveContexts.Add( new Context( shape.ToSwitches() ) );
		}
	}

	// Coverage is one-directional: every dimension the shape does NOT own has to sit at its default value in the
	// key, but an owned dimension is allowed to be at its default too (iRacing reports an empty track configuration
	// name on config-less tracks, which lands as "Default"). This is exactly the value range new Context( shape )
	// produces, which is why one physical bucket can legitimately be covered by several shapes at once.
	private static bool ShapeCovers( TuningProfileShape shape, Context context )
	{
		if ( ( shape.Dims < TuningProfileDims.Car ) && ( context.CarName != Context.DefaultContextName ) )
		{
			return false;
		}

		if ( ( shape.Dims < TuningProfileDims.CarTrack ) && ( context.TrackName != Context.DefaultContextName ) )
		{
			return false;
		}

		if ( ( shape.Dims < TuningProfileDims.CarTrackConfig ) && ( context.TrackConfigurationName != Context.DefaultContextName ) )
		{
			return false;
		}

		if ( !shape.PerWetDry && ( context.WetDryName != Context.DryContextName ) )
		{
			return false;
		}

		return true;
	}

	private Dictionary<string, FFBGraph> BuildGraphsById()
	{
		var graphsById = new Dictionary<string, FFBGraph>( StringComparer.Ordinal );

		foreach ( var graph in RacingWheelFFBGraphs.Values )
		{
			if ( !string.IsNullOrEmpty( graph.GraphId ) )
			{
				graphsById.TryAdd( graph.GraphId, graph );
			}
		}

		return graphsById;
	}

	// Resolves a "{graphId}/{moduleId}/{settingKey}" overlay key against the current graphs. Fails on malformed
	// keys and on anything that no longer exists (a deleted graph, a removed node, a setting a module no longer
	// carries) - the callers skip those silently or prune them.
	private static bool TryResolveModuleValueKey( string compositeKey, Dictionary<string, FFBGraph> graphsById, [NotNullWhen( true )] out FFBGraph? graph, [NotNullWhen( true )] out FFBModuleData? module, [NotNullWhen( true )] out FFBSettingDescriptor? settingDescriptor )
	{
		graph = null;
		module = null;
		settingDescriptor = null;

		var keyParts = compositeKey.Split( '/' );

		if ( keyParts.Length != 3 )
		{
			return false;
		}

		if ( !graphsById.TryGetValue( keyParts[ 0 ], out graph ) )
		{
			return false;
		}

		module = graph.Modules.Find( candidate => candidate.ModuleId == keyParts[ 1 ] );

		if ( module == null )
		{
			graph = null;

			return false;
		}

		var moduleDescriptor = FFBModuleRegistry.TryGet( module.ModuleType );

		var settingIndex = moduleDescriptor?.IndexOfSetting( keyParts[ 2 ] ) ?? -1;

		if ( ( moduleDescriptor == null ) || ( settingIndex < 0 ) )
		{
			graph = null;
			module = null;

			return false;
		}

		settingDescriptor = moduleDescriptor.EffectiveSettings[ settingIndex ];

		return true;
	}

	#endregion

	#region Tuning profile model

	// Builds the whole logical profile model: the default profile first (it lists every setting), then one profile
	// per (non-default bucket x covering in-use shape) that actually has something to show. Safe to call at any
	// time, with or without the simulator connected - when it is not connected every shape collapses onto the
	// default bucket, so only the default profile ends up marked live.
	public List<TuningProfile> BuildTuningProfiles()
	{
		var profiles = new List<TuningProfile>();

		lock ( ContextSettingsLock )
		{
			var scope = BuildTuningProfileScope();

			var defaultContext = new Context();

			var defaultProfile = new TuningProfile
			{
				Shape = new TuningProfileShape( TuningProfileDims.None, false ),
				Key = defaultContext,
				Bucket = scope.DefaultContextSettings,
				IsDefaultProfile = true,
				Weather = TuningProfileWeather.Any,
				Label = DataContext.Instance.Localization[ "Default" ],
				LabelWithWeather = DataContext.Instance.Localization[ "Default" ],
				IsLive = true
			};

			defaultProfile.Rows = BuildTuningProfileRows( defaultProfile, scope );

			var otherProfiles = new List<TuningProfile>();

			foreach ( var ( context, contextSettings ) in ContextSettingsDictionary )
			{
				if ( context.CompareTo( defaultContext ) == 0 )
				{
					continue;
				}

				for ( var shapeIndex = 0; shapeIndex < scope.InUseShapes.Count; shapeIndex++ )
				{
					var shape = scope.InUseShapes[ shapeIndex ];

					if ( shape.IsDefault || !ShapeCovers( shape, context ) )
					{
						continue;
					}

					var profile = new TuningProfile
					{
						Shape = shape,
						Key = context,
						Bucket = contextSettings,
						IsDefaultProfile = false,
						Weather = !shape.PerWetDry ? TuningProfileWeather.Any : ( ( context.WetDryName == Context.WetContextName ) ? TuningProfileWeather.Wet : TuningProfileWeather.Dry ),
						Label = DescribeTuningProfileContext( context, shape ),
						LabelWithWeather = DescribeContext( context, shape.ToSwitches() ),
						IsLive = scope.LiveContexts[ shapeIndex ].CompareTo( context ) == 0
					};

					profile.Rows = BuildTuningProfileRows( profile, scope );

					// a bucket covered by a shape none of that shape's settings deviate at is not a profile
					if ( profile.Rows.Count > 0 )
					{
						otherProfiles.Add( profile );
					}
				}
			}

			otherProfiles.Sort( CompareTuningProfiles );

			profiles.Add( defaultProfile );
			profiles.AddRange( otherProfiles );
		}

		return profiles;
	}

	// Specificity first (which is also the manager's grouping: per weather, per car, per car + track, per car +
	// track + configuration), then the context label, then the weather badge.
	private static int CompareTuningProfiles( TuningProfile left, TuningProfile right )
	{
		var dimsComparison = left.Shape.Dims.CompareTo( right.Shape.Dims );

		if ( dimsComparison != 0 )
		{
			return dimsComparison;
		}

		var labelComparison = string.Compare( left.Label, right.Label, StringComparison.CurrentCultureIgnoreCase );

		if ( labelComparison != 0 )
		{
			return labelComparison;
		}

		return left.Weather.CompareTo( right.Weather );
	}

	// The context label a profile row carries: the car / track / configuration dimensions its shape owns, with the
	// weather deliberately left out (the manager prints that as a badge). A shape owning nothing but the weather is
	// left with an empty label rather than "Default" - in that group the badge IS the row.
	private static string DescribeTuningProfileContext( Context context, TuningProfileShape shape )
	{
		if ( shape.Dims == TuningProfileDims.None )
		{
			return string.Empty;
		}

		return DescribeContext( context, new TuningProfileShape( shape.Dims, false ).ToSwitches() );
	}

	// One difference between a bucket and what sits below it, at one shape: either a flat paired setting (Binding
	// set) or one FFB module value key (Graph / Module / SettingDescriptor set).
	private sealed class TuningProfileDifference
	{
		public TuningProfileBinding? Binding { get; init; }
		public object? Value { get; init; }
		public object? DefaultValue { get; init; }

		public string CompositeKey { get; init; } = string.Empty;
		public FFBGraph? Graph { get; init; }
		public FFBModuleData? Module { get; init; }
		public FFBSettingDescriptor? SettingDescriptor { get; init; }
		public float FFBValue { get; init; }
		public float FFBDefaultValue { get; init; }
	}

	// THE definition of "what does this profile change": the flat settings tuned at this shape whose value differs
	// from the default bucket's, then - when the shape is the FFB graph selection's - the FFB module values that
	// differ from their baseline ladder. The row builder dresses these up for display and the clean up pass counts
	// them, so the manager can never show a profile the clean up considers empty (or the other way round).
	// repairedSelections substitutes the graph selections a clean up dry run would have repaired; null everywhere else.
	private static IEnumerable<TuningProfileDifference> EnumerateTuningProfileDifferences( TuningProfileShape shape, ContextSettings contextSettings, TuningProfileScope scope, Dictionary<ContextSettings, string>? repairedSelections = null )
	{
		for ( var bindingIndex = 0; bindingIndex < scope.Bindings.Length; bindingIndex++ )
		{
			var binding = scope.Bindings[ bindingIndex ];

			// retired settings have no UI left anywhere, so they neither show as a row nor keep a bucket alive
			if ( binding.IsRetired || ( scope.BindingShapes[ bindingIndex ] != shape ) )
			{
				continue;
			}

			var value = binding.ContextSettingsProperty.GetValue( contextSettings );
			var defaultValue = binding.ContextSettingsProperty.GetValue( scope.DefaultContextSettings );

			if ( ( repairedSelections != null ) && ( binding.PropertyBaseName == nameof( RacingWheelSelectedFFBGraphName ) ) )
			{
				if ( repairedSelections.TryGetValue( contextSettings, out var repairedSelection ) )
				{
					value = repairedSelection;
				}

				if ( repairedSelections.TryGetValue( scope.DefaultContextSettings, out var repairedDefaultSelection ) )
				{
					defaultValue = repairedDefaultSelection;
				}
			}

			if ( Equals( value, defaultValue ) )
			{
				continue;
			}

			yield return new TuningProfileDifference
			{
				Binding = binding,
				Value = value,
				DefaultValue = defaultValue
			};
		}

		if ( shape != scope.FFBShape )
		{
			yield break;
		}

		foreach ( var ( compositeKey, value ) in contextSettings.RacingWheelFFBGraphModuleValues )
		{
			if ( !TryResolveModuleValueKey( compositeKey, scope.GraphsById, out var graph, out var module, out var settingDescriptor ) )
			{
				continue;
			}

			var defaultValue = scope.ResolveFFBDefaultValue( graph, module, settingDescriptor, compositeKey );

			if ( value == defaultValue )
			{
				continue;
			}

			yield return new TuningProfileDifference
			{
				CompositeKey = compositeKey,
				Graph = graph,
				Module = module,
				SettingDescriptor = settingDescriptor,
				FFBValue = value,
				FFBDefaultValue = defaultValue
			};
		}
	}

	private List<TuningProfileRow> BuildTuningProfileRows( TuningProfile profile, TuningProfileScope scope )
	{
		// the default profile is not a diff - it lists every setting instead - so it is built its own way
		if ( profile.IsDefaultProfile )
		{
			return BuildDefaultTuningProfileRows( scope );
		}

		var rows = new List<TuningProfileRow>();

		var ffbDifferences = new List<TuningProfileDifference>();

		foreach ( var difference in EnumerateTuningProfileDifferences( profile.Shape, profile.Bucket, scope ) )
		{
			if ( difference.Binding != null )
			{
				rows.Add( BuildTuningProfileFlatRow( difference.Binding, difference.Value, difference.DefaultValue ) );
			}
			else
			{
				ffbDifferences.Add( difference );
			}
		}

		// no scope badge on a non-default profile - every row it has is tuned at the profile's own shape, so the
		// default shape is handed in and describes itself as no badge at all
		rows.AddRange( BuildTuningProfileFFBRows( ffbDifferences, profile.Bucket.RacingWheelFFBGraphModuleValues, scope.DefaultContextSettings.RacingWheelFFBGraphModuleValues, default ) );

		return rows;
	}

	// The default profile sits at the bottom of the stack, so it lists EVERY setting rather than a diff, with the
	// factory value as the "without this profile" column. Settings scoped somewhere else are listed too - the value
	// the default bucket holds for them is what every untuned context falls back to - and so are the FFB module
	// values, whatever scope the FFB graph selection happens to be at: the shipped scope is per car, so gating them
	// on the shape would leave the default bucket's FFB fallbacks invisible (and unrevertable) out of the box. Those
	// scoped settings sit in their normal category groups like everything else, carrying a scope badge that says what
	// they are the fallback for - the shipped scope is per car for 58 of them, so pulling them out into a section of
	// their own only buried them.
	private List<TuningProfileRow> BuildDefaultTuningProfileRows( TuningProfileScope scope )
	{
		var rows = new List<TuningProfileRow>();

		for ( var bindingIndex = 0; bindingIndex < scope.Bindings.Length; bindingIndex++ )
		{
			var binding = scope.Bindings[ bindingIndex ];

			// retired settings have no UI left anywhere, so they are hidden here too
			if ( binding.IsRetired )
			{
				continue;
			}

			var value = binding.ContextSettingsProperty.GetValue( scope.DefaultContextSettings );
			var factoryValue = GetTuningProfileFactoryValue( binding );

			var row = BuildTuningProfileFlatRow( binding, value, factoryValue );

			ApplyTuningProfileScope( row, scope.BindingShapes[ bindingIndex ] );

			row.IsChanged = !Equals( value, factoryValue );

			rows.Add( row );
		}

		var ffbDifferences = new List<TuningProfileDifference>();

		foreach ( var ( compositeKey, value ) in scope.DefaultContextSettings.RacingWheelFFBGraphModuleValues )
		{
			if ( !TryResolveModuleValueKey( compositeKey, scope.GraphsById, out var graph, out var module, out var settingDescriptor ) )
			{
				continue;
			}

			ffbDifferences.Add( new TuningProfileDifference
			{
				CompositeKey = compositeKey,
				Graph = graph,
				Module = module,
				SettingDescriptor = settingDescriptor,
				FFBValue = value,

				// nothing sits below the default bucket, so these revert to the factory value - the ladder
				// without its default-bucket step
				FFBDefaultValue = scope.ResolveFFBDefaultValue( graph, module, settingDescriptor, compositeKey, useDefaultBucket: false )
			} );
		}

		var ffbRows = BuildTuningProfileFFBRows( ffbDifferences, scope.DefaultContextSettings.RacingWheelFFBGraphModuleValues, scope.DefaultContextSettings.RacingWheelFFBGraphModuleValues, scope.FFBShape );

		foreach ( var ffbRow in ffbRows )
		{
			if ( ffbRow is TuningProfileFFBRow row )
			{
				row.IsChanged = row.Value != row.DefaultValue;
			}
		}

		rows.AddRange( ffbRows );

		return rows;
	}

	private TuningProfileFlatRow BuildTuningProfileFlatRow( TuningProfileBinding binding, object? value, object? defaultValue )
	{
		var ( groupLabel, subGroupLabel, label ) = DescribeTuningProfileSetting( binding );

		return new TuningProfileFlatRow
		{
			GroupLabel = groupLabel,
			SubGroupLabel = subGroupLabel,
			Label = label,
			ValueString = FormatFlatValue( binding, value ),
			DefaultValueString = FormatFlatValue( binding, defaultValue ),
			Binding = binding
		};
	}

	// valueContext / defaultValueContext are the buckets the two columns are formatted against - a formatter that
	// depends on a sibling setting has to see the values of the bucket being displayed, not the live model.
	// scopeShape is the badge every one of these rows carries (they all ride the FFB graph selection's scope).
	private static List<TuningProfileRow> BuildTuningProfileFFBRows( List<TuningProfileDifference> differences, FFBGraphValues valueContext, FFBGraphValues defaultValueContext, TuningProfileShape scopeShape )
	{
		var orderedRows = new List<(string graphName, int moduleIndex, int settingIndex, TuningProfileRow row)>();

		foreach ( var difference in differences )
		{
			var graph = difference.Graph!;
			var module = difference.Module!;
			var settingDescriptor = difference.SettingDescriptor!;

			var graphLabel = FFBGraphViewModel.GraphDisplayName( graph.Name, graph.IsBuiltIn );
			var moduleLabel = FFBDisplayNames.Module( module.ModuleType );

			var row = new TuningProfileFFBRow
			{
				// the graph carries the section on its own (the rows are ordered graph by graph, so it is printed
				// once) and the module is the group inside it
				GroupLabel = graphLabel,
				SubGroupLabel = moduleLabel,
				Label = FFBDisplayNames.Localize( settingDescriptor.LocalizationKey, FFBDisplayNames.Localize( settingDescriptor.Key, FFBDisplayNames.Humanize( settingDescriptor.Key ) ) ),
				ValueString = FormatFFBValue( valueContext, graph.GraphId, module, settingDescriptor, difference.FFBValue ),
				DefaultValueString = FormatFFBValue( defaultValueContext, graph.GraphId, module, settingDescriptor, difference.FFBDefaultValue ),
				ValueKey = difference.CompositeKey,
				Descriptor = settingDescriptor,
				Value = difference.FFBValue,
				DefaultValue = difference.FFBDefaultValue
			};

			ApplyTuningProfileScope( row, scopeShape );

			orderedRows.Add( ( graph.Name, graph.Modules.IndexOf( module ), FFBModuleRegistry.Get( module.ModuleType ).IndexOfSetting( settingDescriptor.Key ), row ) );
		}

		orderedRows.Sort( ( left, right ) =>
		{
			var graphComparison = string.Compare( left.graphName, right.graphName, StringComparison.CurrentCultureIgnoreCase );

			if ( graphComparison != 0 )
			{
				return graphComparison;
			}

			return ( left.moduleIndex != right.moduleIndex ) ? left.moduleIndex.CompareTo( right.moduleIndex ) : left.settingIndex.CompareTo( right.settingIndex );
		} );

		return orderedRows.Select( orderedRow => orderedRow.row ).ToList();
	}

	#endregion

	#region Tuning profile labels and value strings

	// Both halves of a row's scope badge: the composed string the manager shows as the icon cluster's tooltip, and
	// the same shape broken back out into the four dimensions it draws an icon for. The default shape leaves the row
	// with no badge at all - an empty label and four false flags.
	private static void ApplyTuningProfileScope( TuningProfileRow row, TuningProfileShape shape )
	{
		row.ScopeLabel = DescribeTuningProfileScope( shape );

		row.ScopePerCar = shape.Dims >= TuningProfileDims.Car;
		row.ScopePerTrack = shape.Dims >= TuningProfileDims.CarTrack;
		row.ScopePerTrackConfiguration = shape.Dims >= TuningProfileDims.CarTrackConfig;
		row.ScopePerWetDry = shape.PerWetDry;
	}

	// The scope badge a default profile row carries: which contexts the setting on that row is tuned per, so the
	// value shown is readable as "this is what every context WITHOUT its own value falls back to". A setting tuned at
	// the default shape gets no badge - it has nothing below it, and a badge on nearly every row says nothing.
	private static string DescribeTuningProfileScope( TuningProfileShape shape )
	{
		if ( shape.IsDefault )
		{
			return string.Empty;
		}

		var localizationKey = shape.Dims switch
		{
			TuningProfileDims.Car => shape.PerWetDry ? "TuningProfilesGroupPerCarWeather" : "TuningProfilesGroupPerCar",
			TuningProfileDims.CarTrack => shape.PerWetDry ? "TuningProfilesGroupPerCarTrackWeather" : "TuningProfilesGroupPerCarTrack",
			TuningProfileDims.CarTrackConfig => shape.PerWetDry ? "TuningProfilesGroupPerCarTrackConfigurationWeather" : "TuningProfilesGroupPerCarTrackConfiguration",

			// the only shape left is the weather on its own - the default shape returned above
			_ => "TuningProfilesGroupPerWeather"
		};

		return DataContext.Instance.Localization[ localizationKey ];
	}

	// How a settings page names one control: the section it sits in and the control's own label, with the index
	// appended when the same control repeats down a page (the three pedal effect slots, Clutch strength 1/2/3).
	private sealed record TuningProfileSettingLabels( string GroupLabelKey, string LabelKey, int Index = 0 );

	// The context-scoped settings the mappable action catalog does NOT cover, because they carry no button mapping
	// of their own: switches, mode / algorithm / effect selectors, and the two auto-tune balance weights. Each one is
	// pointed at the very localization keys its own control uses on its settings page, so a row here reads exactly
	// like the control the user tuned - "Enabled" under an "Auto-tune" sub-header rather than a humanized property
	// name. The category is left to the property name prefix (TuningProfileGroupLabelFor), which lands on the right
	// page for all of these.
	private static readonly Dictionary<string, TuningProfileSettingLabels> TuningProfileSettingLabelsByPropertyName = new( StringComparer.Ordinal )
	{
		// ----- Racing wheel -----

		{ nameof( RacingWheelWheelForce ), new TuningProfileSettingLabels( "OverallStrength", "WheelForce" ) },
		{ nameof( RacingWheelSelectedFFBGraphName ), new TuningProfileSettingLabels( "FFBGraph", "Graph" ) },
		{ nameof( RacingWheelFadeEnabled ), new TuningProfileSettingLabels( "Switches", "FadeForceFeedback" ) },

		// ----- Steering effects -----

		{ nameof( SteeringEffectsUndersteerEnabled ), new TuningProfileSettingLabels( "Understeer", "Enabled" ) },
		{ nameof( SteeringEffectsOversteerEnabled ), new TuningProfileSettingLabels( "Oversteer", "Enabled" ) },
		{ nameof( SteeringEffectsSeatOfPantsEnabled ), new TuningProfileSettingLabels( "SeatOfPants", "Enabled" ) },
		{ nameof( SteeringEffectsSeatOfPantsAlgorithm ), new TuningProfileSettingLabels( "SeatOfPants", "Algorithm" ) },

		// ----- Pedals - effect slots -----

		{ nameof( PedalsClutchEffect1 ), new TuningProfileSettingLabels( "Clutch", "PedalEffects", 1 ) },
		{ nameof( PedalsClutchEffect2 ), new TuningProfileSettingLabels( "Clutch", "PedalEffects", 2 ) },
		{ nameof( PedalsClutchEffect3 ), new TuningProfileSettingLabels( "Clutch", "PedalEffects", 3 ) },
		{ nameof( PedalsBrakeEffect1 ), new TuningProfileSettingLabels( "Brake", "PedalEffects", 1 ) },
		{ nameof( PedalsBrakeEffect2 ), new TuningProfileSettingLabels( "Brake", "PedalEffects", 2 ) },
		{ nameof( PedalsBrakeEffect3 ), new TuningProfileSettingLabels( "Brake", "PedalEffects", 3 ) },
		{ nameof( PedalsThrottleEffect1 ), new TuningProfileSettingLabels( "Throttle", "PedalEffects", 1 ) },
		{ nameof( PedalsThrottleEffect2 ), new TuningProfileSettingLabels( "Throttle", "PedalEffects", 2 ) },
		{ nameof( PedalsThrottleEffect3 ), new TuningProfileSettingLabels( "Throttle", "PedalEffects", 3 ) },

		// ----- Pedals - effect switches -----

		{ nameof( PedalsABSEngagedFadeWithBrakeEnabled ), new TuningProfileSettingLabels( "ABSEngaged", "FadeWithBrake" ) },
		{ nameof( PedalsRPMVibrateInTopGearEnabled ), new TuningProfileSettingLabels( "RPM", "VibrateInTopGear" ) },
		{ nameof( PedalsRPMFadeWithThrottleEnabled ), new TuningProfileSettingLabels( "RPM", "FadeWithThrottle" ) },
		{ nameof( PedalsShiftRPMPulsateEnabled ), new TuningProfileSettingLabels( "ShiftRPM", "Pulsate" ) },
		{ nameof( PedalsWheelLockFadeWithBrakeEnabled ), new TuningProfileSettingLabels( "WheelLock", "FadeWithBrake" ) },
		{ nameof( PedalsWheelSpinFadeWithThrottleEnabled ), new TuningProfileSettingLabels( "WheelSpin", "FadeWithThrottle" ) },

		// ----- G Tensioner - auto-tune (the two weights share one balance control, so each takes its own axis name) -----

		{ nameof( GTensionerAutoTuneEnabled ), new TuningProfileSettingLabels( "AutoTune", "Enabled" ) },
		{ nameof( GTensionerAutoTuneSwayWeight ), new TuningProfileSettingLabels( "Balance", "Sway" ) },
		{ nameof( GTensionerAutoTuneSurgeWeight ), new TuningProfileSettingLabels( "Balance", "Surge" ) },

		// ----- G Tensioner - surge -----

		{ nameof( GTensionerSurgeMode ), new TuningProfileSettingLabels( "Surge", "AxisMode" ) },
		{ nameof( GTensionerSurgeSubtractGravity ), new TuningProfileSettingLabels( "Surge", "SubtractGravity" ) },
		{ nameof( GTensionerSurgeMaxG ), new TuningProfileSettingLabels( "Surge", "MaxGForce" ) },
		{ nameof( GTensionerSurgeDeadZone ), new TuningProfileSettingLabels( "Surge", "DeadZone" ) },
		{ nameof( GTensionerSurgeSmoothing ), new TuningProfileSettingLabels( "Surge", "Smoothing" ) },
		{ nameof( GTensionerSurgeCurve ), new TuningProfileSettingLabels( "Surge", "Curve" ) },

		// ----- G Tensioner - sway -----

		{ nameof( GTensionerSwayMode ), new TuningProfileSettingLabels( "Sway", "AxisMode" ) },
		{ nameof( GTensionerSwaySubtractGravity ), new TuningProfileSettingLabels( "Sway", "SubtractGravity" ) },
		{ nameof( GTensionerSwayMaxG ), new TuningProfileSettingLabels( "Sway", "MaxGForce" ) },
		{ nameof( GTensionerSwayDeadZone ), new TuningProfileSettingLabels( "Sway", "DeadZone" ) },
		{ nameof( GTensionerSwaySmoothing ), new TuningProfileSettingLabels( "Sway", "Smoothing" ) },
		{ nameof( GTensionerSwayCurve ), new TuningProfileSettingLabels( "Sway", "Curve" ) },

		// ----- G Tensioner - heave -----

		{ nameof( GTensionerHeaveMode ), new TuningProfileSettingLabels( "Heave", "AxisMode" ) },
		{ nameof( GTensionerHeaveSubtractGravity ), new TuningProfileSettingLabels( "Heave", "SubtractGravity" ) },
		{ nameof( GTensionerHeaveMaxG ), new TuningProfileSettingLabels( "Heave", "MaxGForce" ) },
		{ nameof( GTensionerHeaveDeadZone ), new TuningProfileSettingLabels( "Heave", "DeadZone" ) },
		{ nameof( GTensionerHeaveSmoothing ), new TuningProfileSettingLabels( "Heave", "Smoothing" ) },
		{ nameof( GTensionerHeaveCurve ), new TuningProfileSettingLabels( "Heave", "Curve" ) },

		// ----- G Tensioner - seat-of-pants effect -----

		{ nameof( GTensionerSeatOfPantsMode ), new TuningProfileSettingLabels( "SeatOfPantsEffect", "AxisMode" ) },
		{ nameof( GTensionerSeatOfPantsCurve ), new TuningProfileSettingLabels( "SeatOfPantsEffect", "Curve" ) }
	};

	// The mappable action catalog already carries a localized category / section / label for every control it can
	// map, so a setting it covers is described exactly the way the controller profiles page describes it. A setting
	// with no mapping is described the same way from the table above, and anything neither of them knows falls back
	// to its own property name (localized when a key happens to exist, humanized otherwise).
	private static (string groupLabel, string subGroupLabel, string label) DescribeTuningProfileSetting( TuningProfileBinding binding )
	{
		var localization = DataContext.Instance.Localization;

		if ( MappableActionCatalog.TryGetLabels( binding.PropertyBaseName, out var categoryKey, out var groupLabelKey, out var labelKey, out var index ) )
		{
			var ( catalogSubGroupLabel, catalogLabel ) = DescribeTuningProfileSettingParts( groupLabelKey, labelKey, index );

			return ( localization[ categoryKey ], catalogSubGroupLabel, catalogLabel );
		}

		if ( TuningProfileSettingLabelsByPropertyName.TryGetValue( binding.PropertyBaseName, out var settingLabels ) )
		{
			var ( mappedSubGroupLabel, mappedLabel ) = DescribeTuningProfileSettingParts( settingLabels.GroupLabelKey, settingLabels.LabelKey, settingLabels.Index );

			return ( TuningProfileGroupLabelFor( binding.PropertyBaseName ), mappedSubGroupLabel, mappedLabel );
		}

		return ( TuningProfileGroupLabelFor( binding.PropertyBaseName ), string.Empty, FFBDisplayNames.Localize( binding.PropertyBaseName, FFBDisplayNames.Humanize( binding.PropertyBaseName ) ) );
	}

	// The lower two levels of a control's name on its settings page: the section it sits in and the control's own
	// label, with the index appended when the same control repeats down the page. The section is dropped when it IS
	// the control's label - a sub-header there would only repeat the row under it.
	private static (string subGroupLabel, string label) DescribeTuningProfileSettingParts( string groupLabelKey, string labelKey, int index )
	{
		var localization = DataContext.Instance.Localization;

		var label = localization[ labelKey ];

		if ( index > 0 )
		{
			label = $"{label} {index}";
		}

		return ( ( groupLabelKey != labelKey ) ? localization[ groupLabelKey ] : string.Empty, label );
	}

	private static string TuningProfileGroupLabelFor( string propertyBaseName )
	{
		foreach ( var prefix in TuningProfileGroupPrefixes )
		{
			if ( propertyBaseName.StartsWith( prefix, StringComparison.Ordinal ) )
			{
				return FFBDisplayNames.Localize( prefix, FFBDisplayNames.Humanize( prefix ) );
			}
		}

		return FFBDisplayNames.Localize( "Settings", "Settings" );
	}

	// The property names whose formatter has thrown, so a broken one is reported once instead of once per row.
	private static readonly HashSet<string> _failedDisplayStringFormatters = new( StringComparer.Ordinal );

	private string FormatFlatValue( TuningProfileBinding binding, object? value )
	{
		var localization = DataContext.Instance.Localization;

		// a number or a choice with a display string builder of its own renders exactly the way its settings page
		// control renders it - the builder's parameter type was matched to this binding at table build, so the
		// boxed bucket value goes straight into it
		if ( ( binding.DisplayStringFormatter != null ) && ( value is float or Enum ) )
		{
			var formattedValue = InvokeDisplayStringFormatter( binding, value );

			if ( formattedValue != null )
			{
				return formattedValue;
			}
		}

		switch ( value )
		{
			case null:

				return string.Empty;

			case bool boolValue:

				return localization[ boolValue ? "ON" : "OFF" ];

			case float floatValue:

				// current culture, to match the number the graph editor and the settings pages show for the
				// same value (a German user would otherwise see 0,5 there and 0.5 here)
				return floatValue.ToString( "0.####" );

			case Enum enumValue:

				return FFBDisplayNames.Localize( enumValue.ToString(), FFBDisplayNames.Humanize( enumValue.ToString() ) );

			case string stringValue:

				return ( binding.PropertyBaseName == nameof( RacingWheelSelectedFFBGraphName ) ) ? FormatGraphNameValue( stringValue ) : stringValue;

			default:

				return value.ToString() ?? string.Empty;
		}
	}

	// Runs the setting's own display string builder for a bucket's value. It runs against the LIVE settings instance
	// on purpose: only the value being formatted comes from the bucket, while a formatter that scales by a sibling
	// (the wheel force behind every torque reading, the seat of pants algorithm behind its units) reads that sibling
	// live - which is exactly what every other display of these strings does. Returns null when it could not run,
	// leaving the caller on the generic formatting.
	private string? InvokeDisplayStringFormatter( TuningProfileBinding binding, object value )
	{
		try
		{
			return binding.DisplayStringFormatter!.Invoke( this, [ value ] ) as string;
		}
		catch ( Exception exception )
		{
			if ( _failedDisplayStringFormatters.Add( binding.PropertyBaseName ) )
			{
				App.Instance?.Logger.WriteLine( $"[Settings] Tuning profile display string for {binding.PropertyBaseName} could not be formatted ({( exception.InnerException ?? exception ).Message})" );
			}

			return null;
		}
	}

	private string FormatGraphNameValue( string graphName )
	{
		if ( !string.IsNullOrEmpty( graphName ) && RacingWheelFFBGraphs.TryGetValue( graphName, out var graph ) )
		{
			return FFBGraphViewModel.GraphDisplayName( graph.Name, graph.IsBuiltIn );
		}

		return FFBDisplayNames.Localize( "TuningProfilesNoGraph", "(no graph)" );
	}

	private static string FormatFFBValue( FFBGraphValues contextValues, string graphId, FFBModuleData module, FFBSettingDescriptor settingDescriptor, float value )
	{
		if ( settingDescriptor.Type == FFBSettingType.Switch )
		{
			return DataContext.Instance.Localization[ ( value != 0f ) ? "ON" : "OFF" ];
		}

		if ( ( settingDescriptor.Type == FFBSettingType.Choice ) && ( settingDescriptor.ChoiceLocalizationKeys is { Length: > 0 } choiceLocalizationKeys ) )
		{
			var choiceIndex = Math.Clamp( (int) value, 0, choiceLocalizationKeys.Length - 1 );

			return FFBDisplayNames.Localize( choiceLocalizationKeys[ choiceIndex ], FFBDisplayNames.Humanize( choiceLocalizationKeys[ choiceIndex ] ) );
		}

		if ( settingDescriptor.FormatValue == null )
		{
			// current culture, matching the graph editor's own unformatted fallback (FFBSettingViewModel.Format)
			return value.ToString( "0.####" );
		}

		// a formatter that depends on a sibling setting (a mode switch changing the unit, say) has to see the
		// values of the bucket being displayed - not the live model, which usually belongs to another context
		return settingDescriptor.FormatValue( new FFBFormatContext( value, ( siblingKey, fallback ) =>
		{
			if ( contextValues.TryGetValue( FFBGraphValues.ComposeKey( graphId, module.ModuleId, siblingKey ), out var siblingValue ) )
			{
				return siblingValue;
			}

			return module.SettingValues.TryGetValue( siblingKey, out var baselineValue ) ? baselineValue : fallback;
		} ) );
	}

	#endregion

	#region Tuning profile mutations

	// Pulls the (now changed) values for the currently active context back into the live settings, rebuilding the
	// live FFB engine, the graph editor, and the overlay layout on the way. Must run OUTSIDE the context settings
	// lock - and the preview has to be poked by hand, since the read path never sets that flag itself.
	private void AfterTuningProfileMutation( bool touchesFFB )
	{
		var app = App.Instance!;

		UpdateSettings( false );

		if ( touchesFFB )
		{
			app.RacingWheel.UpdateAlgorithmPreview = true;
		}

		app.SettingsFile.QueueForSerialization = true;
	}

	// Deletes a profile by writing the default profile's values over everything the profile's shape owns. The
	// physical bucket only goes away when nothing can land on it again (see below); the default profile itself can
	// never be deleted.
	public bool DeleteTuningProfile( TuningProfile profile )
	{
		if ( profile.IsDefaultProfile )
		{
			return false;
		}

		var touchesFFB = false;

		lock ( ContextSettingsLock )
		{
			if ( !ContextSettingsDictionary.TryGetValue( profile.Key, out var contextSettings ) )
			{
				return false;
			}

			var scope = BuildTuningProfileScope();

			// retired settings are written back too: they are hidden from the UI, but a stale value left in the
			// bucket would still be pushed into the live settings the next time this context goes active
			for ( var bindingIndex = 0; bindingIndex < scope.Bindings.Length; bindingIndex++ )
			{
				if ( scope.BindingShapes[ bindingIndex ] == profile.Shape )
				{
					var binding = scope.Bindings[ bindingIndex ];

					binding.ContextSettingsProperty.SetValue( contextSettings, binding.ContextSettingsProperty.GetValue( scope.DefaultContextSettings ) );
				}
			}

			if ( profile.Shape == scope.FFBShape )
			{
				touchesFFB = true;

				ResetFFBModuleValues( contextSettings, scope );
			}

			// FindContextSettings re-creates a missing bucket seeded from the LIVE values, so physically removing a
			// bucket the current sim state still resolves to would resurrect it holding exactly what we just
			// cleared. And ONE bucket can back several logical profiles at once (a {Car} shape and a
			// {Car + wet/dry} shape both cover the same key), so dropping it because THIS shape is now empty would
			// silently delete the sibling shapes' tuning with it. Both have to be clear before the entry goes; a
			// bucket left behind is swept up by the clean up pass once it really has nothing left to say.
			if ( !scope.IsLiveContext( profile.Key ) && !BucketHasTuningProfileDifferences( profile.Key, contextSettings, scope ) )
			{
				ContextSettingsDictionary.Remove( profile.Key );
			}
		}

		AfterTuningProfileMutation( touchesFFB );

		return true;
	}

	// A missing module value key is NOT the same as a default one: the read sync only writes a module value when
	// the context has an entry for it (it has no else branch), so removing a key would leave the module holding
	// whatever the previously active context put there. Every resolvable key is therefore WRITTEN back to its
	// baseline (see TuningProfileScope.ResolveFFBDefaultValue); only keys that resolve to nothing at all are removed.
	private static void ResetFFBModuleValues( ContextSettings contextSettings, TuningProfileScope scope )
	{
		foreach ( var compositeKey in contextSettings.RacingWheelFFBGraphModuleValues.Keys.ToArray() )
		{
			if ( TryResolveModuleValueKey( compositeKey, scope.GraphsById, out var graph, out var module, out var settingDescriptor ) )
			{
				contextSettings.RacingWheelFFBGraphModuleValues[ compositeKey ] = scope.ResolveFFBDefaultValue( graph, module, settingDescriptor, compositeKey );
			}
			else
			{
				contextSettings.RacingWheelFFBGraphModuleValues.Remove( compositeKey );
			}
		}
	}

	// Puts one row back to what it would be without this profile: the default profile's value, or the factory
	// value when the row belongs to the default profile itself (nothing sits below it).
	public bool RevertTuningProfileRow( TuningProfile profile, TuningProfileRow row )
	{
		var touchesFFB = false;

		lock ( ContextSettingsLock )
		{
			var defaultContextSettings = FindContextSettings( new Context() );

			var contextSettings = defaultContextSettings;

			if ( !profile.IsDefaultProfile && !ContextSettingsDictionary.TryGetValue( profile.Key, out contextSettings! ) )
			{
				return false;
			}

			if ( row is TuningProfileFlatRow flatRow )
			{
				var binding = flatRow.Binding;

				binding.ContextSettingsProperty.SetValue( contextSettings, profile.IsDefaultProfile ? GetTuningProfileFactoryValue( binding ) : binding.ContextSettingsProperty.GetValue( defaultContextSettings ) );

				touchesFFB = binding.PropertyBaseName == nameof( RacingWheelSelectedFFBGraphName );
			}
			else if ( row is TuningProfileFFBRow ffbRow )
			{
				// written, never removed - see ResetFFBModuleValues. The row already carries the baseline the
				// ladder resolved for it (the factory value when the row belongs to the default profile).
				contextSettings.RacingWheelFFBGraphModuleValues[ ffbRow.ValueKey ] = ffbRow.DefaultValue;

				touchesFFB = true;
			}
			else
			{
				return false;
			}
		}

		AfterTuningProfileMutation( touchesFFB );

		return true;
	}

	// Copies everything one profile owns onto another profile of the SAME shape (the manager only offers same-shape
	// targets - the two profiles would otherwise own different sets of settings).
	public bool CopyTuningProfile( TuningProfile source, TuningProfile destination )
	{
		if ( ( source.Shape != destination.Shape ) || ( source.Key.CompareTo( destination.Key ) == 0 ) )
		{
			return false;
		}

		var touchesFFB = false;

		lock ( ContextSettingsLock )
		{
			if ( !ContextSettingsDictionary.TryGetValue( source.Key, out var sourceContextSettings ) )
			{
				return false;
			}

			var scope = BuildTuningProfileScope();

			var destinationContextSettings = FindContextSettings( destination.Key );

			for ( var bindingIndex = 0; bindingIndex < scope.Bindings.Length; bindingIndex++ )
			{
				if ( scope.BindingShapes[ bindingIndex ] == source.Shape )
				{
					var binding = scope.Bindings[ bindingIndex ];

					binding.ContextSettingsProperty.SetValue( destinationContextSettings, binding.ContextSettingsProperty.GetValue( sourceContextSettings ) );
				}
			}

			if ( source.Shape == scope.FFBShape )
			{
				touchesFFB = true;

				// every graph's values come along, not just the selected one: the copied graph selection has to stay
				// consistent with the values stored for it. Written, never removed (see ResetFFBModuleValues) - a
				// destination key the source has no entry for is RESET to its baseline rather than dropped, so the
				// destination's own deviation for some other graph goes back to the fallback instead of being left
				// to leak into the live modules. Keys that resolve to nothing at all are still dropped.
				foreach ( var compositeKey in destinationContextSettings.RacingWheelFFBGraphModuleValues.Keys.ToArray() )
				{
					if ( !TryResolveModuleValueKey( compositeKey, scope.GraphsById, out var graph, out var module, out var settingDescriptor ) )
					{
						destinationContextSettings.RacingWheelFFBGraphModuleValues.Remove( compositeKey );
					}
					else if ( !sourceContextSettings.RacingWheelFFBGraphModuleValues.ContainsKey( compositeKey ) )
					{
						destinationContextSettings.RacingWheelFFBGraphModuleValues[ compositeKey ] = scope.ResolveFFBDefaultValue( graph, module, settingDescriptor, compositeKey );
					}
				}

				foreach ( var ( compositeKey, value ) in sourceContextSettings.RacingWheelFFBGraphModuleValues )
				{
					if ( TryResolveModuleValueKey( compositeKey, scope.GraphsById, out _, out _, out _ ) )
					{
						destinationContextSettings.RacingWheelFFBGraphModuleValues[ compositeKey ] = value;
					}
				}
			}
		}

		AfterTuningProfileMutation( touchesFFB );

		return true;
	}

	// Makes a profile's tuning the new fallback for every context that has none of its own. The source profile is
	// left alone (it becomes an empty diff, which the clean up pass then drops).
	public bool PromoteTuningProfileToDefault( TuningProfile profile )
	{
		if ( profile.IsDefaultProfile )
		{
			return false;
		}

		var touchesFFB = false;

		lock ( ContextSettingsLock )
		{
			if ( !ContextSettingsDictionary.TryGetValue( profile.Key, out var contextSettings ) )
			{
				return false;
			}

			var scope = BuildTuningProfileScope();

			for ( var bindingIndex = 0; bindingIndex < scope.Bindings.Length; bindingIndex++ )
			{
				if ( scope.BindingShapes[ bindingIndex ] == profile.Shape )
				{
					var binding = scope.Bindings[ bindingIndex ];

					binding.ContextSettingsProperty.SetValue( scope.DefaultContextSettings, binding.ContextSettingsProperty.GetValue( contextSettings ) );
				}
			}

			if ( profile.Shape == scope.FFBShape )
			{
				touchesFFB = true;

				// merged, never cleared: the default bucket is the universal fallback, so it carries the values of
				// every graph and every other shape as well
				foreach ( var ( compositeKey, value ) in contextSettings.RacingWheelFFBGraphModuleValues )
				{
					if ( TryResolveModuleValueKey( compositeKey, scope.GraphsById, out _, out _, out _ ) )
					{
						scope.DefaultContextSettings.RacingWheelFFBGraphModuleValues[ compositeKey ] = value;
					}
				}
			}
		}

		AfterTuningProfileMutation( touchesFFB );

		return true;
	}

	#endregion

	#region Tuning profile clean up

	// Four passes over the saved buckets: prune module values that no longer resolve, repair empty or dangling
	// graph selections, drop buckets nothing can reach any more, and drop buckets that no longer say anything
	// different. Pass apply = false for a dry run - it counts exactly what an apply would do without touching
	// anything (the repaired selections of pass 2 are carried through pass 4 in memory so both agree).
	public TuningProfileCleanUpResult CleanUpTuningProfiles( bool apply )
	{
		var result = new TuningProfileCleanUpResult();

		lock ( ContextSettingsLock )
		{
			var scope = BuildTuningProfileScope();

			var defaultContext = new Context();

			// ----- pass 1: module value keys that no longer resolve (deleted graphs, removed nodes, malformed keys)

			foreach ( var contextSettings in ContextSettingsDictionary.Values )
			{
				foreach ( var compositeKey in contextSettings.RacingWheelFFBGraphModuleValues.Keys.ToArray() )
				{
					if ( !TryResolveModuleValueKey( compositeKey, scope.GraphsById, out _, out _, out _ ) )
					{
						result.RemovedModuleValueKeys++;

						if ( apply )
						{
							contextSettings.RacingWheelFFBGraphModuleValues.Remove( compositeKey );
						}
					}
				}
			}

			// ----- pass 2: empty or dangling graph selections. Deleting a graph blanks the selection in every
			// bucket that used it but only repairs the live one, so a bucket can be left pointing at nothing.

			var repairedSelections = new Dictionary<ContextSettings, string>();

			var defaultSelection = scope.DefaultContextSettings.RacingWheelSelectedFFBGraphName;

			if ( !RacingWheelFFBGraphs.ContainsKey( defaultSelection ) )
			{
				defaultSelection = FallbackGraphName( RacingWheelFFBGraphs );

				result.RepairedGraphSelections++;

				repairedSelections[ scope.DefaultContextSettings ] = defaultSelection;

				if ( apply )
				{
					scope.DefaultContextSettings.RacingWheelSelectedFFBGraphName = defaultSelection;
				}
			}

			foreach ( var contextSettings in ContextSettingsDictionary.Values )
			{
				if ( ( contextSettings == scope.DefaultContextSettings ) || RacingWheelFFBGraphs.ContainsKey( contextSettings.RacingWheelSelectedFFBGraphName ) )
				{
					continue;
				}

				result.RepairedGraphSelections++;

				repairedSelections[ contextSettings ] = defaultSelection;

				if ( apply )
				{
					contextSettings.RacingWheelSelectedFFBGraphName = defaultSelection;
				}
			}

			// ----- pass 3: buckets no in-use shape covers any more (the scope they were made at was turned off)

			var removedContexts = new List<Context>();

			foreach ( var ( context, contextSettings ) in ContextSettingsDictionary )
			{
				// a live context key always stays, even when no shape covers it - the degenerate case where the
				// simulator reports an empty car name lands on a key the app is actively writing to
				if ( ( context.CompareTo( defaultContext ) == 0 ) || scope.IsLiveContext( context ) )
				{
					continue;
				}

				var covered = false;

				foreach ( var shape in scope.InUseShapes )
				{
					covered |= ShapeCovers( shape, context );
				}

				if ( !covered )
				{
					result.RemovedUnreachableBuckets++;

					removedContexts.Add( context );
				}
			}

			// ----- pass 4: buckets that say nothing different any more. A deviation in a retired setting does not
			// count as a difference here, so a bucket left holding only retired leftovers is deletable.

			foreach ( var ( context, contextSettings ) in ContextSettingsDictionary )
			{
				if ( ( context.CompareTo( defaultContext ) == 0 ) || scope.IsLiveContext( context ) || removedContexts.Contains( context ) )
				{
					continue;
				}

				if ( !BucketHasTuningProfileDifferences( context, contextSettings, scope, repairedSelections ) )
				{
					result.RemovedEmptyBuckets++;

					removedContexts.Add( context );
				}
			}

			if ( apply )
			{
				foreach ( var context in removedContexts )
				{
					ContextSettingsDictionary.Remove( context );
				}
			}
		}

		if ( apply )
		{
			AfterTuningProfileMutation( true );
		}

		return result;
	}

	// How many rows the profile at this shape would show - the same enumeration the manager builds its rows from,
	// counted instead of formatted. repairedSelections lets a dry run count against the graph selections pass 2
	// would have repaired.
	private static int CountTuningProfileDifferences( TuningProfileShape shape, ContextSettings contextSettings, TuningProfileScope scope, Dictionary<ContextSettings, string>? repairedSelections = null )
	{
		return EnumerateTuningProfileDifferences( shape, contextSettings, scope, repairedSelections ).Count();
	}

	// True when ANY in-use shape covering this bucket still says something different - the whole-bucket test the
	// clean up pass and the delete guard share, so the two can never disagree about whether a bucket is empty.
	private static bool BucketHasTuningProfileDifferences( Context context, ContextSettings contextSettings, TuningProfileScope scope, Dictionary<ContextSettings, string>? repairedSelections = null )
	{
		foreach ( var shape in scope.InUseShapes )
		{
			if ( !shape.IsDefault && ShapeCovers( shape, context ) && ( CountTuningProfileDifferences( shape, contextSettings, scope, repairedSelections ) > 0 ) )
			{
				return true;
			}
		}

		return false;
	}

	#endregion
}
